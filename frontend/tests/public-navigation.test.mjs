import test from 'node:test';
import assert from 'node:assert/strict';
import { publicNavigation, categoryLinks, academyGroupLink, publicSearch, navigationActive, navigationOwner, navigationLinkCurrent } from '../src/lib/public/navigation.ts';

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

test('rail and public mobile menu have one URL-aware owner across hubs, detail pages, scopes and legacy aliases', () => {
  const cases = [
    ['/', 'home'], ['/team', 'team'], ['/team?tab=overview', 'team'], ['/team?tab=roster', 'team'],
    ['/team?tab=staff', 'team'], ['/team?tab=statistics', 'team'], ['/team?tab=venues', 'team'], ['/team?tab=gallery', 'team'],
    ['/team?tab=schedule', 'matches'], ['/team?tab=results', 'matches'], ['/team?tab=overview#standings', 'matches'],
    ['/team?tab=roster#standings', 'team'], ['/team?tab=unrecognized', 'team'], ['/player/Case-ID', 'team'], ['/staff/coach', 'team'], ['/match/match-id', 'matches'],
    ['/academy/u12--group', 'academy'], ['/academy/groups', 'academy'], ['/academy/philosophy', 'academy'],
    ['/partners', 'club'], ['/partners?scope=all', 'club'], ['/partners?scope=first_team', 'team'], ['/partners?scope=academy', 'academy'],
    ['/partners/slug?scope=academy', 'academy'], ['/sponsors/academy?scope=first_team', 'academy'], ['/sponsors/first-team?scope=academy', 'team'],
    ['/sponsors/name--id?scope=academy', 'academy'], ['/seasons/season', 'club'], ['/news/article', 'news'], ['/events/event', 'events'],
    ['/fixtures', 'matches'], ['/calendar', 'matches'], ['/venues', 'team'], ['/staff', 'team'], ['/newshop', null]
  ];
  for (const [location, owner] of cases) {
    const url = new URL(location, 'https://preview.test');
    assert.equal(navigationOwner(url), owner, location);
    assert.equal(navigationOwner(Object.freeze({ href: url.href })), owner, location + ' readonly URL-like input');
    assert.deepEqual(publicNavigation.filter(item => navigationActive(item, url)).map(item => item.id), owner ? [owner] : [], location);
  }
});

test('submenu current markers distinguish exact tabs, scopes, standings anchors and raw/canonical Academy group IDs', () => {
  assert.equal(navigationLinkCurrent('/team', '/team?tab=overview'), 'page');
  assert.equal(navigationLinkCurrent('/team', '/team?tab=unrecognized'), 'page');
  assert.equal(navigationLinkCurrent('/team', '/team?tab=roster'), undefined);
  assert.equal(navigationLinkCurrent('/team?tab=roster', '/team?tab=roster#player'), 'page');
  assert.equal(navigationLinkCurrent('/team?tab=roster', Object.freeze({ href: 'https://preview.test/team?tab=roster#player' })), 'page');
  assert.equal(navigationLinkCurrent('/team?tab=results', '/calendar'), undefined);
  assert.equal(navigationLinkCurrent('/team?tab=schedule', '/calendar'), 'page');
  assert.equal(navigationLinkCurrent('/team', '/team?tab=overview#standings'), undefined);
  assert.equal(navigationLinkCurrent('/team?tab=overview#standings', '/team?tab=overview'), undefined);
  assert.equal(navigationLinkCurrent('/team?tab=overview#standings', '/team?tab=overview#standings'), 'location');
  assert.equal(navigationLinkCurrent('/partners', '/partners?scope=all'), 'page');
  assert.equal(navigationLinkCurrent('/partners', '/partners?scope=academy'), undefined);
  assert.equal(navigationLinkCurrent('/partners?scope=academy', '/partners/technical-partner?scope=academy'), 'location');
  assert.equal(navigationLinkCurrent('/partners?scope=first_team', '/partners/technical-partner?scope=academy'), undefined);
  assert.equal(navigationLinkCurrent('/sponsors/academy', '/sponsors/academy?scope=first_team'), 'page');
  assert.equal(navigationLinkCurrent('/sponsors/academy', '/sponsors/gold--id?scope=academy'), 'location');
  assert.equal(navigationLinkCurrent('/news', '/news/story--id'), 'location');
  const groups = [{ id: 'Case/opaque--id', name: 'Ακαδημία Κ-12' }];
  assert.equal(navigationLinkCurrent(academyGroupLink(groups[0]), '/academy/Case%2Fopaque--id', groups), 'page');
  assert.equal(navigationLinkCurrent(academyGroupLink(groups[0]), '/academy/case%2Fopaque--id', groups), undefined);
  assert.equal(navigationLinkCurrent('/academy/groups', '/academy/Case%2Fopaque--id', groups), undefined);
  assert.equal(navigationLinkCurrent('//external.example/team', '/team'), undefined);
});
