import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { mkdir, writeFile } from 'node:fs/promises';
import { createServer } from 'node:net';
import { fileURLToPath } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';
import { randomBytes } from 'node:crypto';

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
    MobileAuth__SigningKey: randomBytes(32).toString('base64'),
    MobileAuth__OtpKey: randomBytes(32).toString('base64'),
    LegacyMobileBridge__Enabled: 'false', LegacyMobileBridge__SigningSecret: '',
    LegacyMobileBridge__FingerprintKey: '', LegacyMobileBridge__StartsAt: '', LegacyMobileBridge__EndsAt: '',
    Twilio__AccountSid: '', Twilio__AuthToken: '', Twilio__FromNumber: '',
    WebAdmin__Enabled: 'false', WebAdmin__Password: '', WebAdmin__ThrottleKey: '',
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
  assert.deepEqual(document.paths['/api/admin/auth/me'].get.security, [{ WebAdmin: [] }]);
  assert.equal(document.components.securitySchemes.WebAdmin.in, 'cookie');
  assert.equal(document.components.securitySchemes.WebAdmin.name, '__Host-lefteria-admin');
  assert.equal(document.paths['/api/admin/auth/login'].post.security, undefined);
  assert.equal(document.paths['/api/public/site'].get.security, undefined);
  assert.deepEqual(Object.keys(document.components.schemas.WebAdminIdentityDto.properties).sort(),
    ['id', 'name', 'organizations', 'permissions', 'role', 'username']);
  assert.ok(document.paths['/api/health/ready'].get.responses['503']);
  assert.deepEqual(document.paths['/api/mobile/auth/me'].get.security, [{ Mobile: [] }]);
  assert.equal(document.paths['/api/mobile/auth/request-otp'].post.security, undefined);
  const legacyExchange = document.paths['/api/mobile/auth/exchange-legacy'].post;
  assert.deepEqual(legacyExchange.security, [{ LegacyMobile: [] }]);
  assert.equal(legacyExchange.requestBody, undefined);
  assert.ok(legacyExchange.responses['200'] && legacyExchange.responses['401'] && legacyExchange.responses['503']);
  const avatarUpload = document.paths['/api/mobile/profile/avatar'].post;
  assert.deepEqual(avatarUpload.security, [{ Mobile: [] }]);
  const multipart = avatarUpload.requestBody.content['multipart/form-data'].schema;
  const uploadSchema = multipart.$ref
    ? document.components.schemas[multipart.$ref.split('/').at(-1)] : multipart;
  assert.ok(uploadSchema.required?.includes('file'));
  assert.equal(uploadSchema.properties.file.format, 'binary');
  assert.equal(uploadSchema.properties.file.type, 'string');
  const avatarRead = document.paths['/api/uploads/avatars/{fileName}'];
  assert.ok(avatarRead.get && avatarRead.head);
  assert.equal(avatarRead.get.security, undefined);
  const playerPicker = document.paths['/api/mobile/players-list'].get;
  assert.deepEqual(playerPicker.security, [{ Mobile: [] }]);
  assert.ok(playerPicker.responses['503'], 'An uninitialized roster must remain unavailable.');
  assert.deepEqual(Object.keys(document.components.schemas.MobilePlayerOptionDto.properties).sort(),
    ['id', 'image_url', 'name', 'number', 'position']);
  assert.ok(document.components.schemas.MobileVerificationResult.anyOf?.length === 2 ||
    document.components.schemas.MobileVerificationResult.oneOf?.length === 2);
  for (const [path, method] of [
    ['/api/mobile/calendar', 'get'], ['/api/mobile/availability', 'post'],
    ['/api/mobile/my-availability', 'get'],
    ['/api/mobile/calendar/{event_type}/{event_id}/responses', 'get']
  ]) {
    const operation = document.paths[path][method];
    assert.deepEqual(operation.security, [{ Mobile: [] }]);
    assert.ok(operation.responses['503'], 'An uninitialized calendar must remain unavailable.');
  }
  const responseParameters = document.paths['/api/mobile/calendar/{event_type}/{event_id}/responses'].get.parameters;
  assert.ok(responseParameters.some(parameter => parameter.name === 'event_type' && parameter.in === 'path' && parameter.required));
  assert.equal(document.paths['/api/mobile/events/{event_id}/attendance'], undefined,
    'The legacy attendance/MVP contract must not be replaced by a different RSVP shape.');

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
