import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const parent=readFileSync(new URL('../src/lib/mobile/components/MobileParentHome.svelte',import.meta.url),'utf8');
const roles=readFileSync(new URL('../src/lib/mobile/components/MobileRoleHome.svelte',import.meta.url),'utf8');

test('parent and member dashboard components compile after readable layout update',()=>{
  for(const [name,source] of [['MobileParentHome',parent],['MobileRoleHome',roles]]){
    assert.ok(compile(source,{filename:name+'.svelte',generate:'server'}).js.code.length>100);
  }
});

test('parent event and participation labels are readable with safe touch targets',()=>{
  for(const rule of [
    /\.parent-home-rsvp-actions legend\{font-size:14px/,
    /\.parent-home-rsvp-actions button\{min-height:44px/,
    /\.parent-event-copy p\{margin:0;color:#c0c6c8;font-size:14px/,
    /\.parent-event-copy h3\{margin:4px 0;font-size:17px/,
    /\.parent-child-card \.parent-select-schedule\{[^}]*min-height:44px/,
    /\.parent-settings:focus-visible/,
    /@media\(max-width:359px\)/
  ])assert.match(parent,rule);
  assert.match(parent,/data-testid="parent-home-rsvp-actions"/);
  assert.match(parent,/aria-pressed=\{ownRsvp===choice\[0\]\}/);
});

test('player and staff cards expose large labels, keyboard focus and a compact phone layout',()=>{
  for(const rule of [
    /\.intro\{color:#ddd5c6;font-size:15px/,
    /\.shortcut strong\{font-size:15px/,
    /\.event-copy strong\{font-size:16px/,
    /\.event-copy small\{font-size:14px/,
    /\.match-rsvp legend\{font-size:14px/,
    /\.match-rsvp button\{min-height:44px/,
    /\.match-open:focus-visible/,
    /@media\(max-width:430px\)/
  ])assert.match(roles,rule);
  assert.match(roles,/aria-pressed=\{playerRsvp===choice\[0\]\}/);
  assert.match(roles,/contextIsCurrent/);
  assert.doesNotMatch(roles,/localStorage|innerHTML|\{@html\}/);
});
