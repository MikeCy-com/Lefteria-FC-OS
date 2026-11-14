import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { existsSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { registerHooks } from 'node:module';
import { compile } from 'svelte/compiler';
import { render } from 'svelte/server';

test('public roster routes use scoped recorded sections, preserve opaque IDs and distinguish unavailable from missing', async () => {
  const player = { id:'Case--ID',organization_id:'lefteria_fc',name:'<script>Recorded Player</script>',number:9,image_url:null,team_type:'First Team',
    position:'Forward',age:null,nationality:'Cyprus',bio:'Recorded public biography',height:null,weight:null,preferred_foot:null,team_id:null,academy_group_name:null,academy_group_ids:[],
    statistics:{appearances:4,goals:0,assists:null,minutes_played:null,clean_sheets:null,yellow_cards:1,red_cards:null},previous_clubs:[],
    date_of_birth:'sentinel-private',medical_notes:'sentinel-private',parent_phone:'sentinel-private' };
  const staff = { id:'staff-ID',organization_id:'lefteria_fc',name:'Recorded Coach',role:'Head Coach',image_url:null,team_type:'First Team',nationality:null,bio:'Public coaching biography',joined_date:null,
    qualifications:['Recorded qualification'],badges:[],coaching_philosophy:null,favorite_quote:null,team_ids:[],academy_group_ids:[],previous_experience:[] };
  const sections = { organization_id:'lefteria_fc',players:[player,{...player,id:'academy-child',organization_id:'lefteria_fc_academy',team_type:'Academy',name:'sentinel-academy'}],
    staff:[staff,{...staff,id:'academy-coach',organization_id:'lefteria_fc_academy',team_type:'Academy'}],
    gallery:[{id:'photo',organization_id:'lefteria_fc',title:'Recorded team photo',image_url:'https://example.test/team.jpg',category:'First Team'}],
    venues:[{id:'venue',organization_id:'lefteria_fc',name:'Recorded Stadium',address:null,city:'Limassol',country:'Cyprus',capacity:1000,surface:'Grass',image_url:null,map_url:null,is_home_ground:true}],
    standings:[{id:'standing',organization_id:'lefteria_fc',competition:'Recorded league',team_name:'Fixture Club',played:4,won:2,drawn:1,lost:1,goals_for:6,goals_against:4,points:7}],
    modules:[],fixtures:[],academy_groups:[],seasons:[],sponsors:[],partners:[],events:[],products:[],tickets:[],archived_seasons:[],reviews:[],academy_stats:null,trial_settings:null,branding:null };
  let unavailable = false; let wrongScope = false;
  const requests = [];
  const server = createServer((request,response) => {
    const url = new URL(request.url,'http://fixture.test'); requests.push({url,cookie:request.headers.cookie,authorization:request.headers.authorization});
    response.setHeader('content-type','application/json');
    if(url.pathname==='/api/public/sections') {
      if(unavailable) {response.statusCode=503;response.end('{"code":"public_sections_unavailable"}');}
      else if(url.searchParams.get('organization_id')==='lefteria_fc_academy') response.end(JSON.stringify({...sections,organization_id:'lefteria_fc_academy',
        players:[{...player,id:'academy-child',organization_id:'lefteria_fc_academy',team_type:'Academy',name:'Ανδρέας Πα.',bio:null,nationality:null}]}));
      else response.end(JSON.stringify(wrongScope ? {...sections,organization_id:'lefteria_fc_academy'} : sections));
    } else if(url.pathname==='/api/public/site') response.end(JSON.stringify({organization_id:url.searchParams.get('organization_id'),club:null,teams:[],academy_groups:[],fixtures:[],news:[]}));
    else {response.statusCode=404;response.end('{}');}
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const origin='http://127.0.0.1:'+server.address().port;
  const hooks = registerHooks({
    resolve(specifier,context,next) {
      if(specifier==='$app/state')return {url:'data:text/javascript,'+encodeURIComponent('export const page={url:new URL("https://fixture.test/team")};'),shortCircuit:true};
      if(specifier==='$app/env/private')return {url:'data:text/javascript,'+encodeURIComponent('export const INTERNAL_API_ORIGIN='+JSON.stringify(origin)),shortCircuit:true};
      if(specifier.startsWith('.') && /\/src\//.test(context.parentURL??'') && !/\.[a-z]+$/i.test(specifier)) {
        const candidate=new URL(specifier+'.ts',context.parentURL);if(existsSync(candidate))return next(candidate.href,context);
      }
      return next(specifier,context);
    },
    load(url,context,next) {
      if(url.endsWith('.css'))return {format:'module',source:'export {};',shortCircuit:true};
      if(url.endsWith('.svelte')) {const filename=fileURLToPath(url);return {format:'module',source:compile(readFileSync(filename,'utf8'),{filename,generate:'server'}).js.code,shortCircuit:true};}
      return next(url,context);
    }
  });
  try {
    const {loadTeamPage,loadPlayerPage,loadStaffPage}=await import('../src/lib/server/public-sections.ts');
    const url=new URL('https://preview.test/player/Case--ID?organization_id=lefteria_fc_academy');
    const loaded=await loadPlayerPage(url,'Case--ID');assert.equal(loaded.player.id,'Case--ID');assert.equal(loaded.organization,'lefteria_fc');
    assert.equal(loaded.sections,undefined,'A player profile must not serialize unrelated public section collections.');
    assert.equal((await loadPlayerPage(url,'recorded-player--Case--ID')).player.id,'Case--ID');
    for(const id of ['case--id','unknown'])await assert.rejects(loadPlayerPage(url,id),error=>error.status===404);
    const academy=await loadPlayerPage(url,'academy-child');
    assert.equal(academy.player.name,'Ανδρέας Πα.','Academy players keep the original profile page with the API-abbreviated name.');
    assert.equal(academy.player.organization_id,'lefteria_fc_academy');
    assert.equal((await loadStaffPage(url,'staff-ID')).staff.name,'Recorded Coach');
    await assert.rejects(loadStaffPage(url,'academy-coach'),error=>error.status===404);
    const {default:Profile}=await import('../src/lib/public/PublicPersonProfile.svelte');
    const profile=render(Profile,{props:{player:loaded.player}}).body;
    assert.match(profile,/&lt;script>Recorded Player&lt;\/script>/);assert.doesNotMatch(profile,/<script>|sentinel-private|sentinel-academy/);
    assert.match(profile,/Recorded public biography/);assert.match(profile,/>0<\/dd>/);assert.match(profile,/>—<\/dd>/);assert.match(profile,/href="\/team\?tab=roster"/);
    const {default:Team}=await import('../src/routes/team/+page.svelte');
    for(const [tab,text] of [['roster','Recorded Player'],['staff','Recorded Coach'],['gallery','Recorded team photo'],['venues','Recorded Stadium'],['statistics','ΣΤΑΤΙΣΤΙΚΑ ΠΑΙΚΤΩΝ']]) {
      const data=await loadTeamPage(new URL('https://preview.test/team?tab='+tab));assert.equal(data.tab,tab);
      const html=render(Team,{props:{data}}).body;assert.match(html,new RegExp(text));assert.doesNotMatch(html,/sentinel-academy|sentinel-private|<script>Recorded Player/);
      if(tab==='roster')assert.match(html,/href="\/player\/Case--ID"/);
      if(tab==='statistics')assert.match(html,/>0<\/td>/);
    }
    assert.equal((await loadTeamPage(new URL('https://preview.test/team?tab=unknown'))).tab,'overview');
    unavailable=true;
    await assert.rejects(loadPlayerPage(url,'unknown'),error=>error.status===503);
    await assert.rejects(loadTeamPage(new URL('https://preview.test/team?tab=roster')),error=>error.status===503);
    assert.equal((await loadTeamPage(new URL('https://preview.test/team?tab=schedule'))).available,true);
    unavailable=false;wrongScope=true;
    await assert.rejects(loadPlayerPage(url,'Case--ID'),error=>error.status===503);
    assert.ok(requests.filter(row=>row.url.pathname==='/api/public/sections').every(row=>['lefteria_fc','lefteria_fc_academy'].includes(row.url.searchParams.get('organization_id'))));
    assert.ok(requests.every(row=>!row.cookie&&!row.authorization));
  } finally {hooks.deregister();await new Promise(resolve=>server.close(resolve));}
});
