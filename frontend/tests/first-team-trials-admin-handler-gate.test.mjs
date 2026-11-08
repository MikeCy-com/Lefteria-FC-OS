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

test('admin trials GET is mapped with explicit protected OpenAPI scope and has no submit operations',()=>{
  assert.match(program,/app\.MapFirstTeamTrialsAdmin\(\\)/);
  const api=JSON.parse(openapi);
  const route=api.paths['/api/admin/trials/settings'];
  assert.deepEqual(Object.keys(route),['get']);
  assert.deepEqual(route.get.security,[{WebAdmin:[]}]);
  assert.deepEqual(route.get.parameters.filter(p=>p.name==='organization_id').map(p=>p.schema.enum),[['lefteria_fc']]);
  assert.ok(route.get.responses['503']);
  assert.doesNotMatch(program,/app\.MapAcademyEnquiry/);
});
