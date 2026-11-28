import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
test('departure preflight exposes only exact-player lifecycle flags and retained link counts',()=>{
 const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261128202000_AcademyDeparturePreflight.cs');
 for(const marker of ["v_role<>'super_admin'","identity.lock_admin_club_authority(_digest,_owner)","g.player_id=p.id AND g.organization_id=_owner","b.source_id=p.id AND b.kind='guardian'","g.status='granted'","'lifecycle_review',v_lifecycle"])assert.ok(sql.includes(marker),marker);
 assert.doesNotMatch(sql,/UPDATE club\.players|DELETE FROM|INSERT INTO identity\./);
});
test('preflight shows real state with no archive or reactivation action',()=>{
 const panel=read('frontend/src/lib/admin/AdminTrainingFees.svelte');
 assert.doesNotThrow(()=>compile(panel,{filename:'AdminTrainingFees.svelte',generate:'server'}));
 for(const marker of ['admin-player-lifecycle-preflight','validLifecycle(lifecycle,scoped.player_active)','verifiedPlayer.lifecycle.is_archived','verifiedPlayer.lifecycle.unrevoked_guardian_permissions','player_id:focus,section:'])assert.ok(panel.includes(marker),marker);
});
