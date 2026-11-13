import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { registerHooks } from 'node:module';
import { compile } from 'svelte/compiler';
import { render } from 'svelte/server';
import { matchingParentHomeCalendar, parentHomeCalendarMonth, parentHomeSchedule } from '../src/lib/mobile/parent-home.ts';

const today = '2026-11-07';
const identityId = '11111111-1111-4111-8111-111111111111';
const family = { children: [
  { id: 'academy-child', name: 'Παιδί Άλφα', organization_id: 'lefteria_fc_academy',
    team_type: 'Academy', team_id: null, academy_group_ids: ['u12'] },
  { id: 'club-child', name: 'Παιδί Βήτα', organization_id: 'lefteria_fc',
    team_type: 'First Team', team_id: 'club-team', academy_group_ids: [] }
] };
const item = (patch = {}) => ({
  id: 'training-1', kind: 'training', event_type: 'training',
  organization_id: 'lefteria_fc_academy', date: '2026-11-10',
  end_date: '2026-11-10', title: 'Προπόνηση Ακαδημίας',
  start_time: '17:00', location: 'Γήπεδο Ακαδημίας', is_cancelled: false,
  ...patch
});
const calendar = (patch = {}) => ({
  identity_id: identityId, role: 'parent', selected_player_id: 'academy-child',
  month: '2026-11', time_zone: 'Asia/Nicosia',
  organization_id: 'lefteria_fc_academy', players: [], next_offset: null,
  items: [
    item(),
    item({ id: 'past', date: '2026-11-03', end_date: '2026-11-03', title: 'Παλιό γεγονός' }),
    item({ id: 'cancelled', date: '2026-11-11', end_date: '2026-11-11',
      title: 'Ακυρωμένη προπόνηση', is_cancelled: true }),
    item({ id: 'foreign', organization_id: 'lefteria_fc', date: '2026-11-12',
      end_date: '2026-11-12', title: 'Άλλου συλλόγου' })
  ],
  ...patch
});

registerHooks({
  resolve(specifier, context, nextResolve) {
    if (specifier.endsWith('.css'))
      return {url:'data:text/javascript,export%20default%20%7B%7D',shortCircuit:true};
    return nextResolve(specifier, context);
  },
  load(url, context, nextLoad) {
    if (url.endsWith('.svelte')) {
      const filename = fileURLToPath(url);
      return {format:'module',source:compile(readFileSync(filename,'utf8'),
        {filename,generate:'server'}).js.code,shortCircuit:true};
    }
    return nextLoad(url, context);
  }
});

test('home schedule includes only this currently linked child, parent role and matching month', () => {
  assert.equal(parentHomeCalendarMonth(today), '2026-11');
  assert.equal(matchingParentHomeCalendar(family,calendar(),identityId,today),true);
  assert.deepEqual(parentHomeSchedule(family,calendar(),identityId,today).map(row => row.id),['training-1']);

  const invalid = [
    calendar({role:'coach'}),
    calendar({identity_id:'another-account'}),
    calendar({selected_player_id:'club-child'}),
    calendar({organization_id:'lefteria_fc'}),
    calendar({month:'2026-10'}),
    null
  ];
  for (const value of invalid) {
    assert.equal(matchingParentHomeCalendar(family,value,identityId,today),false);
    assert.deepEqual(parentHomeSchedule(family,value,identityId,today),[]);
  }
  assert.deepEqual(parentHomeSchedule({children:[]},calendar(),identityId,today),[]);
  assert.deepEqual(parentHomeSchedule(null,calendar(),identityId,today),[]);
});

test('home schedule orders verified calendar items and shows no unverified future data', () => {
  const rows = calendar({items:[
    item({id:'last',date:'2026-11-28',end_date:'2026-11-28'}),
    item({id:'first',date:'2026-11-07',end_date:'2026-11-07'}),
    item({id:'second',date:'2026-11-09',end_date:'2026-11-09'}),
    item({id:'third',date:'2026-11-10',end_date:'2026-11-10'}),
    item({id:'fourth',date:'2026-11-17',end_date:'2026-11-17'}),
    item({id:'next-month',date:'2026-12-02',end_date:'2026-12-02'})
  ]});
  assert.deepEqual(parentHomeSchedule(family, rows,identityId,today).map(x=>x.id),
    ['first','second','third','fourth']);
});

test('Svelte Parent Home renders only verified children and their current scoped events', async () => {
  const { default: Home } = await import('../src/lib/mobile/components/MobileParentHome.svelte');
  const common = {
    identityId, parentName: 'Γονέας Δοκιμής', today,
    family, familyStatus:'ready', familyError:null,
    calendar:calendar(), calendarStatus:'ready', calendarError:null, busy:false,
    onLoadFamily:()=>true, onLoadCalendar:()=>true,
    onChildren:()=>{}, onCalendar:()=>{}
  };
  const html = render(Home,{props:common}).body;
  for(const text of ['parent-dashboard-home','parent-children-overview',
    'parent-upcoming-schedule','parent-home-shortcuts','parent-home-child-calendar',
    'Παιδί Άλφα','Παιδί Βήτα','Προπόνηση Ακαδημίας','Γονέας Δοκιμής',
    'Μαζί για το μέλλον τους','Under Developments']) {
    assert.ok(html.includes(text),text);
  }
  assert.equal((html.match(/data-testid="parent-home-child"/g)??[]).length,2);
  assert.equal((html.match(/data-testid="parent-home-event"/g)??[]).length,1);
  assert.doesNotMatch(html,/Παλιό γεγονός|Άλλου συλλόγου/);
  assert.match(html,/parent-home-cancellations/);
  assert.match(html,/Ακυρωμένη προπόνηση/);
  assert.doesNotMatch(html,/medical_notes|date_of_birth|private_contact|guardian_phone|Bearer/);
  assert.match(html,/Πρόγραμμα για Παιδί Άλφα/);
  assert.match(html,/Πρόγραμμα για Παιδί Βήτα/);

  const sibling = render(Home,{props:{...common,calendar:calendar({selected_player_id:'club-child'})}}).body;
  assert.doesNotMatch(sibling,/Προπόνηση Ακαδημίας/);
  assert.match(sibling,/parent-home-calendar-loading/);

  const revoked = render(Home,{props:{...common,family:null,familyStatus:'error',
    familyError:'Η πρόσβαση έχει αφαιρεθεί'}}).body;
  assert.match(revoked,/parent-home-family-error/);
  assert.doesNotMatch(revoked,/Παιδί Άλφα|Προπόνηση Ακαδημίας/);

  const empty = render(Home,{props:{...common,family:{children:[]}}}).body;
  assert.match(empty,/parent-home-no-children/);
  assert.match(empty,/parent-home-calendar-no-child/);
  assert.doesNotMatch(empty,/Παιδί Άλφα/);

  const unavailable = render(Home,{props:{...common,calendar:null,
    calendarStatus:'error',calendarError:'Το ημερολόγιο δεν είναι διαθέσιμο'}}).body;
  assert.match(unavailable,/parent-home-calendar-error/);
  assert.doesNotMatch(unavailable,/Δεν υπάρχουν άλλες δημοσιευμένες/);
});

test('parent home is role-gated in app router and reuses guarded family/calendar controller', () => {
  const route = readFileSync(new URL('../src/routes/app/[...path]/+page.svelte',import.meta.url),'utf8');
  assert.match(route,/activeTab === 'home' && role === 'parent' && !composing/);
  assert.match(route,/onLoadFamily=\{\(\) => controller\.loadFamily\(\)\}/);
  assert.match(route,/onLoadCalendar=\{\(month, childId\) => controller\.loadCalendar\(month, childId\)\}/);
  assert.match(route,/onCalendar=\{\(childId\) => openTab\('calendar', childId\)\}/);
  assert.match(route,/onChildren=\{\(\) => openTab\('children'\)\}/);
  assert.match(route,/<MobileParentHome identityId=\{user\.id\} parentName=\{user\.name\}/);
  assert.match(route,/!user\.current_role_available/);
});
