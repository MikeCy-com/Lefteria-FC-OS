import test from 'node:test';
import assert from 'node:assert/strict';
import {searchPublishedHubEducation} from '../src/lib/mobile/published-education.ts';

const items=[
  {id:'1',organization_id:'lefteria_fc_academy',audience:'parent',title:'Στήριξη στο γήπεδο',body:'Ακούμε τα παιδιά.',published_at:'2026-11-01T10:00:00Z',age_min:null,age_max:null},
  {id:'2',organization_id:'lefteria_fc_academy',audience:'parent',title:'Ασφαλής επικοινωνία',body:'Συζητάμε χωρίς πίεση.',published_at:'2026-11-02T10:00:00Z',age_min:null,age_max:null}
];

test('published HUB search is accent insensitive and matches all query tokens',()=>{
  assert.deepEqual(searchPublishedHubEducation(items,'στηριξη').map(x=>x.id),['1']);
  assert.deepEqual(searchPublishedHubEducation(items,'ασφαλης επικοινωνια').map(x=>x.id),['2']);
  assert.deepEqual(searchPublishedHubEducation(items,'χωρις πιεση').map(x=>x.id),['2']);
  assert.deepEqual(searchPublishedHubEducation(items,'δεν υπαρχει'),[]);
});

test('search is read-only, bounded and never creates or mutates an article',()=>{
  const previous=JSON.stringify(items);
  assert.equal(searchPublishedHubEducation(items,' ').length,2);
  assert.deepEqual(searchPublishedHubEducation(items,'x'.repeat(10000)),[]);
  assert.equal(JSON.stringify(items),previous);
});
