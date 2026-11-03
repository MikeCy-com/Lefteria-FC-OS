import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { fileURLToPath } from 'node:url';
import { createServer } from 'node:http';
import { compile } from 'svelte/compiler';
import { render } from 'svelte/server';

function hooks(origin) {
  return registerHooks({
    resolve(specifier, context, nextResolve) {
      if (specifier === '$app/state') return { url: 'data:text/javascript,' + encodeURIComponent('export const page={url:new URL("https://fixture.test/match/Recorded-ID")};'), shortCircuit: true };
      if (specifier === '$app/env/private') return { url: 'data:text/javascript,' + encodeURIComponent('export const INTERNAL_API_ORIGIN=' + JSON.stringify(origin)), shortCircuit: true };
      if (specifier.startsWith('.') && /\/src\/(?:lib|routes)\//.test(context.parentURL ?? '') && !/\.[a-z]+$/i.test(specifier)) {
        const candidate = new URL(specifier + '.ts', context.parentURL);
        if (existsSync(candidate)) return nextResolve(candidate.href, context);
      }
      return nextResolve(specifier, context);
    },
    load(url, context, nextLoad) {
      if (url.endsWith('.css')) return { format: 'module', source: 'export {};', shortCircuit: true };
      if (url.endsWith('.svelte')) {
        const filename = fileURLToPath(url);
        return { format: 'module', source: compile(readFileSync(filename, 'utf8'), { filename, generate: 'server' }).js.code, shortCircuit: true };
      }
      return nextLoad(url, context);
    }
  });
}

test('public match resolves both explicit organizations, preserves legacy IDs, distinguishes unavailable from unknown, and renders only recorded match information', async () => {
  const fixture = { id:'Recorded-ID',organization_id:'lefteria_fc',home_team:'ΛΕΥΤΕΡΙΑ FC',away_team:'Recorded opponent',home_team_logo:null,away_team_logo:null,
    home_score:0,away_score:0,match_date:'2026-11-07',match_time:'15:00',venue:'Recorded ground',competition:'Recorded competition',status:'Completed',team_id:null,academy_group_id:null };
  const academyFixture = { ...fixture,id:'Academy-ID',organization_id:'lefteria_fc_academy',academy_group_id:'group',status:'Scheduled',home_score:null,away_score:null,match_time:null };
  const exact = { ...fixture, id:'exact--ID' };
  const fc = { organization_id:'lefteria_fc',club:null,teams:[],academy_groups:[],fixtures:[fixture,exact],news:[],fixture_clock:[{id:fixture.id,kickoff_at:'2026-11-07T13:00:00Z'}],time_zone:'Asia/Nicosia',generated_at:'2026-11-02T12:00:00Z' };
  const academy = { ...fc,organization_id:'lefteria_fc_academy',fixtures:[academyFixture],academy_groups:[{id:'group',name:'U12'}],fixture_clock:[{id:academyFixture.id,kickoff_at:null}] };
  let unavailable = false;
  const requests = [];
  const server = createServer((request,response) => {
    const url = new URL(request.url,'http://fixture.test');
    requests.push({ path:url.pathname,org:url.searchParams.get('organization_id'),cookie:request.headers.cookie,authorization:request.headers.authorization });
    response.setHeader('content-type','application/json');
    if (unavailable && url.searchParams.get('organization_id') === 'lefteria_fc_academy') { response.statusCode=503; response.end('{}'); return; }
    response.end(JSON.stringify(url.searchParams.get('organization_id') === 'lefteria_fc_academy' ? academy : fc));
  });
  await new Promise(resolve => server.listen(0,'127.0.0.1',resolve));
  const installed = hooks('http://127.0.0.1:' + server.address().port);
  try {
    const {loadPublicMatch} = await import('../src/lib/server/public-match.ts');
    const url = new URL('https://preview.test/match/Recorded-ID?organization_id=other&upstream=https://evil.test');
    const data = await loadPublicMatch(url,'Recorded-ID');
    assert.equal(data.fixture.id,'Recorded-ID');
    assert.equal(data.organization,'lefteria_fc');
    assert.equal(data.kickoffAt,'2026-11-07T13:00:00Z');
    assert.ok(requests.every(row => row.path === '/api/public/site' && ['lefteria_fc','lefteria_fc_academy'].includes(row.org) && row.cookie === undefined && row.authorization === undefined));
    assert.equal((await loadPublicMatch(url,'exact--ID')).fixture.id,'exact--ID');
    assert.equal((await loadPublicMatch(url,'recorded-opponent--Recorded-ID')).fixture.id,'Recorded-ID');
    await assert.rejects(loadPublicMatch(url,'recorded-id'), error => error.status === 404);
    await assert.rejects(loadPublicMatch(url,'not-in-snapshot'), error => error.status === 404);
    await assert.rejects(loadPublicMatch(url,'x'.repeat(401)), error => error.status === 404);
    const academyData = await loadPublicMatch(url,'Academy-ID');
    assert.equal(academyData.organization,'lefteria_fc_academy');
    const {default:Page} = await import('../src/routes/match/[id]/+page.svelte');
    const html = render(Page,{props:{data}}).body;
    assert.match(html,/Recorded opponent/);
    assert.match(html,/Recorded competition/);
    assert.match(html,/Recorded ground/);
    assert.match(html,/0 : 0/);
    assert.match(html,/15:00/);
    assert.match(html,/ΩΡΑ ΚΥΠΡΟΥ/);
    assert.match(html,/team\?tab=results/);
    assert.doesNotMatch(html,/parent_phone|medical|lineup|κατοχή|σουτ/i);
    const pending = render(Page,{props:{data:academyData}}).body;
    assert.match(pending,/U12/);
    assert.match(pending,/Η ώρα θα ανακοινωθεί/);
    assert.match(pending,/Δεν έχει καταχωριστεί σκορ/);
    assert.doesNotMatch(pending,/0 : 0/);
    assert.match(pending,/\/academy#academy-fixtures/);
    const {default:Fixtures} = await import('../src/lib/public/Fixtures.svelte');
    const rows = render(Fixtures,{props:{fixtures:[{...fixture,id:'opaque/id'}]}}).body;
    assert.match(rows,/href="\/match\/opaque%2Fid"/);
    assert.match(rows,/Λεπτομέρειες αγώνα/);
    unavailable = true;
    await assert.rejects(loadPublicMatch(url,'not-in-snapshot'), error => error.status === 503);
    assert.equal((await loadPublicMatch(url,'Recorded-ID')).fixture.id,'Recorded-ID');
  } finally {
    installed.deregister();
    await new Promise(resolve => server.close(resolve));
  }
});
