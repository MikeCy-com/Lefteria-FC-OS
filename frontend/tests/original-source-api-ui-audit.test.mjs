import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {existsSync} from 'node:fs';
import {fileURLToPath} from 'node:url';
import {resolve} from 'node:path';
const root=resolve(fileURLToPath(new URL('../..',import.meta.url)));
const source=path=>readFileSync(new URL('../'+path,import.meta.url),'utf8');

// The historical source was inspected read-only in MikeCy-com/Lefteria-FC.
// These are pinned ORIGINAL navigation identities, not guesses based on OS.
const originalPublicRoutes={
  home:['/'],club:['/about','/join','/seasons','/partners',
    '/sponsors/first-team','/sponsors/academy'],
  team:['/team','/team?tab=roster','/team?tab=results',
    '/team?tab=schedule','/team?tab=gallery','/team?tab=venues',
    '/trials','/partners?scope=first_team'],
  academy:['/academy','/academy/philosophy','/academy/groups',
    '/academy/registration','/partners?scope=academy'],
  matches:['/team?tab=schedule','/team?tab=results','/team?tab=overview#standings',
    '/vote','/shop'],
  news:['/news'],events:['/events'],shop:['/shop'],contact:['/contact']
};
test('all nine original public menu destinations are still in the OS navigation',()=>{
  const navigation=source('src/lib/public/navigation.ts');
  for(const [name,paths] of Object.entries(originalPublicRoutes)){
    assert.match(navigation,new RegExp('id: ["\\x27]'+name+'["\\x27]'),name);
    for(const path of paths)assert.ok(navigation.includes('"'+path+'"'),
      name+': original destination absent '+path);
  }
});
test('legacy staff destination preserves original First Team overview redirect',()=>{
  assert.match(source('src/routes/staff/+page.server.ts'),/redirect\(308, '\/team\?tab=overview'\)/);
  assert.match(source('src/lib/public/navigation.ts'),/['"]\/staff['"]: ['"]overview['"]/);
  assert.match(source('src/routes/team/+page.svelte'),/data.tab === 'staff'/);
});
test('unported sensitive source flows remain honest instead of fake success',()=>{
  for(const path of ['join','academy/registration','vote']){
    const s=source('src/routes/'+path+'/+page.svelte');
    assert.match(s,/PublicJourney/);
    assert.doesNotMatch(s,/fetch\([^)]*method: ['"]POST['"]/);
  }
  const trial=source('src/routes/trials/+page.svelte');
  assert.match(trial,/trials-closed/);
  assert.doesNotMatch(trial,/\bhandleSubmit\b/);
});
test('each source role has a declared OS navigation contract',()=>{
  const nav=source('src/lib/mobile/navigation.ts');
  for(const role of ['parent','player','coach','fan','management'])
    assert.match(nav,new RegExp('^\\s*'+role+':\\s*\\{','m'),role);
});
test('the audit script exists and explicitly separates inventory from runtime proof',()=>{
  assert.equal(existsSync(resolve(root,'frontend/scripts/audit-original-parity.mjs')),true);
  const script=source('scripts/audit-original-parity.mjs');
  assert.match(script,/backend\/src\/LefteriaFc.Api/);
  assert.match(script,/frontend\/src\/routes/);
  assert.match(script,/operationally_verified:false/);
  assert.match(script,/These counts do not prove runtime/);
});

test('pinned source inventory enumerates all 39 public routes and 72 admin render labels',()=>{
  const inventory=readFileSync(new URL('../../docs/implementation/route-parity-inventory.md',
    import.meta.url),'utf8');
  function section(name){
    const start=inventory.indexOf(name);
    assert.ok(start>=0,name);
    const end=inventory.indexOf('\n## ',start+name.length);
    return inventory.slice(start,end<0?undefined:end);
  }
  const tick=String.fromCharCode(96);
  const pages=section('## Every original public route')
    .split('\n').filter(row=>row.startsWith('| ['+tick));
  const admin=section('## Every original AdminPanel render target')
    .split('\n').filter(row=>row.startsWith('| '+tick));
  assert.equal(pages.length,39);
  assert.equal(admin.length,72);
  const script=source('scripts/audit-original-parity.mjs');
  assert.match(script,/original_route_coverage:originalRouteCoverage/);
  assert.match(script,/sourcePublicRows/);
  assert.match(script,/runtime_verified:false/);
});
