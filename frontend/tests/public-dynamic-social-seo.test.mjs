import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {publicSocialImage,personPublicLink,publicCanonicalHref} from '../src/lib/public/public-urls.ts';

const read=path=>readFileSync(new URL('../src/'+path,import.meta.url),'utf8');

test('Only explicit published HTTPS pictures are used for OpenGraph (no relative private assets)',()=>{
  assert.equal(publicSocialImage('https://images.example.test/club.jpg'),'https://images.example.test/club.jpg');
  for(const src of [undefined,null,'','/api/uploads/private.jpg','http://insecure.test/foo.jpg',
    'javascript:alert(1)','data:image/png;base64,AAA','https://user:pass@example.test/a.jpg',
    'https://example.test/a.jpg\r\nBcc:private','https://example.test/\\private.jpg'])
    assert.equal(publicSocialImage(src),null,String(src));
  assert.equal(publicSocialImage('https://example.test/a'.repeat(400)),null);
});

test('All dynamic public details declare honest per-record social metadata without fake source content',()=>{
  const routes=[
    ['routes/player/[id]/+page.svelte','personPublicLink'],
    ['routes/staff/[id]/+page.svelte','personPublicLink'],
    ['routes/academy/[slug]/+page.svelte','academyGroupLink'],
    ['routes/match/[id]/+page.svelte','fixturePublicLink'],
    ['lib/public/PublicCatalogDetail.svelte','catalogCanonicalDetailLink']
  ];
  for(const [path,canonical] of routes){
    const s=read(path);
    assert.doesNotThrow(()=>compile(s,{filename:path,generate:'server'}),path);
    assert.ok(s.includes(canonical),path+' must use verified public DTO');
    assert.match(s,/property="og:title"/,path);
    assert.match(s,/property="og:description"/,path);
    assert.match(s,/property="og:url"/,path);
    assert.match(s,/name="twitter:card"/,path);
    assert.match(s,/publicSocialImage\(/,path);
    assert.match(s,/<meta property="og:image" content=\{socialImage\}/,path);
    assert.doesNotMatch(s,/\{@html\}|localStorage|private_document|guardian_phone/);
  }
});

test('Young academy players cannot acquire crawlable social cards from their public profile',()=>{
  const page=read('routes/player/[id]/+page.svelte');
  assert.match(page,/Number\(data\.player\.age\) >= 18/);
  assert.match(page,/data\.player\.organization_id === 'lefteria_fc'/);
  assert.match(page,/data\.player\.team_type === 'First Team'/);
  assert.match(page,/\{#if !indexable\}<meta name="robots" content="noindex,follow"\s*\/>/);
  assert.match(page,/\{#if indexable && canonical\}/);
  assert.match(page,/indexable \? publicSocialImage\(data\.player\.image_url\) : null/);
  const a=personPublicLink('player',{id:'130d36bf-d0ae-4b6a-98f0-b29cbba629c5',name:'Δημόσιο Όνομα'});
  assert.ok(publicCanonicalHref(a).startsWith('https://lefteriafc.cy/player/'));
  assert.ok(!a.includes('private'));
  assert.doesNotMatch(page,/\+ '?team_id='|data\.returnTeamId\)\)/);
});

test('News social metadata uses only the available public article and safe HTTPS images',()=>{
  const path='routes/news/[slug]/+page.svelte';
  const page=read(path);
  assert.doesNotThrow(()=>compile(page,{filename:path,generate:'server'}));
  assert.doesNotThrow(()=>compile(page,{filename:path,generate:'client'}));
  assert.match(page,/const canonicalPath = \$derived\(data\.articleAvailable && data\.article/);
  assert.match(page,/canonicalPath && data\.article \? publicSocialImage\(data\.article\.image_url\) : null/);
  assert.match(page,/\{#if canonicalPath\}[\s\S]*property="og:title"[\s\S]*property="og:description"[\s\S]*property="og:url"/);
  assert.match(page,/\{#if socialImage\}\s*<meta property="og:image" content=\{socialImage\}/);
  assert.match(page,/name="twitter:image" content=\{socialImage\}/);
  assert.match(page,/name="twitter:card" content=\{socialImage \? 'summary_large_image' : 'summary'\}/);
  assert.match(page,/\{:else\}\s*<meta name="robots" content="noindex,follow"/);
  assert.doesNotMatch(page,/property="og:image" content=\{data\.article/);
});
