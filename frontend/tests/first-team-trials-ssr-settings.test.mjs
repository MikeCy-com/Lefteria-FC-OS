import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { verifiedClosedTrialSettings } from '../src/lib/public/trials-settings-policy.ts';

const read=(path)=>readFileSync(new URL('../src/'+path,import.meta.url),'utf8');

test('first-team trials stay closed for absent, malformed or open-marked API responses',()=>{
  for(const upstream of [null,undefined,{},[],0,'invalid',{open:true},{open:'true'}]){
    const data=verifiedClosedTrialSettings(upstream);
    assert.equal(data.open,false);
    assert.equal(data.headline,'Ετοιμος να Ξεκινησεις το Ταξιδι σου;');
    assert.match(data.closed_message,/προσωρινά κλειστές/);
  }
});

test('safe upstream copy is bounded, while opening enrollment remains forbidden',()=>{
  const safe=verifiedClosedTrialSettings({
    open:true,headline:'Επιλεγμένες δοκιμές',subtitle:'Νέα ενημέρωση',
    button_text:'Αίτηση',closed_message:'Προσωρινά κλειστό'
  });
  assert.equal(safe.open,false);
  assert.equal(safe.headline,'Επιλεγμένες δοκιμές');
  assert.equal(safe.subtitle,'Νέα ενημέρωση');

  for(const malicious of [
    {headline:' '},{headline:' unsafe'},
    {headline:'unsafe\nline'}, {headline:new Array(182).join('x')},
    {subtitle:new Array(502).join('x')},
    {button_text:'invalid\0control'},
    {closed_message:new Array(502).join('x')}
  ]){
    const data=verifiedClosedTrialSettings({open:true,...malicious});
    for(const field of Object.keys(malicious))
      assert.notEqual(data[field],malicious[field],field);
    assert.equal(data.open,false);
  }
});

test('first-team SSR uses only credential-free fixed-scope public API',()=>{
  const server=read('lib/server/trials-settings.ts');
  const loader=read('routes/trials/+page.server.ts');
  const page=read('routes/trials/+page.svelte');
  assert.ok(server.includes("publicApi().GET('/api/trials/settings')"));
  assert.ok(server.includes('verifiedClosedTrialSettings(null)'));
  assert.ok(loader.includes("loadPublicSite(url, 'lefteria_fc')"));
  assert.ok(loader.includes('loadFirstTeamTrialSettings()'));
  assert.ok(page.includes('data.trialSettings.headline'));
  assert.ok(page.includes('data.trialSettings.closed_message'));
  assert.doesNotMatch(page,/\{#if[^\n]*trialSettings\.open/);
  assert.doesNotMatch(page,/<form\b|type="submit"|data-testid="trials-success"/i);
  assert.doesNotMatch(server,/cookie|authorization|X-Forwarded|process\.env|fetch\(url/i);
});
