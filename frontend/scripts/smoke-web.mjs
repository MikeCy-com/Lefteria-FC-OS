import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { cp, mkdtemp, rm } from 'node:fs/promises';
import { createServer } from 'node:net';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';

const frontend = fileURLToPath(new URL('../', import.meta.url));
const tempRoot = path.join(frontend, '.svelte-kit');
const directory = await mkdtemp(path.join(tempRoot, 'runtime-smoke-'));
let child;
try {
  await cp(path.join(frontend, 'build'), path.join(directory, 'build'), { recursive: true });
  for (const file of ['package.json', 'package-lock.json']) {
    await cp(path.join(frontend, file), path.join(directory, file));
  }
  if (!process.env.npm_execpath) throw new Error('Run this check with npm run smoke:web.');
  const installer = spawn(process.execPath, [process.env.npm_execpath, 'ci', '--omit=dev', '--offline', '--no-audit', '--no-fund'], {
    cwd: directory, stdio: 'inherit', windowsHide: true
  });
  const [code] = await once(installer, 'exit');
  assert.equal(code, 0, 'Production dependencies must restore from the verified npm cache.');

  const listener = createServer();
  listener.listen(0, '127.0.0.1');
  await once(listener, 'listening');
  const port = listener.address().port;
  await new Promise((resolve, reject) => listener.close((error) => error ? reject(error) : resolve()));
  const origin = `http://127.0.0.1:${port}`;
  child = spawn(process.execPath, ['build'], {
    cwd: directory, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'],
    env: { ...process.env, NODE_ENV: 'production', HOST: '127.0.0.1', PORT: String(port), ORIGIN: origin,
      INTERNAL_API_ORIGIN: 'http://127.0.0.1:1' }
  });
  let logs = '';
  let startupError;
  child.on('error', (error) => { startupError = error; });
  for (const stream of [child.stdout, child.stderr]) {
    stream.on('data', (chunk) => { logs = (logs + chunk).slice(-4000); });
  }
  let response;
  const deadline = Date.now() + 15000;
  while (Date.now() < deadline) {
    if (startupError) throw startupError;
    if (child.exitCode !== null) throw new Error(`Production web host exited. ${logs}`);
    try {
      response = await fetch(origin, { headers: { cookie: 'smoke_fixture=valid' }, signal: AbortSignal.timeout(1000) });
      if (response.ok) break;
    } catch { /* Wait for the host to bind its local port. */ }
    await delay(100);
  }
  assert.equal(response?.status, 200, `Production host must serve the page. ${logs}`);
  const home = await response.text();
  assert.match(home, /ΛΕΥΤΕΡΙΑ FC/);
  assert.match(home, /Η ΕΠΙΣΗΜΗ ΙΣΤΟΣΕΛΙΔΑ/);
  for (const pathname of ['/about', '/team', '/academy', '/news', '/contact', '/admin', '/admin/login']) {
    const page = await fetch(origin + pathname);
    assert.equal(page.status, 200, `Public/admin path ${pathname} must render without private API or browser storage.`);
    assert.doesNotMatch(await page.text(), /fixture-password|password_hash|twilio_auth_token/);
  }
  for (const pathname of ['/app/login', '/app', '/app/calendar']) {
    const mobile = await fetch(origin + pathname);
    assert.equal(mobile.status, 200, `Mobile path ${pathname} must render without browser storage on the server.`);
    assert.match(await mobile.text(), /Έλεγχος σύνδεσης/);
  }
  console.log('Production web smoke passed with build, package.json and production-only dependencies.');
} finally {
  if (child?.pid && child.exitCode === null && child.signalCode === null) {
    const exited = once(child, 'exit');
    child.kill();
    await Promise.race([exited, delay(5000, undefined, { ref: false }).then(() => child.kill('SIGKILL'))]);
  }
  // Only remove this check's freshly-created disposable directory.
  if (!path.resolve(directory).startsWith(path.resolve(tempRoot) + path.sep)) {
    throw new Error('Refusing cleanup outside the runtime smoke directory.');
  }
  await rm(directory, { recursive: true, force: true });
}
