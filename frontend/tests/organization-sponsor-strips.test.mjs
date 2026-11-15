import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const strip=read('frontend/src/lib/public/OrganizationSponsorStrip.svelte');
const academy=read('frontend/src/routes/academy/+page.svelte');
const academyLoader=read('frontend/src/routes/academy/+page.server.ts');
const team=read('frontend/src/routes/team/+page.svelte');

test('academy and Club sponsor strips use only matching record organizations',()=>{
  for(const [name,source] of [['OrganizationSponsorStrip',strip],['academy/+page',academy],['team/+page',team]])
    assert.ok(compile(source,{filename:name+'.svelte',generate:'server'}).js.code.length>100);
  assert.match(strip,/sponsors\.filter\(item => item\.organization_id === organization\)/);
  assert.match(strip,/\/sponsors\/academy/);
  assert.match(strip,/\/sponsors\/first-team/);
  assert.match(strip,/catalogHref\('sponsors',sponsor,sponsor\.name\)/);
  assert.match(strip,/list_scope=/);
  assert.match(academyLoader,/loadSectionPage\(url, 'lefteria_fc_academy'\)/);
  assert.match(academy,/organization="lefteria_fc_academy" sponsors=\{data\.sections\?\.sponsors \?\? \[\]\}/);
  assert.match(team,/organization="lefteria_fc" sponsors=\{data\.sections\?\.sponsors \?\? \[\]\}/);
  assert.match(team,/data\.selectedTeamId && data\.selectedTeamId ===/);
});
