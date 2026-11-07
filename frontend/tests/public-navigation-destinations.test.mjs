import test from 'node:test';
import assert from 'node:assert/strict';
import {existsSync,readFileSync} from 'node:fs';
const url=(path)=>new URL('../src/routes/'+path,import.meta.url);
const text=(path)=>readFileSync(url(path),'utf8');

test('all original top-level menu journeys have a real, safely branded destination',()=>{
  for(const [route,organization] of [
    ['join','lefteria_fc_academy'],
    ['trials','lefteria_fc'],
    ['vote','lefteria_fc'],
    ['academy/registration','lefteria_fc_academy']
  ]) {
    assert.ok(existsSync(url(route+'/+page.svelte')),route);
    assert.ok(existsSync(url(route+'/+page.server.ts')),route);
    const page=text(route+'/+page.svelte');
    const server=text(route+'/+page.server.ts');
    assert.match(page,/PublicJourney/);
    assert.ok(page.includes('organization="'+organization+'"'),route);
    assert.match(page,/status="[^"]+"/);
    assert.match(server,/loadPublicSite\(url/);
    assert.doesNotMatch(page,/<form|type="submit"|medical|payment|stripe|axios/i,
      'Do not fake collecting personal, child-medical or financial data');
  }
});

test('legacy header/footer cart and account links have explicit nontransactional redirects',()=>{
  for(const [route,target] of [
    ['cart','/shop?tab=merchandise'],['profile','/login'],['register','/academy/registration']
  ]) {
    const content=text(route+'/+server.ts');
    assert.match(content,/export function GET\(\): Response/);
    assert.ok(content.includes("redirect(307, '"+target+"')"));
    assert.doesNotMatch(content,/POST|PUT|DELETE|payment|checkout/i);
  }
});

test('source-matched journey UI is one semantic main, Fira fonts and safe real links',()=>{
  const page=readFileSync(new URL('../src/lib/public/PublicJourney.svelte',import.meta.url),'utf8');
  assert.match(page,/PublicShell/);
  assert.doesNotMatch(page,/<main\b/);
  assert.match(page,/Fira Sans Extra Condensed/);
  assert.match(page,/#f5a623/);
  assert.match(page,/data-testid="journey-primary"/);
  assert.match(page,/data-testid="journey-capability-status"/);
  assert.doesNotMatch(page,/<form|requestSubmit\(|fetch\(|submit\s*=|onclick=\{.*submit/i);
});
