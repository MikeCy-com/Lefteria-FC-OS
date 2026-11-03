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
  let unavailable = false;
  const requests=[];
  const server=createServer((request,response)=>{
    const url=new URL(request.url,'http://fixture.test');
    requests.push({url,cookie:request.headers.cookie,authorization:request.headers.authorization});
    response.setHeader('content-type','application/json');
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
    assert.doesNotMatch(listHtml,/sentinel-private|<script>unsafe/);
    const path=academyGroupLink(group);
    const detail=await detailLoad({url:new URL('https://fixture.test'+path),params:{slug:decodeURIComponent(path.slice('/academy/'.length))}});
    assert.equal(detail.group.id,group.id);
    assert.deepEqual(detail.groupFixtures.map(item=>item.id),['completed','earlier','match']);
    const exact=await detailLoad({url:new URL('https://fixture.test/academy/'+group.id),params:{slug:group.id}});
    assert.equal(exact.group.id,group.id,'Original opaque-ID links must remain supported, including IDs containing --.');
    const {default:DetailPage}=await import('../src/routes/academy/[slug]/+page.svelte');
    const detailHtml=render(DetailPage,{props:{data:detail}}).body;
    for(const actual of ['Δευτέρα 17:00–18:00','Πέμπτη 17:00–18:00','Καταχωρισμένο γήπεδο','Πραγματικός αντίπαλος','2 : 1','10–12 ετών']) assert.ok(detailHtml.includes(actual),actual);
    assert.doesNotMatch(detailHtml,/sentinel-private|Other group sentinel|Unassigned fixture sentinel|First-team fixture sentinel|<script>unsafe/);
    assert.match(detailHtml,/&lt;script>unsafe/);
    for(const slug of ['missing','group--legacy','u12--group--legacy'])
      await assert.rejects(detailLoad({url:new URL('https://fixture.test/academy/'+slug),params:{slug}}),error=>error.status===404);
    const {default:Landing}=await import('../src/routes/academy/+page.svelte');
    const landingHtml=render(Landing,{props:{data:list}}).body;
    assert.ok(landingHtml.includes(academyGroupLink(group)));
    assert.match(landingHtml,/href="\/academy\/groups"/);
    assert.ok(requests.every(row=>row.cookie===undefined&&row.authorization===undefined));
    assert.ok(requests.every(row=>['lefteria_fc_academy','lefteria_fc'].includes(row.url.searchParams.get('organization_id'))));
    unavailable=true;
    await assert.rejects(listLoad({url:new URL('https://fixture.test/academy/groups')}),error=>error.status===503);
    await assert.rejects(detailLoad({url:new URL('https://fixture.test/academy/missing'),params:{slug:'missing'}}),error=>error.status===503,'Missing source must not be misreported as an unknown group.');
  } finally {hooks.deregister();await new Promise(resolve=>server.close(resolve));}
});
