import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import {
  academyEducationFor, searchAcademyEducation, EDUCATION_STAGES
} from '../src/lib/mobile/academy-education.ts';

test('Greek accent-insensitive Hub search respects age and audience before text', () => {
  const all = academyEducationFor('10-12','all','all');
  assert.equal(all.length, 2);
  const confidence = searchAcademyEducation('10-12','player','psychology','ΑΥΤΟΠΕΠΟΙΘΗΣΗ');
  assert.equal(confidence.length, 1);
  assert.equal(confidence[0].audience, 'player');
  assert.deepEqual(searchAcademyEducation('10-12','parent','psychology','αυτοπεποίθηση'), []);
  assert.deepEqual(searchAcademyEducation('6-9','player','psychology','αυτοπεποίθηση'), []);
});

test('Hub search is bounded and does not mutate first-party content', () => {
  for (const stage of EDUCATION_STAGES) {
    assert.deepEqual(searchAcademyEducation(stage.id,'all','all',''),academyEducationFor(stage.id));
    assert.deepEqual(searchAcademyEducation(stage.id,'all','all','   '),academyEducationFor(stage.id));
  }
  const results=searchAcademyEducation('10-12','all','all','ΑΥΤΟΠΕΠΟΙΘΗΣΗ'.repeat(50));
  assert.ok(Array.isArray(results));
  assert.deepEqual(searchAcademyEducation('10-12','all','all','word-never-present'),[]);
});

test('Hub search offers accessible clear action and does not fetch private data', () => {
  const component=readFileSync(new URL('../src/lib/mobile/components/MobileAcademyEducation.svelte',import.meta.url),'utf8');
  assert.match(component,/searchAcademyEducation\(stage,audience,topic,search\)/);
  assert.match(component,/type="search" maxlength="120"/);
  assert.match(component,/data-testid="hub-content-search"/);
  assert.match(component,/aria-label="Καθαρισμός αναζήτησης"/);
  assert.doesNotMatch(component,/fetch\(|\/api\/mobile\/family|localStorage|subscribe\(/);
});
