import test from 'node:test';
import assert from 'node:assert/strict';
import { publicNavigation, categoryLinks, academyGroupLink, publicSearch } from '../src/lib/public/navigation.ts';

test('public navigation preserves all nine original sections and the real submenu destinations', () => {
  assert.deepEqual(publicNavigation.map(item => item.id), ['home','club','team','academy','matches','news','events','shop','contact']);
  assert.deepEqual(publicNavigation.filter(item => item.items).map(item => [item.id,item.items.length]),
    [['club',6],['team',8],['academy',5],['matches',5]]);
  const paths = publicNavigation.flatMap(item => item.items?.map(row => row[2]) ?? [item.path]);
  for (const expected of ['/join','/seasons','/partners','/sponsors/first-team','/sponsors/academy','/team?tab=roster','/team?tab=gallery','/team?tab=venues','/trials','/academy/philosophy','/academy/groups','/academy/registration','/vote','/events','/shop']) assert.ok(paths.includes(expected), expected);
  assert.ok(paths.every(path => path.startsWith('/') && !path.startsWith('//') && !path.startsWith('/admin') && !path.startsWith('/app')));
});

test('Academy category appends generated DTO group links with original Greek slugs and opaque IDs', () => {
  const group = {id:'Case/opaque--id',name:'Ακαδημία Κ-12'};
  assert.equal(academyGroupLink(group),'/academy/ακαδημια-κ-12--Case%2Fopaque--id');
  const academy = publicNavigation.find(item => item.id === 'academy');
  const links = categoryLinks(academy,[group]);
  assert.equal(links.length,6); assert.deepEqual(links.at(-1),[group.id,group.name,academyGroupLink(group)]);
  assert.equal(categoryLinks(publicNavigation.find(item => item.id === 'club'),[group]).length,6);
});

test('real public search normalizes Greek accents/case, keeps news and groups, deduplicates routes and bounds results', () => {
  const group = {id:'group',name:'Δοκιμή'};
  const news = Array.from({length:30},(_,i) => ({id:'news-'+i,title:'Νέα Δοκιμή '+i,organization_id:'lefteria_fc'}));
  assert.equal(publicSearch('ΑΠΟΤΕΛΕΣΜΑΤΑ',[],[]).filter(row => row.path === '/team?tab=results').length,1);
  const results = publicSearch('δοκιμη',[group],news);
  assert.equal(results.length,16); assert.equal(results[0].path,academyGroupLink(group));
  assert.ok(results.some(row => row.path.startsWith('/news/νεα-δοκιμη')));
  assert.deepEqual(publicSearch('δεν υπάρχει τέτοια σελίδα',[],[]),[]);
});
