import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
const source=readFileSync(new URL('../src/lib/admin/AdminPlayerPrivateProfilePanel.svelte',import.meta.url),'utf8');
test('guardian contact review is tied to exact actor, player, revision and edited values',()=>{
  assert.doesNotThrow(()=>compile(source,{filename:'AdminPlayerPrivateProfilePanel.svelte',generate:'server'}));
  for(const marker of ['admin-guardian-contact-review','contactChanges.length','JSON.stringify([playerId,actorId,profile?.current_contact?.revision??0,contactDraft])','contactConfirmation===contactReviewKey','!contactConfirmed||savingContact','contact:submitted','result.revision!==expected+1','playerId!==id||actorId!==actor||!allowWrite'])assert.ok(source.includes(marker),marker);
});
test('contact changes remain separate from immutable identity proof',()=>{
  assert.ok(source.includes('showGuardianReview=false;error='));
  assert.ok(source.includes('guardianPhone={profile.registration.guardian_phone}'));
  assert.ok(source.includes('disabled={editingContact||savingContact}'));
  assert.ok(source.includes("const submitted={...contactDraft}"));
});
