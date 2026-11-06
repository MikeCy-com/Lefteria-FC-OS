import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { fileURLToPath } from 'node:url';
import { compile } from 'svelte/compiler';
import { render } from 'svelte/server';

registerHooks({
  resolve(specifier, context, nextResolve) {
    if (specifier.endsWith('.css'))
      return {url:'data:text/javascript,export%20default%20%7B%7D',shortCircuit:true};
    return nextResolve(specifier,context);
  },
  load(url, context, nextLoad) {
    if (url.endsWith('.svelte')) {
      const filename=fileURLToPath(url);
      return {format:'module',source:compile(readFileSync(filename,'utf8'),
        {filename,generate:'server'}).js.code,shortCircuit:true};
    }
    return nextLoad(url,context);
  }
});

test('each verified child has an accessible schedule action; no hidden writable family control',async()=>{
  const {default:MobileFamily}=await import('../src/lib/mobile/components/MobileFamily.svelte');
  const children=[
    {id:'child-a',name:'Παιδί Άλφα',organization_id:'lefteria_fc_academy',
      team_type:'Academy',team_id:null,academy_group_ids:['group-a']},
    {id:'child-b',name:'Παιδί Βήτα',organization_id:'lefteria_fc',
      team_type:'First Team',team_id:'team-b',academy_group_ids:[]}
  ];
  const html=render(MobileFamily,{props:{family:{children},status:'ready',
    error:null,busy:false,onLoad:()=>true,onCalendar:()=>{}}}).body;
  assert.equal((html.match(/data-testid="family-child-calendar"/g)??[]).length,2);
  assert.match(html,/Πρόγραμμα για Παιδί Άλφα/);
  assert.match(html,/Πρόγραμμα για Παιδί Βήτα/);
  assert.doesNotMatch(html,/data-testid="family-invite-submit"|data-testid="family-phone-change"/);
});

test('the parent route passes the requested child into the already-authorized calendar',()=>{
  const route=readFileSync(new URL('../src/routes/app/[...path]/+page.svelte',import.meta.url),'utf8');
  const calendar=readFileSync(new URL('../src/lib/mobile/components/MobileCalendar.svelte',import.meta.url),'utf8');
  const family=readFileSync(new URL('../src/lib/mobile/components/MobileFamily.svelte',import.meta.url),'utf8');

  assert.match(route,/query\.set\('player_id', childId\)/);
  assert.match(route,/role === 'parent' && tab === 'calendar'/);
  assert.match(route,/onCalendar=\{\(childId\) => openTab\('calendar', childId\)\}/);
  assert.match(route,/initialPlayerId=\{role === 'parent' \? page\.url\.searchParams\.get\('player_id'\) : null\}/);
  assert.match(calendar,/calendar\?\.selected_player_id \?\? \(role === 'parent' \? initialPlayerId \?\? undefined : undefined\)/);
  assert.match(family,/onCalendar\(child\.id\)/);
  assert.match(calendar,/onLoad\(month, playerId\)/);
  // The existing mobile-calendar tests cover server-validated second-child
  // selection and denial when an unrelated player's ID is requested.
});
