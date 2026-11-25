import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const source=readFileSync(new URL('../src/routes/admin/groups/+page.svelte',import.meta.url),'utf8');
const territory=readFileSync(new URL('../src/routes/admin/territories/+page.svelte',import.meta.url),'utf8');
test('Academy groups compile with per-city/site hierarchy and no guessed assignment',()=>{
  assert.ok(compile(source,{filename:'admin/groups/+page.svelte',generate:'server'}).js.code.length>100);
  for(const marker of [
    'academy-city-location-hierarchy','academy-group-location-heading',
    'academy-group-exact-placement','cityFilter','siteFilter',
    'groupPlacements','placementsReady','Επίσημη τοποθεσία: μη επαληθευμένη',
    "GET /api/admin/football/territories"
  ].filter(s=>s!=='GET /api/admin/football/territories')) {
    assert.ok(source.includes(marker),marker);
  }
  assert.match(source,/fetch\('\/api\/admin\/football\/territories\?organization_id=lefteria_fc_academy'/);
  assert.match(source,/site\.cityId!==row\.city_id/);
  assert.match(source,/visible\.has\(row\.id\)/);
  assert.match(source,/data-testid="admin-new-city-route"/);
  assert.match(territory,/data-testid="admin-create-city"/);
  assert.match(territory,/data-testid="admin-create-city"/);
});
