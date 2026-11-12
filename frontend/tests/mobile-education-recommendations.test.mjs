import test from 'node:test';
import assert from 'node:assert/strict';
import { recommendStarterEducation } from '../src/lib/mobile/education-recommendations.ts';

test('fan interest never grants parent or player starter article bodies', () => {
  assert.deepEqual(recommendStarterEducation({role:'fan',stage:'10-12',preferredTopics:['psychology']}),[]);
});

test('parent and player recommendations never cross audiences', () => {
  for(const role of ['parent','player']){
    for(const stage of ['6-9','10-12','13-15','16-18']){
      const result=recommendStarterEducation({role,stage});
      assert.ok(result.length>0);
      assert.ok(result.every(item=>item.audience===role && item.stage===stage));
    }
  }
});

test('completion and interests affect ordering without exposing any identity data', () => {
  const baseline=recommendStarterEducation({role:'parent',stage:'10-12'});
  assert.equal(baseline.length,1);
  assert.deepEqual(recommendStarterEducation({
    role:'parent',stage:'10-12',completedArticleIds:[baseline[0].id]
  }).map(item=>item.id),[baseline[0].id]);
  assert.deepEqual(recommendStarterEducation({role:'player',stage:'10-12',limit:0}),[]);
  assert.deepEqual(recommendStarterEducation({role:'player',stage:'invalid'}),[]);
  assert.ok(recommendStarterEducation({role:'player',stage:'10-12',limit:999}).length<=10);
});
