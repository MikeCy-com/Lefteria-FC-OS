import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { mkdir, writeFile } from 'node:fs/promises';
import { createServer } from 'node:net';
import { fileURLToPath } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';

const root = new URL('../../', import.meta.url);
const listener = createServer();
listener.listen(0, '127.0.0.1');
await once(listener, 'listening');
const port = listener.address().port;
await new Promise((resolve, reject) => listener.close((error) => error ? reject(error) : resolve()));
const origin = `http://127.0.0.1:${port}`;
const host = process.env.DOTNET_HOST_PATH || 'dotnet';
const dll = fileURLToPath(new URL('backend/src/LefteriaFc.Api/bin/Release/net10.0/LefteriaFc.Api.dll', root));

// This host always uses a deliberately closed loopback database port. Export
// and smoke checks must never inherit an operator's real database credentials.
const child = spawn(host, [dll, '--urls', origin, '--environment', 'Development'], {
  cwd: fileURLToPath(root),
  env: {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: 'Development',
    ASPNETCORE_URLS: origin,
    DOTNET_ENVIRONMENT: 'Development',
    DOTNET_URLS: origin,
    ConnectionStrings__Postgres: 'Host=127.0.0.1;Port=1;Database=contract;Username=contract;Password=contract-only;Timeout=1'
  },
  stdio: ['ignore', 'pipe', 'pipe'],
  windowsHide: true
});
let logs = '';
let startupError;
child.on('error', (error) => { startupError = error; });
for (const stream of [child.stdout, child.stderr]) {
  stream.on('data', (chunk) => { logs = (logs + chunk).slice(-8000); });
}

try {
  let response;
  let lastAttempt = 'no response';
  const deadline = Date.now() + 30000;
  while (Date.now() < deadline) {
    if (startupError) throw startupError;
    if (child.exitCode !== null) throw new Error(`API exited during startup. ${logs}`);
    try {
      response = await fetch(`${origin}/api/openapi/v1.json`, { signal: AbortSignal.timeout(1000) });
      if (response.ok) break;
      lastAttempt = `HTTP ${response.status}`;
    } catch (error) {
      lastAttempt = `${error.message} (${error.cause?.code ?? error.name})`;
    }
    await delay(100);
  }
  if (!response?.ok) throw new Error(`OpenAPI endpoint was unavailable: ${lastAttempt}. ${logs}`);
  const document = await response.json();
  assert.ok(document.paths['/api/health/live']);
  assert.ok(document.paths['/api/health/ready'].get.responses['503']);

  const live = await fetch(`${origin}/api/health/live`);
  assert.equal(live.status, 200);
  assert.deepEqual(await live.json(), { status: 'live' });
  const ready = await fetch(`${origin}/api/health/ready`, { signal: AbortSignal.timeout(5000) });
  assert.equal(ready.status, 503);
  assert.deepEqual(await ready.json(), { status: 'unavailable' });

  // Runtime URLs vary by environment and are not part of the client contract.
  delete document.servers;
  const canonical = (value) => Array.isArray(value)
    ? value.map(canonical)
    : value && typeof value === 'object'
      ? Object.fromEntries(Object.keys(value).sort().map((key) => [key, canonical(value[key])]))
      : value;
  const directory = new URL('contracts/', root);
  await mkdir(directory, { recursive: true });
  await writeFile(new URL('openapi.json', directory), JSON.stringify(canonical(document), null, 2) + '\n');
  console.log('API smoke checks passed: liveness 200, unavailable database 503, typed OpenAPI exported.');
} finally {
  if (child.pid && child.exitCode === null && child.signalCode === null) {
    const exited = once(child, 'exit');
    child.kill();
    await Promise.race([exited, delay(5000, undefined, { ref: false }).then(() => child.kill('SIGKILL'))]);
  }
}
