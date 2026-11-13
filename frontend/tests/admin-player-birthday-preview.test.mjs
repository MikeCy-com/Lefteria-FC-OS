import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
const read=p=>readFileSync(new URL(p,import.meta.url),'utf8');
const card=read('../src/lib/admin/AdminPlayerBirthdayPreferences.svelte');
const parent=read('../src/lib/admin/AdminPlayerPrivateRecordsPanel.svelte');

test('private branded birthday preview compiles and uses verified manager player name',()=>{
  assert.ok(compile(card,{filename:'AdminPlayerBirthdayPreferences.svelte',generate:'server'}).js.code.length>0);
  assert.match(parent,/AdminPlayerBirthdayPreferences \{owner\} \{playerId\} \{playerName\}/);
  assert.match(card,/data-testid="birthday-private-branded-preview"/);
  assert.match(card,/\{playerName\}/);
  assert.match(card,/ΧΡΟΝΙΑ ΠΟΛΛΑ!/);
  assert.match(card,/LEFTERIA/);
  assert.match(card,/aspect-ratio:16\/9/);
});
test('client-side visual preview never sends, downloads or publishes child cards',()=>{
  assert.match(card,/previewOpen/);
  assert.match(card,/\{#if disabled\}/);
  assert.match(card,/class="disabled-shield"/);
  assert.match(card,/ΙΔΙΩΤΙΚΗ ΠΡΟΕΠΙΣΚΟΠΗΣΗ · ΟΧΙ ΓΙΑ ΔΗΜΟΣΙΕΥΣΗ/);
  assert.match(card,/Οι αλλαγές που δεν έχουν αποθηκευτεί εμφανίζονται μόνο προσωρινά/);
  assert.match(card,/public_enabled===false/);
  assert.match(card,/auto_post_facebook===false/);
  assert.match(card,/auto_post_instagram===false/);
  assert.doesNotMatch(card,/window\.open|URL\.createObjectURL|navigator\.share|document\.createElement|download=|html2canvas|facebook\.com|instagram\.com/);
});
test('card colors remain restricted to validated hexadecimal theme values',()=>{
  assert.match(card,/\^#\[0-9A-Fa-f\]\{6\}\$/);
  assert.match(card,/\-\-birthday-accent/);
  assert.match(card,/color-mix/);
  assert.match(card,/playerId,playerName,actorId/);
});
