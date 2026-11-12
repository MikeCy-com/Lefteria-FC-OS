import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {recommendStarterEducation} from '../src/lib/mobile/education-recommendations.ts';

const component=readFileSync(new URL('../src/lib/mobile/components/MobileAcademyEducation.svelte',import.meta.url),'utf8');

test('HUB renders recommended articles only within current role and stage',()=>{
  assert.match(component,/recommendStarterEducation\(/);
  assert.match(component,/searchAcademyEducation\(stage,\s*audience,\s*topic,\s*search\)/);
  assert.match(component,/if \(viewerRole === 'fan'\) return \[\]/);
  assert.match(component,/const order = new Map\(/);
  for(const role of ['parent','player']){
    for(const stage of ['6-9','10-12','13-15','16-18']){
      assert.ok(recommendStarterEducation({role,stage}).every(article=>
        article.stage===stage&&article.audience===role));
    }
  }
  assert.deepEqual(recommendStarterEducation({role:'fan',stage:'10-12'}),[]);
});

test('learning progress is session-only and may be undone without backend writes',()=>{
  assert.match(component,/let completedArticleIds = \$state<string\[\]>\(\[\]\)/);
  assert.match(component,/aria-pressed=\{completedArticleIds.includes\(article.id\)\}/);
  assert.match(component,/completedArticleIds.filter\(item => item !== article.id\)/);
  assert.match(component,/Σημείωσε ως ολοκληρωμένο/);
  assert.doesNotMatch(component,/localStorage|sessionStorage|fetch\(|onMount\(|api\/mobile/);
});
