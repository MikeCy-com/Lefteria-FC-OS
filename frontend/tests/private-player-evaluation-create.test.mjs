import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110140000_PrivateEvaluationCreation.cs');
const api=read('backend/src/LefteriaFc.Api/Evaluations/AdminAcademyEvaluationEndpoints.cs');
const grants=read('ops/postgres/academy-evaluation-admin-grants.sql');
const panel=read('frontend/src/lib/admin/AdminPlayerPrivateRecordsPanel.svelte');
const roster=read('frontend/src/lib/admin/AdminRosterPlayersPanel.svelte');
const groups=read('frontend/src/routes/admin/groups/+page.svelte');

test('evaluation creation is idempotent and bound to a current assigned player',()=>{
  assert.match(sql,/PRIMARY KEY \(organization_id,request_id\)/);
  assert.match(sql,/payload_fingerprint char\(32\)/);
  assert.match(sql,/ON CONFLICT DO NOTHING/);
  assert.match(sql,/v_existing\.actor_id<>v_actor/);
  assert.match(sql,/v_existing\.payload_fingerprint<>v_fingerprint/);
  assert.match(sql,/FROM club\.teams t/);
  assert.match(sql,/FROM club\.player_academy_groups m/);
  assert.match(sql,/p\.is_active/);
  assert.match(sql,/FOR SHARE/);
  assert.match(sql,/INSERT INTO academy_private\.evaluations/);
  assert.match(sql,/INSERT INTO academy_private\.evaluation_revisions/);
  assert.match(sql,/INSERT INTO audit\.security_events/);
});
test('actor/organization and score ranges are validated on both boundaries',()=>{
  assert.match(sql,/identity\.lock_admin_club_authority\(_digest,_owner\)/);
  assert.match(sql,/v_role NOT IN \('manager','super_admin'\)/);
  assert.match(sql,/validate_academy_score_group\(_technical,'technical'\)/);
  assert.match(sql,/evaluation_overall_invalid/);
  assert.match(api,/RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(api,/session\.User\.Role is not \("manager" or "super_admin"\)/);
  assert.match(api,/organization_id=="lefteria_fc"\?"teams:write":"academy:write"/);
  assert.match(api,/secrets\.TokenDigest\(cookie!\)/);
  assert.match(api,/NpgsqlDbType\.Jsonb/);
  assert.match(api,/evaluation_create_unavailable/);
});
test('runtime role only gets the reviewed creator function, never raw data',()=>{
  assert.match(sql,/evaluation_create_requests ENABLE ROW LEVEL SECURITY/);
  assert.match(sql,/REVOKE ALL ON FUNCTION academy_private\.create_admin_player_evaluation/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION academy_private\.create_admin_player_evaluation/);
  assert.match(grants,/has_function_privilege\(m,create_fn,'EXECUTE'\)/);
  assert.match(grants,/evaluation_private_table_exposed/);
  assert.doesNotMatch(grants,/GRANT SELECT ON|GRANT INSERT ON|GRANT UPDATE ON/);
});
test('player-panel creation follows roster and exact manager write permissions',()=>{
  assert.match(panel,/admin-player-private-records/);
  assert.match(panel,/admin-evaluation-create/);
  assert.match(panel,/request_id:requestId/);
  assert.match(panel,/crypto\.randomUUID\(\)/);
  assert.match(panel,/X-Lefteria-Admin/);
  assert.match(panel,/allowCreate/);
  assert.match(roster,/AdminPlayerPrivateRecordsPanel/);
  assert.match(roster,/currentGroups\(player\)\.length>0/);
  assert.match(groups,/allowEvaluationWrite=/);
  assert.match(groups,/teams:write/);
  assert.match(groups,/academy:write/);
});