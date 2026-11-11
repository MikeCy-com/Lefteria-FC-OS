import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const grant=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110175000_GuardianVerifiedPhoneScope.cs');
const roster=read('backend/src/LefteriaFc.Infrastructure/Roster/PostgresRosterQueries.cs');
const family=read('backend/src/LefteriaFc.Infrastructure/Family/PostgresParentFamilyQueries.cs');
const identity=read('backend/src/LefteriaFc.Infrastructure/Identity/CanonicalMobileProfiles.cs');
const calendar=read('backend/src/LefteriaFc.Infrastructure/ClubCalendar/OperationalScopeSql.cs');
const chat=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110176000_DelegatedGuardianChatDirectory.cs');
const ui=read('frontend/src/lib/mobile/components/MobileFamily.svelte');
const controller=read('frontend/src/lib/mobile/controller.ts');

test('Parent is granted child scope only by matching a verified current phone',()=>{
  assert.match(grant,/recipient.id=_identity AND recipient.normalized_phone=_phone/);
  assert.match(grant,/recipient.role='parent' AND _role='parent'/);
  assert.match(grant,/recipient.is_active AND _expires>statement_timestamp\(\)/);
  assert.match(grant,/player.is_active AND NOT player.is_archived/);
  assert.match(grant,/NOT player.is_transferred/);
});
test('all parent child projections include live grants, not client-side child lists as authority',()=>{
  for (const source of [roster,family,identity,calendar,chat])
    assert.match(source,/identity.current_phone_granted_children\(/);
  assert.match(roster,/i.role='parent' AND EXISTS/);
  assert.match(family,/SELECT DISTINCT ON \(id\)/);
  assert.match(calendar,/p.id=permission.player_id/);
  assert.match(chat,/i.role='parent'/);
});
test('revoked or unrelated child has no grant lineage and cannot cross roles',()=>{
  assert.match(grant,/grant_row.status='granted'/);
  assert.match(grant,/grant_row.source_permission_id=valid.permission_id/);
  assert.match(grant,/issuer.normalized_phone=valid.target_phone/);
  assert.match(grant,/valid.depth<20 AND NOT grant_row.id=ANY\(valid.path\)/);
  assert.match(grant,/original_link.kind='guardian'/);
  assert.match(grant,/binding.kind='guardian'/);
  assert.doesNotMatch(grant,/recipient.role='player' OR/);
});
test('all-children network retries reuse identical request IDs unless form values change',()=>{
  assert.match(ui,/let bulkRequest=\$state/);
  assert.match(ui,/bulkRequest\?\.key!==key/);
  assert.match(ui,/onCreateManyGuardian\(bulkRequest.items,permissionPhone\)/);
  assert.match(ui,/function clearPermissionDraft/);
  assert.match(ui,/requestId='';bulkRequest=null;/);
  assert.match(controller,/raw.items.length!==children.length/);
  assert.match(controller,/item.id!==children\[i\].request_id/);
});