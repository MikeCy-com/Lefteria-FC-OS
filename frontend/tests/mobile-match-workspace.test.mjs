import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {validMatchWorkspace,recommendGrassrootsStarters,repeatedGrassrootsReserve,statisticFromInputs} from '../src/lib/mobile/match-workspace.ts';
const root=new URL('../../',import.meta.url);
const read=path=>readFileSync(new URL(path,root),'utf8');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261128010000_MatchSquadRotationWorkspace.cs');
const panel=read('frontend/src/lib/mobile/components/MobileMatchWorkspace.svelte');
const calendar=read('frontend/src/lib/mobile/components/MobileCalendar.svelte');
const controller=read('frontend/src/lib/mobile/controller.ts');
const grants=read('ops/postgres/identity-app-grants.sql');
const contract=JSON.parse(read('contracts/openapi.json'));
const player=(id,last_assignment,starts,reserves)=>({player_id:id,name:'Player '+id,number:0,position:null,last_assignment,starts,reserves});
const roster=[player('a','starter',4,0),player('b','starter',3,1),player('c','reserve',1,3),player('d','reserve',0,4),player('e',null,0,0)];
const squad=roster.slice(0,4).map(p=>({player_id:p.player_id,assignment:p.last_assignment,position:null}));
const base={match_id:'match-a',organization_id:'lefteria_fc_academy',host_id:'group-a',mode:'grassroots',version:1,
  state:'selected',completed:false,roster_stale:false,roster,squad,statistics:[],rotation_note:'',review_reason:''};
test('grassroots recommendations prioritize last reserves and never add unselected children',()=>{
  const result=recommendGrassrootsStarters(roster,squad,2);
  assert.deepEqual(result.filter(p=>p.assignment==='starter').map(p=>p.player_id),['c','d']);
  assert.deepEqual(result.filter(p=>p.assignment==='reserve').map(p=>p.player_id),['a','b']);
  assert.ok(result.every(p=>p.position===null));
  assert.equal(result.some(p=>p.player_id==='e'),false);
  assert.equal(repeatedGrassrootsReserve(roster,squad),true);
  assert.equal(repeatedGrassrootsReserve(roster,result),false);
  const next=roster.map(p=>({...p,last_assignment:result.find(s=>s.player_id===p.player_id)?.assignment??null}));
  assert.deepEqual(recommendGrassrootsStarters(next,result,2).filter(p=>p.assignment==='starter').map(p=>p.player_id),['a','b']);
});
test('rotation refuses unknown, duplicate or invalid participants and group sizes',()=>{
  assert.throws(()=>recommendGrassrootsStarters(roster,squad,5));
  assert.throws(()=>recommendGrassrootsStarters(roster,[squad[0],squad[0]],1));
  assert.throws(()=>recommendGrassrootsStarters(roster,[{...squad[0],player_id:'other'}],1));
  assert.throws(()=>recommendGrassrootsStarters(roster,squad,0));
});
test('workspace requires correct organization, fixture, bounded unique rows and no Academy positions',()=>{
  assert.equal(validMatchWorkspace(base,'match-a','lefteria_fc_academy'),true);
  assert.equal(validMatchWorkspace(base,'another','lefteria_fc_academy'),false);
  assert.equal(validMatchWorkspace(base,'match-a','lefteria_fc'),false);
  for(const raw of [{...base,roster:[...roster,roster[0]]},{...base,squad:[...squad,squad[0]]},
    {...base,squad:[{...squad[0],position:'GK'}]},{...base,roster:[{...roster[0],position:'GK'}]},
    {...base,squad:[{...squad[0],player_id:'unknown'}]},{...base,state:'submitted'},
    {...base,roster_stale:true},{...base,phone:'private'}])
    assert.equal(validMatchWorkspace(raw,'match-a','lefteria_fc_academy'),false);
  const stale={...base,roster_stale:true,squad:[]};
  assert.equal(validMatchWorkspace(stale,'match-a','lefteria_fc_academy'),true);
});
test('statistics require explicit integers and cannot invent goals or assists with zero minutes',()=>{
  assert.deepEqual(statisticFromInputs('a','20','1','0'),{player_id:'a',minutes_played:20,goals:1,assists:0});
  assert.deepEqual(statisticFromInputs('a','0','0','0'),{player_id:'a',minutes_played:0,goals:0,assists:0});
  for(const [m,g,a] of [['','0','0'],['20','','0'],['20','0',''],['0','1','0'],['0','0','1'],['181','0','0'],['20','51','0'],['3.5','0','0']])
    assert.equal(statisticFromInputs('a',m,g,a),null);
  const stats=squad.map(p=>({player_id:p.player_id,minutes_played:20,goals:0,assists:0}));
  assert.equal(validMatchWorkspace({...base,completed:true,state:'submitted',statistics:stats},'match-a','lefteria_fc_academy'),true);
  assert.equal(validMatchWorkspace({...base,completed:true,state:'submitted',statistics:stats.slice(1)},'match-a','lefteria_fc_academy'),false);
});
test('persistent writes enforce scope, version and review state, and UI stays staff-only',()=>{
  compile(panel,{filename:'MobileMatchWorkspace.svelte',generate:'client'});
  compile(calendar,{filename:'MobileCalendar.svelte',generate:'client'});
  assert.match(sql,/CREATE TABLE club\.match_workspaces/);
  assert.match(sql,/CREATE TABLE club\.match_workspace_revisions/);
  assert.match(sql,/club\.read_mobile_chat_groups\(_actor,_phone,_role,_expires\)/);
  assert.match(sql,/club\.require_training_drill_group\(v_admin,host\)/);
  assert.match(sql,/club\.admin_football_allowed\(v_admin,v_admin_role,owner,v_scope,NULL,true\)/);
  assert.match(sql,/match_independent_review_required/);
  assert.match(sql,/FOR UPDATE/);
  assert.match(sql,/coalesce\(v_plan.version,0\)<>_expected/);
  assert.match(sql,/v_owner='lefteria_fc_academy' AND s.value->'position'<>'null'::jsonb/);
  assert.match(sql,/match_rotation_note_required/);
  assert.match(sql,/v_state NOT IN \('selected','rejected'\) OR NOT v_completed/);
  assert.match(sql,/v_state<>'submitted' OR NOT v_completed/);
  assert.match(sql,/INSERT INTO club\.match_workspace_revisions/);
  assert.match(grants,/REVOKE ALL ON TABLE club\.match_workspaces,club\.match_workspace_revisions/);
  assert.match(controller,/result\.version!==command\.expected_version\+1/);
  assert.match(calendar,/item\.kind==='match' && !item\.is_cancelled && \(role==='coach'\|\|role==='management'\)/);
  assert.match(panel,/recommendGrassrootsStarters\(workspace.roster,squad,startingCount\)/);
  assert.match(panel,/Δεν έχουν δημοσιευθεί αυτόματα/);
  assert.doesNotMatch(panel,/localStorage|sessionStorage/);
  assert.deepEqual(contract.paths['/api/mobile/calendar/match/{event_id}/workspace'].post.security,[{Mobile:[]}]);
});
