import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {buildAcademyTerritoryDirectory} from '../src/lib/public/academy-territory-directory.ts';

const verified = (id,cityId,cityName,citySlug,locationId,locationName,locationSlug) => ({
  id, organization_id:'lefteria_fc_academy', name:'U12 '+id,
  city_id:cityId,city:cityName,city_slug:citySlug,
  location_id:locationId,location_name:locationName,location_slug:locationSlug
});

test('Academy city -> location -> groups uses only verified IDs, not imported free text',()=>{
  const north=verified('north','cityA','Alpha','alpha','locN','North','north');
  const south=verified('south','cityA','Alpha','alpha','locS','South','south');
  const second=verified('second','cityA','Alpha','alpha','locN','North','north');
  const east=verified('east','cityB','Beta','beta','locE','East','east');
  const unassigned={id:'legacy',organization_id:'lefteria_fc_academy',
    city:'Alpha',location_name:'North',name:'Legacy with matching text'};
  const directory=buildAcademyTerritoryDirectory([north,south,second,east,unassigned]);
  assert.equal(directory.cities.length,2);
  assert.deepEqual(directory.cities.map(city=>city.name),['Alpha','Beta']);
  assert.deepEqual(directory.cities[0].locations.map(loc=>loc.name),['North','South']);
  assert.deepEqual(directory.cities[0].locations[0].groups.map(g=>g.id),['north','second']);
  assert.deepEqual(directory.cities[0].locations[1].groups.map(g=>g.id),['south']);
  assert.deepEqual(directory.cities[1].locations[0].groups.map(g=>g.id),['east']);
  assert.deepEqual(directory.unassigned.map(g=>g.id),['legacy']);
});

test('Partial IDs, malformed slugs and First Team content never become Academy hierarchy',()=>{
  const full=verified('full','cityA','Alpha','alpha','locN','North','north');
  const partiallyAssigned={...full,id:'partial',location_id:null};
  const badSlug={...full,id:'bad',city_slug:'../alpha'};
  const firstTeam={...full,id:'first-team',organization_id:'lefteria_fc'};
  const result=buildAcademyTerritoryDirectory([partiallyAssigned,badSlug,firstTeam]);
  assert.deepEqual(result.cities,[]);
  assert.deepEqual(result.unassigned.map(g=>g.id),['partial','bad','first-team']);
  assert.deepEqual(buildAcademyTerritoryDirectory([]),{cities:[],unassigned:[]});
});

test('Public UI uses accessible real nested locations, preserving public card links and placeholders',()=>{
  const source=readFileSync(new URL('../src/routes/academy/groups/+page.svelte',import.meta.url),'utf8');
  const card=readFileSync(new URL('../src/lib/public/AcademyDirectoryGroupCard.svelte',import.meta.url),'utf8');
  assert.match(source,/aria-label="Πόλεις της ακαδημίας"/);
  assert.match(source,/aria-labelledby={`academy-city-/);
  assert.match(source,/aria-labelledby={`academy-location-/);
  assert.match(source,/academy-verified-city/);
  assert.match(source,/academy-verified-location/);
  assert.match(source,/academy-unassigned-groups/);
  assert.match(source,/academy-groups-player-previews/);
  assert.match(source,/academy-groups-registration/);
  assert.match(card,/academyGroupLink\(group\)/);
  assert.match(card,/academy-public-coach/);
  assert.match(card,/group\.training_schedule/);
});

test('Backend geography projection is privileged, active-only, published-only and strips private columns',()=>{
  const migration=readFileSync(new URL(
    '../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115220000_PublicAcademyVerifiedTerritories.cs',import.meta.url),'utf8');
  const contract=readFileSync(new URL(
    '../../backend/src/LefteriaFc.Application/PublicContent/PublicContentContracts.cs',import.meta.url),'utf8');
  const checks=readFileSync(new URL('../../ops/postgres/public-academy-territory-checks.sql',import.meta.url),'utf8');
  const ci=readFileSync(new URL('../../.github/workflows/rewrite-build.yml',import.meta.url),'utf8');
  assert.match(migration,/SECURITY DEFINER/);
  assert.match(migration,/SET search_path=pg_catalog,pg_temp/);
  assert.match(migration,/club\.academy_group_location_assignments a/);
  assert.match(migration,/club\.football_locations l/);
  assert.match(migration,/club\.football_cities c/);
  assert.match(migration,/p\.is_public/);
  assert.match(migration,/l\.is_active AND c\.is_active/);
  assert.match(migration,/REVOKE ALL ON FUNCTION club\.read_public_academy_group_directory/);
  for(const field of ['city_id','location_id','city_slug','location_slug'])
    assert.ok(migration.includes("'"+field+"'"),field+' not allowlisted');
  assert.match(contract,/string\? CityId = null, string\? LocationId = null/);
  assert.match(checks,/Wrong imported city/i);
  assert.match(checks,/Inactive city still publicly exposes assigned groups/);
  assert.match(checks,/Unassigned legacy content was falsely promoted/);
  assert.match(checks,/ROLLBACK;/);
  assert.match(ci,/public-academy-territory-checks\.sql/);
});
