import test from 'node:test';
import assert from 'node:assert/strict';
import {validPotmBallot,validPotmMutationResult,potmCandidateOptions} from '../src/lib/mobile/potm-ballot.ts';
import {createMobileAuthController} from '../src/lib/mobile/controller.ts';
import {createMobileSessionStorage} from '../src/lib/mobile/session.ts';

const month='2026-11';
const base={month_key:month,voting_open:true,my_vote_player_id:null,revision:0};
const player=(id,name,extra={})=>({id,name,number:9,organization_id:'lefteria_fc',team_type:'First Team',
  position:'Forward',image_url:null,...extra});
const sections=()=>({organization_id:'lefteria_fc',time_zone:'Asia/Nicosia',
  generated_at:'2026-11-28T11:00:00Z',snapshot_at:'2026-11-27T10:00:00Z',
  players:[player('a','Published A'),player('b','Published B'),
    player('youth','PRIVATE YOUTH',{organization_id:'lefteria_fc_academy',team_type:'Academy'}),
    player('women','OTHER TEAM',{team_type:'Women'})],
  modules:[{id:'potm',organization_id:'lefteria_fc',status:'empty',quarantined_count:0}],
  potm:{organization_id:'lefteria_fc',month_key:month,total_votes:0,results:[]}});
const picker={players:[{id:'a',name:'PRIVATE CANONICAL NAME'},{id:'youth'},{id:'women'},{id:'unpublished'}]};
test('ballot accepts only verified own-state shape, exact revision and explicit null',()=>{
  assert.equal(validPotmBallot(base),true);
  assert.equal(validPotmBallot({...base,my_vote_player_id:'a',revision:1}),true);
  for(const bad of [{...base,identity_id:'private'},{...base,revision:'0'},
    {...base,revision:-1},{...base,my_vote_player_id:'a'},{...base,month_key:'2026-99'},
    {...base,voting_open:1},{...base,my_vote_player_id:' '},{...base,revision:2147483648}])
    assert.equal(validPotmBallot(bad),false);
  assert.equal(validPotmMutationResult(base,'a',{...base,my_vote_player_id:'a',revision:1}),true);
  assert.equal(validPotmMutationResult(base,'a',{...base,my_vote_player_id:'a',revision:2}),false);
  assert.equal(validPotmMutationResult(base,'a',{...base,my_vote_player_id:'b',revision:1}),false);
  assert.equal(validPotmMutationResult({...base,my_vote_player_id:'a',revision:1},null,{...base,revision:2}),true);
});
test('candidate list uses public names only and current canonical eligible IDs',()=>{
  const result=potmCandidateOptions(sections(),picker,month);
  assert.deepEqual(result.map(p=>[p.id,p.name]),[['a','Published A']]);
  assert.doesNotMatch(JSON.stringify(result),/PRIVATE|OTHER TEAM|unpublished|votes/);
  const old=sections();old.snapshot_at='2026-10-25T10:00:00Z';
  assert.throws(()=>potmCandidateOptions(old,picker,month));
  const partial=sections();partial.modules[0].status='partial';
  assert.throws(()=>potmCandidateOptions(partial,picker,month));
  const legacy=sections();legacy.modules[0].status='ready';legacy.potm.total_votes=1;legacy.potm.results=[{player_id:'a',votes:1}];
  assert.throws(()=>potmCandidateOptions(legacy,picker,month));
  assert.throws(()=>potmCandidateOptions(sections(),{players:[{id:'a'},{id:'a'}]},month));
  assert.deepEqual(potmCandidateOptions(sections(),{players:[]},month),[]);
});
const identity=role=>({id:'11111111-1111-4111-8111-111111111111',phone:'+35799123456',role,
  name:'CI Fan',platform:'Web/PWA',created_at:'2026-01-01T00:00:00Z',
  last_login:'2026-11-28T00:00:00Z',available_roles:[role],current_role_available:true});
function setup(role,handler){
  const values=new Map([['mobile_token','isolated-token']]);
  const storage=createMobileSessionStorage({getItem:key=>values.get(key)??null,
    setItem:(key,value)=>values.set(key,value),removeItem:key=>values.delete(key)});
  const calls=[];
  const controller=createMobileAuthController({baseUrl:'https://club.example',storage,
    fetch:async(input,init)=>{
      const request=new Request(input,init);const url=new URL(request.url);
      const entry={path:url.pathname,query:url.search,method:request.method,
        auth:request.headers.get('Authorization'),credentials:request.credentials,cache:request.cache,
        body:request.method==='PUT'?await request.json():undefined};
      calls.push(entry);
      if(entry.path==='/api/mobile/auth/me')return Response.json(identity(role));
      return handler(entry);
    }});
  return {controller,calls};
}
test('real controller uses own bearer for ballot and canonical picker but never public fetch',async()=>{
  const {controller,calls}=setup('fan',entry=>{
    if(entry.path==='/api/mobile/potm/ballot')
      return Response.json(entry.method==='PUT'?{...base,my_vote_player_id:'a',revision:1}:base);
    if(entry.path==='/api/mobile/players-list')return Response.json(picker);
    if(entry.path==='/api/public/sections')return Response.json(sections());
    throw Error('unexpected path '+entry.path);
  });
  await controller.bootstrap();
  const previous=await controller.getPotmBallot();
  const options=await controller.getPotmCandidates(previous.month_key);
  assert.deepEqual(options.map(x=>x.id),['a']);
  assert.equal((await controller.savePotmBallot(previous,'a')).revision,1);
  const publicCall=calls.find(x=>x.path==='/api/public/sections');
  assert.equal(publicCall.auth,null);assert.equal(publicCall.credentials,'omit');
  assert.equal(publicCall.query,'?organization_id=lefteria_fc');
  assert.ok(calls.filter(x=>x.path.startsWith('/api/mobile/')).every(x=>x.auth==='Bearer isolated-token'&&x.cache==='no-store'));
  assert.deepEqual(calls.at(-1).body,{month_key:month,expected_revision:0,player_id:'a'});
  controller.logout();
  await assert.rejects(()=>controller.getPotmBallot());
});
test('non-Fan and closed ballot cannot submit even with cached hints',async()=>{
  const {controller,calls}=setup('parent',()=>{throw Error('unexpected request');});
  await controller.bootstrap();
  await assert.rejects(()=>controller.getPotmBallot());
  await assert.rejects(()=>controller.getPotmCandidates(month));
  await assert.rejects(()=>controller.savePotmBallot(base,'a'));
  assert.equal(calls.length,1);
  const fan=setup('fan',()=>{throw Error('unexpected request');});
  await fan.controller.bootstrap();
  await assert.rejects(()=>fan.controller.savePotmBallot({...base,voting_open:false},'a'));
  assert.equal(fan.calls.length,1);
});
test('stale private response after logout cannot populate the ballot',async()=>{
  let release;
  const {controller}=setup('fan',()=>new Promise(resolve=>{release=resolve;}));
  await controller.bootstrap();
  const request=controller.getPotmBallot();
  controller.logout();
  release(Response.json(base));
  await assert.rejects(request);
});
