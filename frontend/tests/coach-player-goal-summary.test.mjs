import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {validCoachGoalPage} from '../src/lib/mobile/coach-player-goals.ts';
const load=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const migration=load('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261121160000_MobileCoachPlayerGoalSummary.cs');
const backend=load('backend/src/LefteriaFc.Infrastructure/Messaging/PostgresCoachFeedbackDraftStore.cs');
const api=load('backend/src/LefteriaFc.Api/Messaging/MobileCoachFeedbackEndpoints.cs');
const grants=load('ops/postgres/identity-app-grants.sql');
const checks=load('ops/postgres/identity-app-permissions-check.sql');
const ctrl=load('frontend/src/lib/mobile/controller.ts');
const view=load('frontend/src/lib/mobile/components/MobileCoachFeedbackTargets.svelte');
const goal={
 id:'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
 title:'Πάσα με το αριστερό',category:'technical',
 progress:50,status:'active',target_date:null
};
const owner='lefteria_fc_academy',player='player-001';
const page={organization_id:owner,player_id:player,total:1,goals:[goal]};
test('coach-only goal parser rejects foreign records and invalid private summaries',()=>{
 assert.equal(validCoachGoalPage(page,owner,player),true);
 assert.equal(validCoachGoalPage(page,'lefteria_fc',player),false);
 assert.equal(validCoachGoalPage({...page,player_id:'other'},owner,player),false);
 assert.equal(validCoachGoalPage({...page,total:2},owner,player),false);
 assert.equal(validCoachGoalPage({...page,goals:[goal,{...goal}]},owner,player),false);
 assert.equal(validCoachGoalPage({...page,goals:[{...goal,progress:55}]},owner,player),false);
 assert.equal(validCoachGoalPage({...page,goals:[{...goal,status:'archived'}]},owner,player),false);
});
test('coach API is narrow and protected by current identity, exact group and immutable origin',()=>{
 for(const word of [
   "_role IS DISTINCT FROM 'coach'",'mobile_record_links',
   'canonical_contact_bindings','resolve_private_player_territory',
   'current_feedback_coach_assignment','staff_academy_groups',
   'academy_group_coaches','academy_group_location_assignments',
   'loc.location_id=v_location','g.origin_location_id=v_location',
   'g.origin_scope_id=v_scope','g.origin_city_id=v_city',
   "g.status IN ('active','completed')"
 ])assert.ok(migration.includes(word),word);
 for(const word of ["'description',","'note',","'history',",
   "'source_evaluation_id',","'medical',","'document',","'birth_date',"])
   assert.ok(!migration.includes(word),word);
 assert.match(migration,/IF v_count>50/);
 assert.match(migration,/audit\.security_events/);
 assert.match(migration,/EXCEPTION WHEN no_data_found OR too_many_rows/);
 assert.match(migration,/REVOKE ALL ON FUNCTION club\.read_mobile_coach_player_goals/);
 assert.match(grants,/GRANT EXECUTE ON FUNCTION club\.read_mobile_coach_player_goals/);
 assert.match(checks,/Mobile coach goals read-only boundary unsafe/);
 assert.match(checks,/has_table_privilege\(current_user,'club\.player_development_goals'/);
 assert.match(backend,/actor\.Role!=MobileRole\.Coach/);
 assert.match(backend,/result\.Goals/);
 assert.match(api,/RequireAuthorization\(IdentityServices\.Policy\)/);
 assert.match(api,/MobilePrincipal\.Read\(context\.User\)/);
 assert.match(api,/private, no-store/);
});
test('coach goal UI only opens for an explicitly selected eligible player',()=>{
 assert.ok(compile(view,{filename:'MobileCoachFeedbackTargets.svelte',generate:'server'}).js.code.length>500);
 assert.match(ctrl,/state\.user\?\.role!=='coach'/);
 assert.match(ctrl,/validCoachGoalPage\(result,owner,playerId\)/);
 assert.match(view,/getCoachPlayerGoals\(/);
 assert.match(view,/target\.organization_id as CoachGoalOwner,target\.id/);
 assert.match(view,/goalRevision\+\+/);
 assert.match(view,/goalKey===key\(target\)/);
 assert.match(view,/data-testid="coach-player-goal-summary"/);
 assert.doesNotMatch(view,/\@html|localStorage|sessionStorage/);
});
