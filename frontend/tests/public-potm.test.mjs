import test from 'node:test';
import assert from 'node:assert/strict';
import { projectPotm, potmMonthLabel } from '../src/lib/public/potm.ts';

const club='lefteria_fc', academy='lefteria_fc_academy';
const player=(id,number,name,extra={})=>({id,number,name,organization_id:club,team_type:'First Team',
  position:'Forward',image_url:null,...extra});
const rows=[player('b',9,'Public B'),player('a',4,'Public A',{image_url:'/api/uploads/players/a.jpg'}),
  player('c',11,'Public C',{image_url:'/api/mobile/private/portrait'}),
  player('youth',10,'PRIVATE YOUTH SENTINEL',{organization_id:academy,team_type:'Academy'}),
  player('women',7,'OTHER TEAM SENTINEL',{team_type:'Women'})];
const data=()=>({organization_id:club,time_zone:'Asia/Nicosia',generated_at:'2026-11-28T11:00:00Z',
  snapshot_at:'2026-11-27T10:00:00Z',players:structuredClone(rows),
  modules:[{id:'potm',organization_id:club,status:'ready',published_count:1,quarantined_count:0}],
  potm:{id:'2026-11',organization_id:club,month_key:'2026-11',total_votes:7,
    results:[{player_id:'a',votes:2,name:'UNTRUSTED RESULT NAME',voter_phone:'PRIVATE VOTER'},
      {player_id:'b',votes:5}]}});
test('published POTM uses first-team roster identity, consistent counts and no voter information',()=>{
  const result=projectPotm(data());
  assert.equal(result.status,'published');
  assert.equal(result.monthKey,'2026-11');
  assert.equal(result.snapshotAt,'2026-11-27T10:00:00Z');
  assert.equal(result.totalVotes,7);
  assert.deepEqual(result.candidates.map(p=>[p.id,p.votes]),[['b',5],['a',2],['c',0]]);
  assert.equal(result.candidates[1].imageUrl,'/api/uploads/players/a.jpg');
  assert.equal(result.candidates[2].imageUrl,null);
  assert.doesNotMatch(JSON.stringify(result),/PRIVATE|SENTINEL|UNTRUSTED|voter_phone/);
});
test('absent monthly result is not an authoritative zero and unavailable data does not fabricate candidates',()=>{
  const sections=data();sections.potm.results=[];sections.potm.total_votes=0;
  const result=projectPotm(sections);
  assert.equal(result.status,'not-published');assert.equal(result.totalVotes,null);
  assert.ok(result.candidates.every(p=>p.votes===null));
  assert.deepEqual(projectPotm(null),{status:'unavailable',monthKey:null,snapshotAt:null,totalVotes:null,candidates:[]});
});
test('aggregate refuses inconsistent, partial, off-scope, duplicate and stale results',()=>{
  for(const mutate of [
    x=>x.potm.total_votes=10,
    x=>x.potm.month_key='2026-10',
    x=>x.potm.organization_id=academy,
    x=>x.modules[0].status='partial',
    x=>x.modules[0].quarantined_count=1,
    x=>x.modules=[],
    x=>x.potm.results[1].player_id='women',
    x=>x.potm.results[1].player_id='youth',
    x=>x.potm.results[1].player_id='a',
    x=>x.potm.results[1].votes=-1,
    x=>x.potm.results[1].votes=2.5,
    x=>x.snapshot_at='2026-10-31T12:00:00Z',
    x=>x.snapshot_at='invalid',
    x=>x.snapshot_at='2026-11-30T12:00:00Z',
    x=>x.players.push({...x.players[0]}),
    x=>x.organization_id=academy,
    x=>x.time_zone='UTC'
  ]){
    const sections=data();mutate(sections);const result=projectPotm(sections);
    assert.equal(result.status,'unavailable',String(mutate));
    assert.equal(result.totalVotes,null);assert.ok(result.candidates.every(p=>p.votes===null));
  }
});
test('monthly view follows Cyprus local calendar, not UTC or browser timezone',()=>{
  const sections=data();sections.generated_at='2026-10-31T22:30:00Z';
  sections.snapshot_at='2026-10-31T22:15:00Z';
  assert.equal(projectPotm(sections).status,'published');
  assert.equal(projectPotm(sections).monthKey,'2026-11');
  assert.ok(potmMonthLabel('2026-11').includes('2026'));
  assert.equal(potmMonthLabel('2026-99'),'');
});
