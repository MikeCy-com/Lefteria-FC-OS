import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { fileURLToPath } from 'node:url';
import { createServer } from 'node:http';
import { compile } from 'svelte/compiler';
import { render } from 'svelte/server';

test('Academy directory and detail use real scoped DTOs, preserve opaque IDs and distinguish unavailable from missing', async () => {
  const group = { id:'Group--Legacy', organization_id:'lefteria_fc_academy', name:'U12 Λεμεσού', age_range:'10–12 ετών',
    banner_url:null, city:'Λεμεσός', location_name:'Καταχωρισμένο γήπεδο', description:'Το τμήμα μας <script>unsafe</script>', season:'2026/27',
    training_schedule:'Δευτέρα 17:00–18:00\nΠέμπτη 17:00–18:00', display_order:1, medical_notes:'sentinel-private', coach_phone:'sentinel-private' };
  const match = { id:'match', organization_id:'lefteria_fc_academy', academy_group_id:group.id, team_id:null,
    home_team:'U12 Λευτεριά', away_team:'Πραγματικός αντίπαλος', home_score:null, away_score:null,
    home_team_logo:null, away_team_logo:null, match_date:'2026-11-12', match_time:'10:00',
    competition:'Πρωτάθλημα U12', status:'Scheduled', venue:'Δημοτικό γήπεδο' };
  const fc = { organization_id:'lefteria_fc', club:null, academy_groups:[], teams:[], fixtures:[], news:[] };
  const academy = { ...fc, organization_id:'lefteria_fc_academy', academy_groups:[group], fixtures:[
    match, { ...match,id:'earlier',match_date:'2026-11-05' },
    { ...match,id:'completed',status:'Completed',home_score:2,away_score:1,match_date:'2026-10-20' },
    { ...match,id:'other-group',academy_group_id:'other',away_team:'Other group sentinel' },
    { ...match,id:'no-group',academy_group_id:null,away_team:'Unassigned fixture sentinel' },
    { ...match,id:'wrong-org',organization_id:'lefteria_fc',away_team:'First-team fixture sentinel' }
  ] };
  const sections = {
    organization_id:'lefteria_fc_academy',academy_stats:{
      id:'academy',organization_id:'lefteria_fc_academy',age_groups:1,athletes:2,trainings_per_week:3,dedication_pct:95
    },
    players:[
      {id:'academy-public-player',organization_id:'lefteria_fc_academy',team_type:'Academy',name:'Ανδρέας Πα.',
        academy_group_ids:[group.id],number:7,position:'Defender',image_url:null,
        statistics:{appearances:3,goals:0,assists:1,minutes_played:120},medical_notes:'sentinel-private'},
      {id:'wrong-group-player',organization_id:'lefteria_fc_academy',team_type:'Academy',name:'Different group sentinel',
        academy_group_ids:['other'],number:8,position:'Forward',image_url:null},
      {id:'wrong-org-player',organization_id:'lefteria_fc',team_type:'First Team',name:'Club player sentinel',
        academy_group_ids:[group.id],number:10,position:'Forward',image_url:null}
    ],
    staff:[{id:'academy-coach',organization_id:'lefteria_fc_academy',name:'Verified Academy Coach',academy_group_ids:[group.id]}],
    gallery:[
      {id:'group-gallery',organization_id:'lefteria_fc_academy',academy_group_id:group.id,
        title:'Επιβεβαιωμένη φωτογραφία',image_url:'https://example.test/academy.jpg'},
      {id:'foreign-gallery',organization_id:'lefteria_fc_academy',academy_group_id:'other',
        title:'Unrelated photo sentinel',image_url:'https://example.test/other.jpg'}
    ],fixtures:[],sponsors:[],modules:[],events:[],partners:[],seasons:[],reviews:[
      {id:'approved-review-1',organization_id:'lefteria_fc_academy',author_name:'Published Guardian',
        author_role:'Γονέας',rating:5,title:'Πολύ καλή συνεργασία',
        body:'Αληθινό δημοσιευμένο σχόλιο',response:'Ευχαριστούμε',published_at:'2026-11-01T12:00:00Z'},
      {id:'approved-review-2',organization_id:'lefteria_fc_academy',author_name:'Published Athlete',
        author_role:'Αθλητής',rating:3,title:null,body:'Δημοσιευμένη εμπειρία',
        response:null,published_at:'2026-10-11T12:00:00Z'},
      {id:'wrong-org-review',organization_id:'lefteria_fc',author_name:'Other organization review sentinel',
        author_role:'Fan',rating:5,title:'Club review',body:'Sentinel from First Team',
        response:null,published_at:'2026-11-12T12:00:00Z'}
    ],venues:[]
  };
  let unavailable = false;
  const requests=[];
  const server=createServer((request,response)=>{
    const url=new URL(request.url,'http://fixture.test');
    requests.push({url,cookie:request.headers.cookie,authorization:request.headers.authorization});
    response.setHeader('content-type','application/json');
    if(url.pathname==='/api/public/sections'){
      response.end(JSON.stringify(sections));return;
    }
    if(url.pathname!=='/api/public/site'){response.statusCode=404;response.end('{}');return;}
    if(url.searchParams.get('organization_id')==='lefteria_fc_academy') {
      response.statusCode=unavailable?503:200; response.end(JSON.stringify(unavailable?{}:academy));
    } else response.end(JSON.stringify(fc));
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const origin='http://127.0.0.1:'+server.address().port;
  const hooks=registerHooks({
    resolve(specifier,context,nextResolve){
      if(specifier==='$app/state') return {url:'data:text/javascript,'+encodeURIComponent('export const page={url:new URL("https://fixture.test/academy")};'),shortCircuit:true};
      if(specifier==='$app/env/private') return {url:'data:text/javascript,'+encodeURIComponent('export const INTERNAL_API_ORIGIN='+JSON.stringify(origin)),shortCircuit:true};
      if(specifier.startsWith('.')&&/\/src\//.test(context.parentURL??'')&&!/\.[a-z]+$/i.test(specifier)) {
        const candidate=new URL(specifier+'.ts',context.parentURL); if(existsSync(candidate))return nextResolve(candidate.href,context);
      }
      return nextResolve(specifier,context);
    },
    load(url,context,nextLoad){
      if(url.endsWith('.css'))return {format:'module',source:'export {};',shortCircuit:true};
      if(url.endsWith('.svelte')){const filename=fileURLToPath(url);return {format:'module',source:compile(readFileSync(filename,'utf8'),{filename,generate:'server'}).js.code,shortCircuit:true};}
      return nextLoad(url,context);
    }
  });
  try {
    const {load:listLoad}=await import('../src/routes/academy/groups/+page.server.ts');
    const {load:detailLoad}=await import('../src/routes/academy/[slug]/+page.server.ts');
    const {academyGroupLink}=await import('../src/lib/public/navigation.ts');
    const list=await listLoad({url:new URL('https://fixture.test/academy/groups?organization_id=lefteria_fc')});
    assert.equal(list.organization,'lefteria_fc_academy');
    const {default:ListPage}=await import('../src/routes/academy/groups/+page.svelte');
    const listHtml=render(ListPage,{props:{data:list}}).body;
    assert.match(listHtml,/ΗΛΙΚΙΑΚΑ ΤΜΗΜΑΤΑ|ΤΑ ΤΜΗΜΑΤΑ ΜΑΣ/);
    assert.ok(listHtml.includes(academyGroupLink(group)));
    assert.match(listHtml,/Καταχωρισμένο γήπεδο/);
    assert.equal(list.sectionsAvailable,true);
    assert.match(listHtml,/academy-groups-philosophy/);
    assert.match(listHtml,/academy-groups-registration/);
    assert.match(listHtml,/academy-registration-cta-btn/);
    assert.match(listHtml,/Δευτέρα 17:00–18:00/);
    assert.match(listHtml,/Verified Academy Coach/);
    assert.match(listHtml,/data-testid="academy-published-player"/);
    assert.match(listHtml,/Ανδρέας Πα\./);
    assert.match(listHtml,/\/player\/andreas-pa--academy-public-player/);
    assert.doesNotMatch(listHtml,/Different group sentinel|Club player sentinel|sentinel-private/);
    const directoryNoProjection=render(ListPage,{props:{data:{...list,
      sections:null,sectionsAvailable:false}}}).body;
    assert.match(directoryNoProjection,/Τα δημοσιευμένα στοιχεία αθλητών δεν είναι προσωρινά διαθέσιμα/);
    assert.doesNotMatch(directoryNoProjection,/Verified Academy Coach|Ανδρέας Πα\.|academy-published-player/);
    assert.ok(directoryNoProjection.includes(academyGroupLink(group)),
      'Approved group directory must survive public roster service downtime');

    assert.doesNotMatch(listHtml,/sentinel-private|<script>unsafe/);
    const path=academyGroupLink(group);
    const detail=await detailLoad({url:new URL('https://fixture.test'+path),params:{slug:decodeURIComponent(path.slice('/academy/'.length))}});
    assert.equal(detail.group.id,group.id);
    assert.deepEqual(detail.groupFixtures.map(item=>item.id),['completed','earlier','match']);
    assert.equal(detail.groupSectionsAvailable,true);
    assert.deepEqual(detail.groupPlayers.map(item=>item.id),['academy-public-player']);
    assert.deepEqual(detail.groupGallery.map(item=>item.id),['group-gallery']);
    assert.deepEqual(detail.groupCoaches.map(item=>item.id),['academy-coach']);
    await assert.rejects(
      detailLoad({url:new URL('https://fixture.test/academy/'+group.id),params:{slug:group.id}}),
      error => error.status === 301 && error.location === path,
      'Original opaque-ID links must redirect permanently to the verified readable group URL.');
    const {default:DetailPage}=await import('../src/routes/academy/[slug]/+page.svelte');
    const detailHtml=render(DetailPage,{props:{data:detail}}).body;
    for(const actual of ['Δευτέρα 17:00–18:00','Πέμπτη 17:00–18:00','Καταχωρισμένο γήπεδο','Πραγματικός αντίπαλος','2 : 1','10–12 ετών']) assert.ok(detailHtml.includes(actual),actual);
    assert.doesNotMatch(detailHtml,/sentinel-private|Other group sentinel|Unassigned fixture sentinel|First-team fixture sentinel|<script>unsafe/);
    assert.match(detailHtml,/&lt;script>unsafe/);
    for(const actual of ['Ανδρέας Πα.','Verified Academy Coach','Επιβεβαιωμένη φωτογραφία','ΡΟΣΤΕΡ','ΣΤΑΤΙΣΤΙΚΑ','ΓΚΑΛΕΡΙ'])
      assert.ok(detailHtml.includes(actual),actual);
    assert.doesNotMatch(detailHtml,/Different group sentinel|Club player sentinel|Unrelated photo sentinel|sentinel-private/);
    for(const slug of ['missing','group--legacy','u12--group--legacy'])
      await assert.rejects(detailLoad({url:new URL('https://fixture.test/academy/'+slug),params:{slug}}),error=>error.status===404);
    const {default:Landing}=await import('../src/routes/academy/+page.svelte');
    const landingHtml=render(Landing,{props:{data:list}}).body;
    assert.ok(landingHtml.includes(academyGroupLink(group)));
    assert.match(landingHtml,/href="\/academy\/groups"/);

    // Legacy free-text city and location names are not verified geographic
    // assignments; retain the published group while labelling it unassigned.
    assert.match(landingHtml,/data-testid="academy-landing-unassigned-groups"/);
    assert.match(landingHtml,/data-organization="lefteria_fc_academy"/);
    assert.match(landingHtml,/ΚΑΘΕ ΠΟΛΗ/);
    assert.match(landingHtml,/footer-nav-groups/);
    assert.match(landingHtml,/footer-nav-sponsors/);
    assert.doesNotMatch(landingHtml,/footer-nav-team/);
    assert.doesNotMatch(landingHtml,/data-testid="academy-landing-verified-city"/);
    assert.doesNotMatch(landingHtml,/Άλλη περιοχή/);
    assert.doesNotMatch(landingHtml,/Other group sentinel|Unassigned fixture sentinel|First-team fixture sentinel/);

    const cityGroup={...group,id:'verified-lemesos',name:'U10 Λεμεσός',
      city_id:'verified-city-lemesos',city:'Λεμεσός',city_slug:'lemesos',
      location_id:'verified-location-limassol',location_name:'Πολεμίδια',location_slug:'polemidia'};
    const secondCityGroup={...group,id:'verified-pafos',name:'U14 Πάφος',
      city_id:'verified-city-pafos',city:'Πάφος',city_slug:'pafos',
      location_id:'verified-location-pafos',location_name:'Κέντρο Πάφου',location_slug:'kentron'};
    const legacyUnassigned={...group,id:'legacy-pafos',name:'Legacy Pafos U12',
      city:'Πάφος',location_name:'Κέντρο Πάφου'};
    const mixedHtml=render(Landing,{props:{data:{...list,
      site:{...list.site,academy_groups:[cityGroup,secondCityGroup,legacyUnassigned]}}}}).body;
    assert.equal((mixedHtml.match(/data-testid="academy-landing-verified-city"/g)??[]).length,2);
    assert.equal((mixedHtml.match(/data-testid="academy-landing-verified-location"/g)??[]).length,2);
    assert.match(mixedHtml,/href="#academy-landing-city-lemesos"/);
    assert.match(mixedHtml,/href="#academy-landing-city-pafos"/);
    assert.match(mixedHtml,/href="\/academy\/groups#academy-city-lemesos"/);
    assert.match(mixedHtml,/id="academy-landing-location-lemesos-polemidia"/);
    assert.match(mixedHtml,/Legacy Pafos U12/);
    assert.match(mixedHtml,/data-testid="academy-landing-unassigned-groups"/);
    assert.match(mixedHtml,/2 πόλεις · 2 τοποθεσίες/);
    assert.doesNotMatch(mixedHtml,/Άλλη περιοχή|Other group sentinel|Unassigned fixture sentinel|First-team fixture sentinel/);

    const {load:landingLoad}=await import('../src/routes/academy/+page.server.ts');
    const withStats=await landingLoad({url:new URL('https://fixture.test/academy')});
    const withStatsHtml=render(Landing,{props:{data:withStats}}).body;
    assert.match(withStatsHtml,/Η ΑΚΑΔΗΜΙΑ ΣΕ ΑΡΙΘΜΟΥΣ/);
    assert.match(withStatsHtml,/>95%<\/strong>/);
    assert.match(withStatsHtml,/academy-development-philosophy/);
    assert.match(withStatsHtml,/academy-registration-cta-btn/);
    assert.match(withStatsHtml,/data-testid="academy-public-reviews"/);
    assert.match(withStatsHtml,/Published Guardian/);
    assert.match(withStatsHtml,/Published Athlete/);
    assert.match(withStatsHtml,/4\/5/);
    assert.match(withStatsHtml,/Ευχαριστούμε/);
    assert.doesNotMatch(withStatsHtml,/Other organization review sentinel|Sentinel from First Team/);
    const withoutReviews = render(Landing,{props:{data:{
      ...withStats, sections:{...withStats.sections,reviews:[]}
    }}}).body;
    assert.doesNotMatch(withoutReviews,/academy-public-reviews|academy-published-rating-summary/);

    assert.ok(requests.every(row=>row.cookie===undefined&&row.authorization===undefined));
    assert.ok(requests.every(row=>['lefteria_fc_academy','lefteria_fc'].includes(row.url.searchParams.get('organization_id'))));
    unavailable=true;
    await assert.rejects(listLoad({url:new URL('https://fixture.test/academy/groups')}),error=>error.status===503);
    await assert.rejects(detailLoad({url:new URL('https://fixture.test/academy/missing'),params:{slug:'missing'}}),error=>error.status===503,'Missing source must not be misreported as an unknown group.');
  } finally {hooks.deregister();await new Promise(resolve=>server.close(resolve));}
});
