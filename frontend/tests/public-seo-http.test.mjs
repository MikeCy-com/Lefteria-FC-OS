import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync } from 'node:fs';
import { registerHooks } from 'node:module';

const publicApiStub = [
  'export function publicApi() {',
  '  globalThis.__seoApiCalls = (globalThis.__seoApiCalls ?? 0) + 1;',
  '  return { GET: async (route, options) => {',
  '    const organization = options?.params?.query?.organization_id;',
  '    if (route === "/api/public/news") return {response:{status:200},',
  '      data:{total:1,items:[{id:"public-news",title:"Νέα της ομάδας",created_at:"2026-11-01T00:00:00Z"}]}};',
  '    if (route === "/api/public/site") return {response:{status:200},',
  '      data:{organization_id:organization,academy_groups:organization==="lefteria_fc_academy"?',
  '        [{id:"academy-group",name:"U12",organization_id:"lefteria_fc_academy"}]:[],fixtures:[]}};',
  '    if (route === "/api/public/sections") return {response:{status:200},',
  '      data:{organization_id:organization,players:[],staff:[],events:[],partners:[],sponsors:[],archived_seasons:[]}};',
  '    throw Error("Unexpected anonymous API route: "+route);',
  '  }};',
  '}'
].join('\n');

const cmsOrigin = 'http://cms.fixture.invalid:8080';
const publishedPage = (owner) => ({
  organization_id: owner, slug: owner === 'lefteria_fc' ? 'club-history' : 'academy-values',
  published_at: '2026-11-15T08:00:00Z',
  page: {title:'Approved',summary:'',seo_title:'',seo_description:'',menu_parent:owner === 'lefteria_fc' ? 'club' : 'academy',
    menu_order:0,blocks:[{type:'paragraph',text:'Safe public content',url:null}]}
});

function mockCms(t, reply = owner => Response.json([publishedPage(owner)])) {
  return t.mock.method(globalThis, 'fetch', async (input, init) => {
    globalThis.__seoPageCalls = (globalThis.__seoPageCalls ?? 0) + 1;
    const url = new URL(input);
    assert.equal(url.origin, cmsOrigin);
    assert.equal(url.pathname, '/api/public/website/pages');
    const owner = url.searchParams.get('organization_id');
    assert.ok(['lefteria_fc','lefteria_fc_academy'].includes(owner));
    assert.deepEqual(init.headers, {Accept:'application/json'});
    assert.equal(init.cache, 'no-store');
    assert.equal(init.redirect, 'error');
    assert.ok(init.signal instanceof AbortSignal);
    return reply(owner);
  });
}

registerHooks({
  resolve(specifier, context, nextResolve) {
    if (specifier === '$app/env/private' &&
        context.parentURL?.endsWith('/lib/server/website-pages.ts')) {
      return { url: 'data:text/javascript,' + encodeURIComponent('export const INTERNAL_API_ORIGIN = ' + JSON.stringify(cmsOrigin)), shortCircuit: true };
    }
    if (specifier === '../../lib/server/public-content' &&
        context.parentURL?.includes('/routes/sitemap.xml/')) {
      return { url: 'data:text/javascript,' + encodeURIComponent(publicApiStub), shortCircuit: true };
    }
    if (specifier.startsWith('.') && /\/src\//.test(context.parentURL ?? '') &&
        !/\.[a-z]+$/i.test(specifier)) {
      const candidate = new URL(specifier + '.ts', context.parentURL);
      if (existsSync(candidate)) return nextResolve(candidate.href, context);
    }
    return nextResolve(specifier, context);
  }
});

test('public robots endpoint prevents preview indexing without redirecting crawlers', async () => {
  const { GET } = await import('../src/routes/robots.txt/+server.ts');
  const preview = GET({url:new URL('https://preview.lefteriafc.cy/robots.txt')});
  assert.equal(preview.status, 200);
  assert.equal(preview.headers.get('x-robots-tag'), 'noindex, nofollow');
  assert.equal(await preview.text(), 'User-agent: *\nDisallow: /\n');
  const publicResponse = GET({url:new URL('https://lefteriafc.cy/robots.txt')});
  assert.equal(publicResponse.status, 200);
  assert.equal(publicResponse.headers.get('content-type'), 'text/plain; charset=utf-8');
  assert.match(await publicResponse.text(), /Sitemap: https:\/\/lefteriafc\.cy\/sitemap\.xml/);
});

test('published sitemap endpoint renders actual anonymous projections; preview never touches API', async (t) => {
  mockCms(t);
  const { GET } = await import('../src/routes/sitemap.xml/+server.ts');
  globalThis.__seoApiCalls = 0;
  globalThis.__seoPageCalls = 0;
  const preview = await GET({url:new URL('https://localhost.invalid/sitemap.xml')});
  assert.equal(preview.status, 404);
  assert.equal(preview.headers.get('x-robots-tag'), 'noindex, nofollow');
  assert.equal(globalThis.__seoApiCalls, 0, 'preview must not request public projections');
  assert.equal(globalThis.__seoPageCalls, 0, 'preview must not request CMS projection');

  const published = await GET({url:new URL('https://lefteriafc.cy/sitemap.xml')});
  assert.equal(published.status, 200);
  assert.equal(published.headers.get('content-type'), 'application/xml; charset=utf-8');
  assert.equal(globalThis.__seoApiCalls, 1);
  assert.equal(globalThis.__seoPageCalls, 2);
  const xml = await published.text();
  assert.match(xml, /<loc>https:\/\/lefteriafc\.cy\/<\/loc>/);
  assert.match(xml, /\/news\/[^<]+--public-news/);
  assert.match(xml, /\/academy\/[^<]+--academy-group/);
  assert.match(xml, /\/pages\/club-history<\/loc>/);
  assert.match(xml, /\/pages\/academy-values\?organization_id=lefteria_fc_academy<\/loc>/);
  assert.doesNotMatch(xml, /\/admin\/|\/app\/|private-user|medical_notes/);
});

test('sitemap returns non-cacheable 503 for invalid CMS data or unavailable owner, while valid empty data succeeds', async (t) => {
  const { GET } = await import('../src/routes/sitemap.xml/+server.ts');
  const url = new URL('https://lefteriafc.cy/sitemap.xml');
  const cases = [
    () => new Response('upstream unavailable', {status:503}),
    () => { throw new Error('private upstream detail'); },
    () => new Response('invalid json'),
    () => Response.json({pages:[]}),
    owner => Response.json([publishedPage(owner), {organization_id:'wrong-owner'}]),
    owner => Response.json([publishedPage(owner), publishedPage(owner)]),
    owner => Response.json(Array.from({length:101}, (_, i) => ({...publishedPage(owner),slug:'page-'+i})))
  ];
  for (const fail of cases) {
    const mock = mockCms(t, owner => owner === 'lefteria_fc_academy' ? fail(owner) : Response.json([publishedPage(owner)]));
    try {
      const result = await GET({url});
      assert.equal(result.status, 503);
      assert.equal(result.headers.get('cache-control'), 'no-store');
      assert.equal(result.headers.get('x-robots-tag'), 'noindex');
      assert.equal(await result.text(), 'Public sitemap temporarily unavailable');
    } finally { mock.mock.restore(); }
  }
  mockCms(t, () => Response.json([]));
  const result = await GET({url});
  assert.equal(result.status, 200);
  assert.match(await result.text(), /<loc>https:\/\/lefteriafc\.cy\/<\/loc>/);
});

test('public navigation keeps graceful fallback while strict reader rejects partial CMS snapshots', async (t) => {
  const { readWebsitePages } = await import('../src/lib/server/website-pages.ts');
  const org = 'lefteria_fc';
  const mock = mockCms(t, owner => Response.json([publishedPage(owner), {slug:'bad-page'}]));
  assert.equal((await readWebsitePages(org)).length, 1);
  await assert.rejects(readWebsitePages(org, {strict:true}), /Public CMS projection unavailable/);
  mock.mock.restore();
  mockCms(t, () => new Response('', {status:503}));
  assert.deepEqual(await readWebsitePages(org), []);
  await assert.rejects(readWebsitePages(org, {strict:true}), /Public CMS projection unavailable/);
});
