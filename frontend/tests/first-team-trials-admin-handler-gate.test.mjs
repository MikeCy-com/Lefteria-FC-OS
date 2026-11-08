import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const source=(path)=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const handler=source('backend/src/LefteriaFc.Api/Trials/FirstTeamTrialsAdminEndpoints.cs');
const program=source('backend/src/LefteriaFc.Api/Program.cs');
const openapi=source('contracts/openapi.json');

test('future admin trials GET must recheck current session and FC role in backend',()=>{
  assert.ok(handler.includes('RequireAuthorization(WebAdminServices.Policy)'));
  assert.ok(handler.includes('admin.CurrentAsync('));
  assert.ok(handler.includes('FirstTeamTrialsAdminAccess.CanRead(session.User,organization_id)'));
  assert.ok(handler.includes('organization_id!="lefteria_fc"'));
  assert.ok(handler.includes('result is not { Open:false }'));
  assert.ok(handler.includes('statusCode:401'));
  assert.ok(handler.includes('statusCode:403'));
  assert.ok(handler.includes('statusCode:503'));
  assert.ok(handler.includes('CacheControl="private, no-store"'));
  assert.doesNotMatch(handler,/MapPost\(|MapPut\(|MapDelete\(|INSERT INTO|UPDATE club\.|applicant|medical|date_of_birth/i);
});

test('admin trial preview remains unreachable until full OpenAPI and auth integration',()=>{
  assert.doesNotMatch(program,/app\.MapFirstTeamTrialsAdmin\(\)/);
  assert.doesNotMatch(openapi,/"\/api\/admin\/trials\/settings"/);
});
