import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
test('restricted lifecycle retains history and refuses identity-linked and unsettled records',()=>{
 const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261128206000_AcademyRestrictedPlayerLifecycle.cs');
 for(const marker of ["v_role<>'super_admin'","FOR UPDATE","identity.canonical_contact_bindings","identity.family_delegated_player_grants","finance.general_charge_balances","NOT v_linked AND NOT v_open","v_revision<>_expected","is_active=(_action='reactivate')","academy_player_lifecycle_events_immutable"])assert.ok(sql.includes(marker),marker);
 assert.doesNotMatch(sql,/DELETE FROM|UPDATE identity\.|UPDATE finance\./);
 const fixture=read('ops/postgres/academy-private-player-profiles-checks.sql');
 for(const marker of ['manager_archived_child','archive_replay_failed','stale_lifecycle_revision_accepted','lifecycle_lost_player_history'])assert.ok(fixture.includes(marker),marker);
});
test('lifecycle panel uses an explicit scoped confirmation and verified response',()=>{
 const panel=read('frontend/src/lib/admin/AdminPlayerLifecyclePanel.svelte');
 assert.doesNotThrow(()=>compile(panel,{filename:'AdminPlayerLifecyclePanel.svelte',generate:'server'}));
 for(const marker of ['admin-player-lifecycle-control','confirmation!==confirmKey','scope!==current','raw.revision!==expected+1','identity_review_required'])assert.ok(panel.includes(marker),marker);
});
