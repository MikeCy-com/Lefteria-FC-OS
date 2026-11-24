import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {readPublicBrand,publicHeroUrl} from '../src/lib/mobile/club-branding.ts';

const read=p=>readFileSync(new URL('../src/'+p,import.meta.url),'utf8');
const parent=read('lib/mobile/components/MobileParentHome.svelte');
const role=read('lib/mobile/components/MobileRoleHome.svelte');
const route=read('routes/app/[...path]/+page.svelte');

const academy=(groups=[])=>({
  organization_id:'lefteria_fc_academy',
  club:{organization_id:'lefteria_fc_academy',name:'Academy'},
  academy_groups:groups
});
test('only published Academy group imagery can appear in Academy member home',()=>{
  const firstTeamBanner='https://example.org/fc-team.jpg';
  const academyBanner='https://example.org/published-academy.jpg';
  const payload=academy([
    {organization_id:'lefteria_fc',banner_url:firstTeamBanner},
    {organization_id:'lefteria_fc_academy',banner_url:'javascript:alert(1)'},
    {organization_id:'lefteria_fc_academy',banner_url:academyBanner}
  ]);
  assert.equal(readPublicBrand(payload,'lefteria_fc_academy')?.heroUrl,academyBanner);
  assert.equal(readPublicBrand(academy([{organization_id:'lefteria_fc',banner_url:firstTeamBanner}]),
    'lefteria_fc_academy')?.heroUrl,null);
  assert.equal(readPublicBrand({...payload,organization_id:'lefteria_fc'},'lefteria_fc_academy'),null);
  assert.equal(readPublicBrand({...payload,club:{organization_id:'lefteria_fc',name:'Wrong'}},
    'lefteria_fc_academy'),null);
  assert.equal(readPublicBrand({organization_id:'lefteria_fc',
    club:{organization_id:'lefteria_fc',name:'First Team'},
    academy_groups:[{organization_id:'lefteria_fc_academy',banner_url:academyBanner}]},
    'lefteria_fc')?.heroUrl,null);
});

test('inline background image accepts URL-safe published sources only',()=>{
  for(const url of ['/media/academy/banner.webp','https://cdn.example.org/photo.jpg'])
    assert.equal(publicHeroUrl(url),url);
  for(const url of ['javascript:alert(1)','//cdn.example.org/pic.jpg',
    'http://insecure.example.org/banner.jpg','https://x.invalid/a") ; color:red; /*',
    'https://x.invalid/a(b).png','/media/img\\x.png','https://x.invalid/a\n.jpg',
    'https://x.invalid/a.png '])
    assert.equal(publicHeroUrl(url),null,url);
});

test('private role and parent hero components never hardcode First Team image',()=>{
  for(const source of [parent,role]){
    assert.doesNotMatch(source,/publicImages\.hero/);
    assert.match(source,/heroUrl/);
    assert.match(source,/url\("\$\{heroUrl\}"\)/);
  }
  assert.match(parent,/class:parent-hero-photographic=\{!!heroUrl\}/);
  assert.match(parent,/\.parent-greeting:not\(\.parent-hero-photographic\)\{min-height:0\}/);
  assert.match(role,/class:hero-photographic=\{!!heroUrl&&\(role==='player'\|\|role==='coach'\)\}/);
  assert.match(route,/heroUrl=\{publicClubBrand\?\.heroUrl\?\?null\}/);
  assert.equal((route.match(/heroUrl=\{publicClubBrand\?\.heroUrl\?\?null\}/g)||[]).length,2);
  assert.match(route,/credentials:'omit',cache:'no-store'/);
});
