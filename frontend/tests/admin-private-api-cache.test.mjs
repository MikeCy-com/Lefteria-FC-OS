import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const program = readFileSync(new URL('../../backend/src/LefteriaFc.Api/Program.cs', import.meta.url), 'utf8');

test('mobile and authenticated admin API prefixes receive private no-store before handlers run', () => {
  assert.match(program, /StartsWithSegments\("\/api\/mobile", StringComparison\.OrdinalIgnoreCase\)/);
  assert.match(program, /StartsWithSegments\("\/api\/admin", StringComparison\.OrdinalIgnoreCase\)/);
  assert.match(program, /context\.Response\.OnStarting\(\(\) =>/);
  assert.match(program, /context\.Response\.Headers\.CacheControl = "private, no-store"/);
  assert.match(program, /context\.Response\.Headers\.Pragma = "no-cache"/);
  assert.match(program, /context\.Response\.Headers\["Expires"\] = "0"/);
});

test('sensitive API cache policy is installed before web-admin guards and authorization', () => {
  const middleware = program.indexOf('app.Use(async (context, next) =>');
  const guards = program.indexOf('app.UseWebAdminGuards();');
  const auth = program.indexOf('app.UseAuthorization();');
  assert.ok(middleware >= 0 && middleware < guards && guards < auth);
  assert.ok(program.includes('app.MapWebAdmin();'));
});
