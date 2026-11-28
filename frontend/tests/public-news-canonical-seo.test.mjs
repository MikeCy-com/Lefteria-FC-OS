import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { compile } from 'svelte/compiler';
import { publicCanonicalHref } from '../src/lib/public/public-urls.ts';
import { articleLink } from '../src/lib/public/content.ts';

const source = path => readFileSync(new URL('../src/' + path, import.meta.url),'utf8');

test('Published news article canonicals include verified editorial owner, never private filters',()=>{
  const article={id:'130d36bf-d0ae-4b6a-98f0-b29cbba629c5',title:'Νέα της Ακαδημίας',organization_id:'lefteria_fc_academy'};
  const scoped=articleLink(article,'lefteria_fc_academy');
  assert.ok(scoped.includes('νέα-της-ακαδημίας--') || scoped.includes('νεα-της-ακαδημιας--'));
  assert.equal(publicCanonicalHref(scoped + '&token=private&team_id=secret'),
    'https://lefteriafc.cy' + new URL(scoped,'https://lefteriafc.cy').pathname + '?organization_id=lefteria_fc_academy');
  assert.equal(publicCanonicalHref('/news/old-article--id?organization_id=unknown&token=private'),
    'https://lefteriafc.cy/news/old-article--id');
  assert.equal(publicCanonicalHref('/news/old-article--id?organization_id=lefteria_fc'),
    'https://lefteriafc.cy/news/old-article--id?organization_id=lefteria_fc');
  assert.equal(publicCanonicalHref('/team?organization_id=lefteria_fc_academy'),
    'https://lefteriafc.cy/team');
});

test('News route verifies published article before redirect and blocks cross-organization rendering',()=>{
  const route=source('routes/news/[slug]/+page.server.ts');
  const page=source('routes/news/[slug]/+page.svelte');
  assert.match(route,/const data = await loadPublicArticle\(url, params\.slug\)/);
  assert.match(route,/data\.article\.organization_id !== data\.newsScope/);
  assert.match(route,/error\(404/);
  assert.match(route,/articleLink\(data\.article, data\.newsScope\)/);
  assert.match(route,/redirect\(301, canonicalUrl\.pathname \+ canonicalUrl\.search\)/);
  assert.doesNotMatch(route,/fetch\(|password|private|Bearer/);
  assert.match(page,/const canonicalPath = \$derived\(data\.articleAvailable && data\.article/);
  assert.match(page,/og:url/);
  assert.match(page,/\{canonicalPath\}>/);
  assert.doesNotThrow(()=>compile(page,{filename:'src/routes/news/[slug]/+page.svelte',generate:'server'}));
});

test('native CMS canonicals preserve Academy owner but discard untrusted filters', () => {
  assert.equal(publicCanonicalHref('/pages/parents-guide?organization_id=lefteria_fc_academy&token=private'),
    'https://lefteriafc.cy/pages/parents-guide?organization_id=lefteria_fc_academy');
  assert.equal(publicCanonicalHref('/pages/parents-guide?organization_id=lefteria_fc&token=private'),
    'https://lefteriafc.cy/pages/parents-guide');
  assert.equal(publicCanonicalHref('/pages/parents-guide?organization_id=spoofed&token=private'),
    'https://lefteriafc.cy/pages/parents-guide');
  assert.equal(publicCanonicalHref('/team?organization_id=lefteria_fc_academy'),
    'https://lefteriafc.cy/team');
  const cmsPage=source('routes/pages/[slug]/+page.svelte');
  assert.match(cmsPage, /og:url/);
  assert.match(cmsPage, /publicCanonicalHref\(canonicalPath\)/);
  assert.doesNotThrow(()=>compile(cmsPage,{filename:'src/routes/pages/[slug]/+page.svelte',generate:'server'}));
});
