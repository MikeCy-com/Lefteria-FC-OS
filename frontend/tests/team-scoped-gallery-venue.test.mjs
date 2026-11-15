import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {selectTeamGallery,selectTeamVenues} from '../src/lib/public/team-scope.ts';

test('Club galleries cannot show another team, Academy, unscoped or unrelated player images',()=>{
  const base={organization_id:'lefteria_fc',category:'Football',image_url:'https://example.test/club.jpg',title:'Public club photo',team_id:null,player_id:null,academy_group_id:null};
  const gallery=[
    {...base,id:'first',title:'First only',team_id:'first-id'},
    {...base,id:'women',title:'Women only',team_id:'women-id'},
    {...base,id:'first-labelled',title:'Verified first-team import',category:'First Team'},
    {...base,id:'first-labelled-Greek',title:'Verified Greek import',category:'Πρώτη ομάδα'},
    {...base,id:'unassigned',title:'Other Club photos'},
    {...base,id:'women-player',title:'Women player photo',player_id:'women-player'},
    {...base,id:'first-player',title:'First Team player photo',player_id:'first-player'},
    {...base,id:'conflicted',title:'Conflicted record',team_id:'first-id',player_id:'women-player'},
    {...base,id:'academy-organization',organization_id:'lefteria_fc_academy',team_id:'women-id'},
    {...base,id:'academy-assignment',academy_group_id:'u12',team_id:'women-id'}
  ];
  const players=[
    {id:'first-player',team_id:'first-id',organization_id:'lefteria_fc'},
    {id:'women-player',team_id:'women-id',organization_id:'lefteria_fc'},
    {id:'cross-org-player',team_id:'women-id',organization_id:'lefteria_fc_academy'}
  ];
  assert.deepEqual(selectTeamGallery(gallery,players,'first-id',true).map(x=>x.id),
    ['first','first-labelled','first-labelled-Greek','first-player','conflicted']);
  assert.deepEqual(selectTeamGallery(gallery,players,'women-id',false).map(x=>x.id),
    ['women','women-player']);
  assert.deepEqual(selectTeamGallery(gallery,players,'unpublished',false),[]);
  assert.deepEqual(selectTeamGallery(gallery,players,null,true),[]);
});

test('Venue cards require verified fixture venue or explicit First Team home-ground designation',()=>{
  const places=[
    {id:'home',organization_id:'lefteria_fc',name:'Club Home',is_home_ground:true},
    {id:'women',organization_id:'lefteria_fc',name:'Γήπεδο Λεμεσού',is_home_ground:false},
    {id:'opponent',organization_id:'lefteria_fc',name:'Opponent Ground',is_home_ground:false},
    {id:'academy',organization_id:'lefteria_fc_academy',name:'Γήπεδο Λεμεσού',is_home_ground:false}
  ];
  const games=[
    {id:'women-fixture',organization_id:'lefteria_fc',team_id:'women-id',venue:'Γηπεδο Λεμεσου'},
    {id:'first-fixture',organization_id:'lefteria_fc',team_id:'first-id',venue:'Opponent Ground'},
    {id:'academy-fixture',organization_id:'lefteria_fc_academy',team_id:'women-id',venue:'Secret Academy ground'},
    {id:'unscoped-fixture',organization_id:'lefteria_fc',team_id:null,venue:'Club Home'}
  ];
  assert.deepEqual(selectTeamVenues(places,games,'women-id',false).map(x=>x.id),['women']);
  assert.deepEqual(selectTeamVenues(places,games,'first-id',true).map(x=>x.id),['home','opponent']);
  assert.deepEqual(selectTeamVenues(places,games,null,true),[]);
  assert.deepEqual(selectTeamVenues(places,[], 'women-id', false),[]);
});

test('Team route renders scoped gallery and venue collections, retaining existing tabs and maps',()=>{
  const source=readFileSync(new URL('../src/routes/team/+page.svelte',import.meta.url),'utf8');
  assert.match(source,/selectTeamGallery\(data\.sections\?\.gallery \?\? \[\], players,/);
  assert.match(source,/selectTeamVenues\(data\.sections\?\.venues \?\? \[\], fixtures,/);
  assert.match(source,/\{#each gallery as image/);
  assert.match(source,/\{#each venues as venue/);
  assert.match(source,/mapHref\(venue\.map_url\)/);
  for(const tab of ['roster','staff','schedule','results','gallery','venues','statistics'])
    assert.match(source,new RegExp("'"+tab+"'"));
});
