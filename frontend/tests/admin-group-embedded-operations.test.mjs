import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
const read=p=>readFileSync(new URL('../src/'+p,import.meta.url),'utf8');
const group=read('routes/admin/groups/+page.svelte');
const workspace=read('lib/admin/AdminGroupWorkspace.svelte');
const attendance=read('lib/admin/AdminGroupAttendancePanel.svelte');
const fixtures=read('lib/admin/AdminGroupPublishedFixtures.svelte');
const plans=read('lib/admin/AdminAcademyTrainingPlans.svelte');
const player=read('lib/admin/AdminRosterPlayersPanel.svelte');
test('all five operations live inside selected group instead of five global menus',()=>{
  for(const [name,source] of Object.entries({group,workspace,attendance,fixtures}))
    assert.ok(compile(source,{filename:name+'.svelte',generate:'server'}).js.code.length>500,name);
  assert.match(group,/data-testid="admin-group-workspace"/);
  assert.match(group,/group=\{focusedGroup\}/);
  assert.match(group,/groups=\{directory\.groups\}/);
  assert.match(group,/organization && \$auth\.session/);
  assert.doesNotMatch(group,/href=\{`\/admin\/(?:schedule|attendance|matches)/);
  for(const id of ['admin-group-players-link','admin-group-training-link',
    'admin-group-attendance-link','admin-group-matches-link','admin-group-settings-link'])
    assert.ok(workspace.includes(id),'Missing internal group tab '+id);
  for(const tag of ['AdminAcademyTrainingPlans','AdminGroupAttendancePanel',
    'AdminGroupPublishedFixtures','AdminGroupMatchDraftPanel','AdminRosterPlayersPanel',
    'AdminFirstTeamProfilePanel','AdminAcademyGroupProfilePanel','AdminCoachDirectoryPanel'])
    assert.match(workspace,new RegExp('<'+tag+'\\b'));
  assert.ok(workspace.includes("{#key organization+':'+group.id"));
  assert.match(workspace,/groupId=\{group\.id\}/);
  assert.match(workspace,/groups=\{\[group\]\}/);
  assert.match(workspace,/from=\{range\.start\} to=\{range\.end\}/);
  assert.match(workspace,/allowPrivate=\{canPrivate\}/);
  assert.match(workspace,/allowEvaluationWrite=\{canEvaluate\}/);
});

test('attendance write belongs to selected group, event and actual player and verifies API confirmation',()=>{
  for(const s of [attendance,workspace]){
    assert.doesNotMatch(s,/localStorage|sessionStorage|innerHTML|\@html/);
  }
  assert.match(attendance,/group_id:group/);
  assert.match(attendance,/x\.organization_id==='lefteria_fc_academy'&&x\.group_id===g/);
  assert.match(attendance,/x\.event_id===id/);
  assert.match(attendance,/x\.players\.every/);
  assert.match(attendance,/validRegister\(raw,selected\)/);
  assert.match(attendance,/!canWrite/);
  assert.match(attendance,/register\?\.writable/);
  assert.match(attendance,/expected_version:version/);
  assert.match(attendance,/res\.status===409/);
  assert.match(attendance,/x\.event_id!==event\|\|x\.player_id!==row\.player_id/);
  assert.match(attendance,/RSVP/);
  assert.match(attendance,/δεν υποκαθιστά την πραγματική παρουσία/);
  assert.match(workspace,/canReadAttendance/);
  assert.match(workspace,/canAttendanceWrite/);
  assert.match(plans,/group_id:groupId/);
});

test('group matches use published exact group scope with truthful empty states',()=>{
  assert.match(fixtures,/f\.team_id:f\.academy_group_id\)===groupId/);
  assert.match(fixtures,/f\.organization_id===owner/);
  assert.match(fixtures,/\/api\/public\/site\?organization_id=/);
  assert.match(fixtures,/Δεν υπάρχουν δημοσιευμένοι αγώνες/);
  assert.match(fixtures,/Η καταχώριση ή διόρθωση σκορ απαιτεί/);
  assert.doesNotMatch(fixtures,/method:'POST'|method:"POST"|Math\.random/);
  assert.match(player,/AdminPlayerPrivateRecordsPanel/);
});
