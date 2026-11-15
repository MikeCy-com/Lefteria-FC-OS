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
  const academyPlayers = [{...player,id:'academy-child',organization_id:'lefteria_fc_academy',team_type:'Academy',name:'Ανδρέας Πα.',bio:null,nationality:null}];
  let unavailable = false; let firstTeamUnavailable = false; let wrongScope = false;
  let publishedTeams = [];
  const requests = [];
  const server = createServer((request,response) => {
    const url = new URL(request.url,'http://fixture.test'); requests.push({url,cookie:request.headers.cookie,authorization:request.headers.authorization});
    response.setHeader('content-type','application/json');
    if(url.pathname==='/api/public/sections') {
      if(unavailable || (firstTeamUnavailable && url.searchParams.get('organization_id')==='lefteria_fc')) {response.statusCode=503;response.end('{"code":"public_sections_unavailable"}');}
      else if(url.searchParams.get('organization_id')==='lefteria_fc_academy') response.end(JSON.stringify({...sections,organization_id:'lefteria_fc_academy',
        players:academyPlayers}));
      else response.end(JSON.stringify(wrongScope ? {...sections,organization_id:'lefteria_fc_academy'} : sections));
    } else if(url.pathname==='/api/public/site') response.end(JSON.stringify({organization_id:url.searchParams.get('organization_id'),club:null,teams:publishedTeams,academy_groups:[],fixtures:[],news:[]}));
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
    // The API order must not let an ID matching the suffix hijack a raw ID.
    const originalPlayers = sections.players;
    const suffixPlayer = {...player,id:'ID',name:'Different suffix player'};
    for (const ordered of [[suffixPlayer,...originalPlayers],[...originalPlayers,suffixPlayer]]) {
      sections.players = ordered;
      assert.equal((await loadPlayerPage(url,'Case--ID')).player.id,'Case--ID',
        'An exact opaque ID wins over a different record matching its suffix.');
      assert.equal((await loadPlayerPage(url,'recorded-player--Case--ID')).player.id,'Case--ID',
        'Generated title slugs still resolve to an opaque ID containing the separator.');
      assert.equal((await loadPlayerPage(url,'ID')).player.name,'Different suffix player');
    }
    academyPlayers.push({...academyPlayers[0],id:'academy--ID',name:'Exact Academy Player'});
    assert.equal((await loadPlayerPage(url,'academy--ID')).player.name,'Exact Academy Player',
      'An Academy exact ID wins over a different First Team record matching only its suffix.');
    assert.equal((await loadPlayerPage(url,'academy-player--academy--ID')).player.id,'academy--ID');
    academyPlayers.pop();
    sections.players = originalPlayers;
    for(const id of ['case--id','unknown'])await assert.rejects(loadPlayerPage(url,id),error=>error.status===404);
    const academy=await loadPlayerPage(url,'academy-child');
    assert.equal(academy.player.name,'Ανδρέας Πα.','Academy players keep the original profile page with the API-abbreviated name.');
    assert.equal(academy.player.organization_id,'lefteria_fc_academy');
    assert.equal((await loadStaffPage(url,'staff-ID')).staff.name,'Recorded Coach');
    await assert.rejects(loadStaffPage(url,'academy-coach'),error=>error.status===404);
    const playerPage = readFileSync(new URL('../src/routes/player/[id]/+page.svelte', import.meta.url), 'utf8');
    assert.ok(playerPage.includes('organization={data.organization}'),
      'Player page must use the resolved player organization, not force the first team.');
    const {default:Profile}=await import('../src/lib/public/PublicPersonProfile.svelte');
    const profile=render(Profile,{props:{player:loaded.player}}).body;
    assert.match(profile,/&lt;script>Recorded Player&lt;\/script>/);assert.doesNotMatch(profile,/<script>|sentinel-private|sentinel-academy/);
    assert.match(profile,/Recorded public biography/);assert.match(profile,/>0<\/dd>/);assert.match(profile,/>—<\/dd>/);assert.match(profile,/href="\/team\?tab=roster"/);
    const academyProfile=render(Profile,{props:{player:academy.player}}).body;
    assert.ok(academyProfile.includes('href="/academy/groups"'));
    assert.match(academyProfile,/ΤΑ ΤΜΗΜΑΤΑ ΤΗΣ ΑΚΑΔΗΜΙΑΣ/);
    assert.ok(!academyProfile.includes('href="/team?tab=roster"'));
    assert.ok(!academyProfile.includes('href="/team?tab=statistics"'));
    assert.doesNotMatch(academyProfile,/Στατιστικά πρώτης ομάδας/);
    assert.equal(academy.organization,'lefteria_fc_academy');

    const {default:Team}=await import('../src/routes/team/+page.svelte');
    for(const [tab,text] of [['roster','Recorded Player'],['staff','Recorded Coach'],['gallery','Recorded team photo'],['venues','Recorded Stadium'],['statistics','ΣΤΑΤΙΣΤΙΚΑ ΠΑΙΚΤΩΝ']]) {
      const data=await loadTeamPage(new URL('https://preview.test/team?tab='+tab));assert.equal(data.tab,tab);
      const html=render(Team,{props:{data}}).body;assert.match(html,new RegExp(text));assert.doesNotMatch(html,/sentinel-academy|sentinel-private|<script>Recorded Player/);
      if(tab==='roster')assert.match(html,/href="\/player\/Case--ID"/);
      if(tab==='statistics')assert.match(html,/>0<\/td>/);
    }
    assert.equal((await loadTeamPage(new URL('https://preview.test/team?tab=unknown'))).tab,'overview');
    // Two published Club teams: all roster links and profiles must preserve
    // their independently authorized team identity without cross-team data.
    publishedTeams = [
      {id:'first-team-id',name:'First Team',organization_id:'lefteria_fc',level:'First Team',description:null,banner_url:null},
      {id:'women-id',name:'Women Team',organization_id:'lefteria_fc',level:'Women',description:null,banner_url:null}
    ];
    sections.players = [
      {...player,team_id:'first-team-id'},
      {...player,id:'women-player',name:'Recorded Women Player',team_id:'women-id',team_type:'Women'}
    ];
    const womenPage=await loadTeamPage(new URL('https://preview.test/team?tab=roster&team_id=women-id'));
    assert.equal(womenPage.selectedTeamId,'women-id');
    const womenHtml=render(Team,{props:{data:womenPage}}).body;
    assert.match(womenHtml,/Recorded Women Player/);
    assert.doesNotMatch(womenHtml,/Recorded Player&lt;\/script>|href="\/player\/Case--ID"/);
    assert.match(womenHtml,/\/player\/women-player\?team_id=women-id/);
    await assert.rejects(loadTeamPage(new URL('https://preview.test/team?team_id=not-published')),error=>error.status===404);
    const womenProfile=await loadPlayerPage(new URL('https://preview.test/player/women-player?team_id=women-id'),'women-player');
    assert.equal(womenProfile.returnTeamId,'women-id');
    const womenProfileHtml=render(Profile,{props:{player:womenProfile.player,returnTeamId:womenProfile.returnTeamId}}).body;
    assert.match(womenProfileHtml,/\/team\?tab=roster&amp;team_id=women-id/);
    assert.match(womenProfileHtml,/\/team\?tab=statistics&amp;team_id=women-id/);
    const forgedProfile=await loadPlayerPage(new URL('https://preview.test/player/women-player?team_id=first-team-id'),'women-player');
    assert.equal(forgedProfile.returnTeamId,null);
    const academyForged=await loadPlayerPage(new URL('https://preview.test/player/academy-child?team_id=women-id'),'academy-child');
    assert.equal(academyForged.returnTeamId,null);
    // Staff backlinks are also scoped to actual assignments and published
    // teams. Never let a forged team_id redirect a Women's coach to the First Team.
    const originalStaff=sections.staff;
    sections.staff=[
      {...staff,team_ids:['first-team-id']},
      {...staff,id:'women-coach',name:'Recorded Women Coach',team_type:'Women',team_ids:['women-id']}
    ];
    const womenStaffPage=await loadTeamPage(new URL('https://preview.test/team?tab=staff&team_id=women-id'));
    const womenStaffHtml=render(Team,{props:{data:womenStaffPage}}).body;
    assert.match(womenStaffHtml,/\/staff\/women-coach\?team_id=women-id/);
    assert.doesNotMatch(womenStaffHtml,/href="\/staff\/staff-ID"/);
    const womenCoach=await loadStaffPage(new URL('https://preview.test/staff/women-coach?team_id=women-id'),'women-coach');
    assert.equal(womenCoach.returnTeamId,'women-id');
    const womenCoachHtml=render(Profile,{props:{staff:womenCoach.staff,returnTeamId:womenCoach.returnTeamId}}).body;
    assert.match(womenCoachHtml,/\/team\?tab=staff&amp;team_id=women-id/);
    const forgedCoach=await loadStaffPage(new URL('https://preview.test/staff/women-coach?team_id=first-team-id'),'women-coach');
    assert.equal(forgedCoach.returnTeamId,null);
    sections.staff=originalStaff;
    sections.players = originalPlayers;
    publishedTeams = [];
    firstTeamUnavailable=true;
    const academyWithoutFirstTeam=await loadPlayerPage(url,'academy-child');
    assert.equal(academyWithoutFirstTeam.organization,'lefteria_fc_academy');
    await assert.rejects(loadPlayerPage(url,'unknown'),error=>error.status===503,
      'Unknown ID must not become a false 404 while First Team projection is unavailable.');
    firstTeamUnavailable=false;
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
