import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {
  catalogHref,catalogCanonicalDetailLink,catalogCanonicalRedirect
} from '../src/lib/public/PublicCatalog.ts';
import { publicCanonicalHref } from '../src/lib/public/public-urls.ts';

const read=path=>readFileSync(new URL('../src/'+path,import.meta.url),'utf8');

test('All public catalog entity links transliterate Greek and retain stable ID and owner',()=>{
  const academy={id:'130d36bf-d0ae-4b6a-98f0-b29cbba629c5',organization_id:'lefteria_fc_academy'};
  const club={...academy,organization_id:'lefteria_fc'};
  assert.equal(catalogHref('events',academy,'Ημέρα Ακαδημίας'),
    '/events/imera-akadimias--130d36bf-d0ae-4b6a-98f0-b29cbba629c5?scope=academy');
  assert.equal(catalogHref('events',club,'Ημέρα Ακαδημίας'),
    '/events/imera-akadimias--130d36bf-d0ae-4b6a-98f0-b29cbba629c5?scope=first_team');
  assert.equal(catalogCanonicalDetailLink('partners',academy,'Συνεργάτης','verified-partner'),
    '/partners/verified-partner?scope=academy');
  assert.equal(catalogCanonicalDetailLink('partners',club,'Συνεργάτης','/evil?scope=academy'),
    '/partners/synergatis--130d36bf-d0ae-4b6a-98f0-b29cbba629c5?scope=first_team');
  assert.equal(catalogCanonicalDetailLink('seasons',academy,'Σεζόν 2025/26'),
    '/seasons/sezon-2025-26--130d36bf-d0ae-4b6a-98f0-b29cbba629c5?scope=academy');
});

test('Existing opaque and renamed URLs redirect permanently only after record resolution, with safe filters',()=>{
  const canonical='/events/epishmi-ekdilosi--abc?scope=academy';
  const url=new URL('https://preview.test/events/abc?scope=first_team&list_scope=all&when=past&type=trial&token=secret');
  const redirect=catalogCanonicalRedirect(url,canonical);
  assert.equal(redirect,'/events/epishmi-ekdilosi--abc?scope=academy&list_scope=all&when=past&type=trial');
  assert.equal(catalogCanonicalRedirect(new URL('https://preview.test/events/epishmi-ekdilosi--abc?scope=academy'),canonical),null);
  assert.equal(catalogCanonicalRedirect(new URL('https://preview.test/events/old?when=unknown&list_scope=%0a&type=bad%0a'),canonical),
    '/events/epishmi-ekdilosi--abc?scope=academy');
  assert.equal(publicCanonicalHref('/events/epishmi-ekdilosi--abc?scope=academy&when=all&token=secret'),
    'https://lefteriafc.cy/events/epishmi-ekdilosi--abc?scope=academy');
  assert.equal(publicCanonicalHref('/contact?organization_id=lefteria_fc_academy&private=1'),
    'https://lefteriafc.cy/contact');
  assert.equal(publicCanonicalHref('/player/public-name--uuid?team_id=secret'),
    'https://lefteriafc.cy/player/public-name--uuid');
});

test('Each catalog detail redirects after its owner-scoped loader resolves a real published record',()=>{
  const kinds=['events','partners','sponsors','seasons'];
  for(const kind of kinds){
    const route=read('routes/'+kind+'/[id]/+page.server.ts');
    assert.match(route,/await loadPublicCatalogDetail\(url,/);
    assert.match(route,/catalogCanonicalDetailLink\(/);
    assert.match(route,/catalogCanonicalRedirect\(url,canonical\)/);
    assert.match(route,/if\(redirectTo\) redirect\(301,redirectTo\)/);
    assert.match(route,/if \(!row\) error\(404,/);
    assert.doesNotMatch(route,/fetch\(|credentials|authorization/);
  }
  const detail=read('lib/public/PublicCatalogDetail.svelte');
  const shell=read('lib/public/PublicShell.svelte');
  assert.doesNotThrow(()=>compile(detail,{filename:'PublicCatalogDetail.svelte',generate:'server'}));
  assert.doesNotThrow(()=>compile(shell,{filename:'PublicShell.svelte',generate:'server'}));
  assert.match(detail,/catalogCanonicalDetailLink\(/);
  assert.match(detail,/<PublicShell[^>]*\{canonicalPath\}/);
  assert.match(shell,/publicCanonicalHref\(canonicalPath \?\? page\.url\.pathname\)/);
});
