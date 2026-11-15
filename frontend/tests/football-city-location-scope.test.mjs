import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import {scopedFootballMatchOptions,scopedFootballTrainingBases} from '../src/lib/admin/football-catalog-scope.ts';

const scopes=[
  {id:'club-main',organization_id:'lefteria_fc',team_id:'team-main',academy_city_id:null},
  {id:'city-a',organization_id:'lefteria_fc_academy',team_id:null,academy_city_id:'a'},
  {id:'city-b',organization_id:'lefteria_fc_academy',team_id:null,academy_city_id:'b'}
];
const locations=[
  {id:'north',city_id:'a',is_active:true},
  {id:'south',city_id:'a',is_active:true},
  {id:'east',city_id:'b',is_active:true},
  {id:'inactive-north',city_id:'a',is_active:false}
];
const opponents=[
  {id:'club-opponent',scope_id:'club-main',organization_id:'lefteria_fc',name:'Club Opponent',is_active:true},
  {id:'a-opponent',scope_id:'city-a',organization_id:'lefteria_fc_academy',name:'Academy City A Opponent',is_active:true},
  {id:'a-inactive',scope_id:'city-a',organization_id:'lefteria_fc_academy',name:'Inactive',is_active:false},
  {id:'b-opponent',scope_id:'city-b',organization_id:'lefteria_fc_academy',name:'Academy City B Opponent',is_active:true}
];
const fields=[
  {id:'club-field',scope_id:'club-main',organization_id:'lefteria_fc',name:'Club Playing Field',is_active:true},
  {id:'a-field',scope_id:'city-a',organization_id:'lefteria_fc_academy',name:'Shared City A Playing Field',is_active:true},
  {id:'b-field',scope_id:'city-b',organization_id:'lefteria_fc_academy',name:'City B Playing Field',is_active:true}
];
const bases=[
  {id:'club-base',scope_id:'club-main',organization_id:'lefteria_fc',name:'Club Training Base',academy_city_id:null,academy_location_id:null,is_active:true},
  {id:'north-base',scope_id:'city-a',organization_id:'lefteria_fc_academy',name:'North Base',academy_city_id:'a',academy_location_id:'north',is_active:true},
  {id:'south-base',scope_id:'city-a',organization_id:'lefteria_fc_academy',name:'South Base',academy_city_id:'a',academy_location_id:'south',is_active:true},
  {id:'b-base',scope_id:'city-b',organization_id:'lefteria_fc_academy',name:'City B Base',academy_city_id:'b',academy_location_id:'east',is_active:true},
  {id:'a-inactive-base',scope_id:'city-a',organization_id:'lefteria_fc_academy',name:'Inactive Base',academy_city_id:'a',academy_location_id:'north',is_active:false}
];

test('two Academy locations in SAME city see identical city-level opponents and playing fields',()=>{
  const north={organizationId:'lefteria_fc_academy',academyCityId:'a',academyLocationId:'north'};
  const south={organizationId:'lefteria_fc_academy',academyCityId:'a',academyLocationId:'south'};
  const first=scopedFootballMatchOptions(north,scopes,locations,opponents,fields);
  const second=scopedFootballMatchOptions(south,scopes,locations,opponents,fields);
  assert.deepEqual(first,second);
  assert.deepEqual(first.opponents.map(item=>item.id),['a-opponent']);
  assert.deepEqual(first.playingFields.map(item=>item.id),['a-field']);
});
test('cities never see other cities or First Team catalogs',()=>{
  const b=scopedFootballMatchOptions(
    {organizationId:'lefteria_fc_academy',academyCityId:'b',academyLocationId:'east'},
    scopes,locations,opponents,fields);
  assert.deepEqual(b.opponents.map(item=>item.id),['b-opponent']);
  assert.deepEqual(b.playingFields.map(item=>item.id),['b-field']);
  const club=scopedFootballMatchOptions(
    {organizationId:'lefteria_fc',teamScopeId:'club-main'},scopes,locations,opponents,fields);
  assert.deepEqual(club.opponents.map(item=>item.id),['club-opponent']);
  assert.deepEqual(club.playingFields.map(item=>item.id),['club-field']);
  assert.deepEqual(scopedFootballMatchOptions(
    {organizationId:'lefteria_fc_academy',academyCityId:'missing',academyLocationId:'north'},
    scopes,locations,opponents,fields),{opponents:[],playingFields:[]});
  assert.deepEqual(scopedFootballMatchOptions(
    {organizationId:'lefteria_fc',teamScopeId:'city-a'},scopes,locations,opponents,fields),
    {opponents:[],playingFields:[]});
  assert.deepEqual(scopedFootballMatchOptions(
    {organizationId:'lefteria_fc_academy',academyCityId:'a',academyLocationId:'east'},
    scopes,locations,opponents,fields),{opponents:[],playingFields:[]},
    'Do not display City A catalogs when the chosen location actually belongs to City B');
  assert.deepEqual(scopedFootballMatchOptions(
    {organizationId:'lefteria_fc_academy',academyCityId:'a',academyLocationId:'inactive-north'},
    scopes,locations,opponents,fields),{opponents:[],playingFields:[]},
    'Inactive locations cannot enumerate city catalogs');
});
test('Academy training base lists stay LOCATION-specific even when fields are shared',()=>{
  const north={organizationId:'lefteria_fc_academy',academyCityId:'a',academyLocationId:'north'};
  const south={organizationId:'lefteria_fc_academy',academyCityId:'a',academyLocationId:'south'};
  assert.deepEqual(scopedFootballTrainingBases(north,scopes,locations,bases).map(item=>item.id),['north-base']);
  assert.deepEqual(scopedFootballTrainingBases(south,scopes,locations,bases).map(item=>item.id),['south-base']);
  assert.deepEqual(scopedFootballTrainingBases(
    {organizationId:'lefteria_fc_academy',academyCityId:'b',academyLocationId:'north'},scopes,locations,bases),[]);
  assert.deepEqual(scopedFootballTrainingBases(
    {organizationId:'lefteria_fc',teamScopeId:'club-main'},scopes,locations,bases).map(item=>item.id),['club-base']);
  assert.deepEqual(scopedFootballTrainingBases(
    {organizationId:'lefteria_fc_academy',academyCityId:'a',academyLocationId:'east'},
    scopes,locations,bases),[]);
});
test('SQL schema enforces Academy CITY catalogs, LOCATION group and training base membership',()=>{
  const migration=readFileSync(new URL(
    '../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115210000_FootballTerritoryCatalogFoundation.cs',
    import.meta.url),'utf8');
  const postgresChecks=readFileSync(new URL('../../ops/postgres/football-city-location-catalog-checks.sql',import.meta.url),'utf8');
  assert.match(migration,/academy_city_id uuid REFERENCES club\.football_cities\(id\)/);
  assert.match(migration,/UNIQUE\(academy_city_id\)/);
  assert.match(migration,/FOREIGN KEY\(city_id,location_id\)[\s\S]*?club\.football_locations\(city_id,id\)/);
  assert.match(migration,/FOREIGN KEY\(city_id,scope_id\)[\s\S]*?academy_city_id,id/);
  assert.match(migration,/FOREIGN KEY\(academy_group_id,scope_id,academy_location_id\)/);
  assert.match(migration,/FOREIGN KEY\(scope_id,opponent_id\)/);
  assert.match(migration,/FOREIGN KEY\(scope_id,playing_field_id\)/);
  assert.match(migration,/football_training_bases\(academy_location_id,lower\(name\)\)/);
  assert.match(migration,/FOREIGN KEY\(academy_city_id,academy_location_id\)/);
  assert.match(postgresChecks,/Both Academy locations in Alpha use EXACTLY the same opponent and pitch/);
  assert.match(postgresChecks,/Cross-city opponent bypass succeeded/);
  assert.match(postgresChecks,/Cross-city field bypass succeeded/);
  assert.match(postgresChecks,/Cross-location group bypass succeeded/);
  assert.match(postgresChecks,/Club-to-Academy opponent bypass succeeded/);
  assert.match(postgresChecks,/Two distinct location bases did not coexist/);
  assert.match(postgresChecks,/ROLLBACK;/);
});
