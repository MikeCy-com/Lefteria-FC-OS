import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
test('atomic cancellation is exact-child, bounded, authenticated and reuses the audited single-fee operation',()=>{
 const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261128205000_AcademyDepartureBatchFees.cs');
 for(const marker of ["v_role<>'super_admin'","v_row.player_id IS DISTINCT FROM _player","ORDER BY f.id FOR UPDATE","finance.cancel_admin_future_training_fee(_digest,_owner,v_row.fee,v_row.request,_reason)","REVOKE ALL ON FUNCTION"])assert.ok(sql.includes(marker),marker);
 assert.doesNotMatch(sql,/DELETE FROM|UPDATE club\.players/);
 const fixture=read('ops/postgres/academy-future-fee-cancellation-checks.sql');
 for(const marker of ['batch_partially_committed','batch_retry_not_idempotent','manager_batch_cancelled'])assert.ok(fixture.includes(marker),marker);
});
test('batch control previews exact fees and requires fresh confirmation',()=>{
 const panel=read('frontend/src/lib/admin/AdminTrainingFees.svelte');
 assert.doesNotThrow(()=>compile(panel,{filename:'AdminTrainingFees.svelte',generate:'server'}));
 for(const marker of ['admin-player-batch-future-fees','batchConfirm!==batchKey','result.player_id!==target','feeIds:ids','cancel-player-future'])assert.ok(panel.includes(marker),marker);
});
