import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {scopedFootballMatchOptions} from '../src/lib/admin/football-catalog-scope.ts';

const read=path=>readFileSync(new URL('../src/'+path,import.meta.url),'utf8');
const territories=read('routes/admin/territories/+page.svelte');
const matches=read('lib/admin/AdminGroupMatchDraftPanel.svelte');
const backend=readFileSync(new URL('../../backend/src/LefteriaFc.Api/Football/AdminFootballTerritoryEndpoints.cs',import.meta.url),'utf8');

test('Academy catalogs are listed and edited directly under the selected city',()=>{
  for(const [name,source] of [['territories',territories],['matches',matches]])
    assert.ok(compile(source,{filename:name+'.svelte',generate:'server'}).js.code.length>100,name);
  const catalog=territories.indexOf('data-testid="academy-city-catalog"');
  const location=territories.indexOf('aria-labelledby="location-heading"');
  assert.ok(catalog>0 && location>catalog,'City catalog should precede location workspace');
  for(const id of ['academy-city-opponents','academy-city-fields',
    'academy-add-city-opponent','academy-add-city-field','first-team-catalog'])
    assert.ok(territories.includes('data-testid="'+id+'"'),id);
  assert.match(territories,/selectedScope\.academy_city_id!==cityId/);
  assert.match(territories,/scope_id:selectedScope\.id/);
  assert.match(territories,/mayManageCatalog/);
});

test('Academy city catalogs cannot mix with the independent First Team match selectors',()=>{
  const scopes=[
    {id:'city-a',organization_id:'lefteria_fc_academy',academy_city_id:'a',team_id:null},
    {id:'city-b',organization_id:'lefteria_fc_academy',academy_city_id:'b',team_id:null},
    {id:'first',organization_id:'lefteria_fc',academy_city_id:null,team_id:'team-1'}
  ];
  const locations=[
    {id:'a1',city_id:'a',is_active:true},
    {id:'a2',city_id:'a',is_active:true},
    {id:'b1',city_id:'b',is_active:true}
  ];
  const opponents=[
    {id:'oa',scope_id:'city-a',organization_id:'lefteria_fc_academy',name:'Academy A',is_active:true},
    {id:'ob',scope_id:'city-b',organization_id:'lefteria_fc_academy',name:'Academy B',is_active:true},
    {id:'of',scope_id:'first',organization_id:'lefteria_fc',name:'First Team',is_active:true}
  ];
  const fields=opponents.map(x=>({...x,id:'field-'+x.id}));
  const opts=selection=>scopedFootballMatchOptions(selection,scopes,locations,opponents,fields);
  const a1=opts({organizationId:'lefteria_fc_academy',academyCityId:'a',academyLocationId:'a1'});
  const a2=opts({organizationId:'lefteria_fc_academy',academyCityId:'a',academyLocationId:'a2'});
  assert.deepEqual(a1,a2,'Two Academy locations in one city must share catalogs');
  assert.deepEqual(a1.opponents.map(x=>x.id),['oa']);
  assert.deepEqual(a1.playingFields.map(x=>x.id),['field-oa']);
  assert.deepEqual(opts({organizationId:'lefteria_fc_academy',academyCityId:'a',academyLocationId:'b1'}),{opponents:[],playingFields:[]});
  assert.deepEqual(opts({organizationId:'lefteria_fc',teamScopeId:'first'}).opponents.map(x=>x.id),['of']);
  assert.match(matches,/row.scope_id===scope\?\.id&&row.organization_id===organization/);
  assert.match(matches,/data-testid="admin-match-opponent-picker"/);
  assert.match(matches,/data-testid="admin-match-field-picker"/);
  assert.match(backend,/club\.create_admin_football_resource\(@digest,@owner,@kind,@data\)/);
});
