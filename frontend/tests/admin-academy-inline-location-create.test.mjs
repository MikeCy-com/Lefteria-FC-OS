import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read = path => readFileSync(new URL('../../' + path, import.meta.url), 'utf8');
const ui = read('frontend/src/routes/admin/groups/+page.svelte');
const territories = read('backend/src/LefteriaFc.Api/Football/AdminFootballTerritoryEndpoints.cs');
const groups = read('backend/src/LefteriaFc.Api/Audiences/AdminProfiledGroupCreateEndpoints.cs');

test('new Academy team form compiles with separate scoped city and location selection', () => {
  assert.ok(compile(ui, {filename:'groups/+page.svelte', generate:'server'}).js.code.length > 100);
  assert.match(ui, /data-testid="admin-group-city"/);
  assert.match(ui, /data-testid="admin-group-verified-location"/);
  assert.match(ui, /cityLocations=\$derived\(creationLocations\.filter\(site=>site\.city_id===creationCityId\)\)/);
  assert.match(ui, /selectedLocation\.city_id!==creationCityId/);
  assert.match(ui, /row\.can_manage!==true/);
  assert.match(ui, /scopedCities\.has\(row\.city_id\)/);
});

test('inline location creation uses existing authenticated territory endpoint', () => {
  assert.match(ui, /data-testid="admin-group-add-location"/);
  assert.match(ui, /data-testid="admin-group-inline-location"/);
  assert.match(ui, /kind:'location',data:\{/);
  assert.match(ui, /city_id:city,name,slug,address:locationAddress/);
  assert.match(ui, /pendingLocationId=/);
  assert.match(ui, /locationReload\+\+/);
  assert.match(ui, /const mayCreateLocation=\$derived\(creationCities\.some\(city=>/);
  assert.match(ui, /scope\.can_write_catalog===true/);
  assert.match(ui, /!mayCreateLocation/);
  assert.match(ui, /maxlength="300" disabled=\{locationSaving \|\| saving\}/);
  assert.match(territories, /create_admin_football_resource\(@digest,@owner,@kind,@data\)/);
  assert.match(territories, /AdminPermissions\.Has\(session\.User\.Role, permission\)/);
  assert.match(groups, /create_admin_academy_location_group_with_profile/);
});

test('creating a location does not reset the unfinished team details', () => {
  const method = ui.slice(ui.indexOf('async function createInlineLocation()'),
    ui.indexOf('async function createGroup()'));
  for(const field of ['groupName', 'academyAge', 'academyCoach', 'academySchedule',
    'academyDescription', 'academyCapacity', 'academySeason', 'academyBanner', 'academyOrder'])
    assert.doesNotMatch(method, new RegExp(field + "\\s*="));
  assert.doesNotMatch(method, /localStorage|sessionStorage|Math\.random/);
});
