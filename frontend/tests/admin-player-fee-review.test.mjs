import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');

test('focused billing reads complete exact-child history and never falls back to global results',()=>{
  const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261128201000_AcademyPlayerFeeReview.cs');
  assert.match(sql,/SECURITY DEFINER/);
  assert.match(sql,/identity\.lock_admin_club_authority\(_digest,_owner\)/);
  assert.match(sql,/v_role<>'super_admin'/);
  assert.match(sql,/WHERE organization_id=_owner AND player_id=_player/);
  assert.match(sql,/LIMIT 1001/);
  assert.match(sql,/jsonb_array_length\(v_fees\)>1000/);
  assert.match(sql,/'player_active',v_active/);
  assert.match(sql,/'cancel_eligible',f\.fee_month>v_month AND f\.status='issued'/);
  assert.match(sql,/f\.payment_request_id IS NULL AND f\.paid_at IS NULL/);
  assert.match(sql,/NOT EXISTS\(SELECT 1 FROM finance\.training_fee_cash_receipts/);
  assert.match(sql,/NOT EXISTS\(SELECT 1 FROM finance\.training_fee_sms_outbox/);
  assert.doesNotMatch(sql,/UPDATE club\.players|UPDATE finance\.training_fee_issues|DELETE FROM/);
  const api=read('backend/src/LefteriaFc.Api/Payabl/AcademyFeeEndpoints.cs');
  assert.match(api,/context\.Request\.Query\["player_id"\]/);
  assert.match(api,/filter\.Count>1/);
  assert.match(api,/finance\.read_admin_player_training_fees\(@digest,@owner,@player\)/);
  assert.match(api,/CacheControl="private, no-store"/);
  const grants=read('ops/postgres/payabl-finance-grants.sql');
  assert.match(grants,/REVOKE ALL ON FUNCTION finance\.read_admin_player_training_fees\(text,text,text\) FROM PUBLIC, :"app_role"/);
});

test('departure review uses verified financial data, exact child and unchanged existing cancellation API',()=>{
  const panel=read('frontend/src/lib/admin/AdminTrainingFees.svelte');
  assert.ok(compile(panel,{filename:'AdminTrainingFees.svelte',generate:'server'}).js.code.length>100);
  assert.match(panel,/player_id:target/);
  assert.match(panel,/scoped\.player_id!==target/);
  assert.match(panel,/raw\.fees\.every\(f=>f\.player_id===target&&typeof f\.cancel_eligible==='boolean'\)/);
  assert.match(panel,/if\(bad\)\{status='error';/);
  assert.match(panel,/raw\.fees\.length>\(target\?1000:100\)/);
  assert.match(panel,/data-testid="admin-player-departure-review"/);
  assert.match(panel,/fee\.cancel_eligible===true/);
  assert.match(panel,/readonly=\{!!focus\}/);
  assert.match(panel,/verifiedPlayer\?\.id!==focus\|\|!verifiedPlayer\.active\|\|playerId!==focus/);
  assert.match(panel,/client_request_id:cancellationRequestIds\[fee\.id\]/);
  const roster=read('frontend/src/lib/admin/AdminRosterPlayersPanel.svelte');
  assert.match(roster,/allowFinance&&owner==='lefteria_fc_academy'/);
  assert.match(roster,/data-testid="admin-player-departure-review-link"/);
  assert.match(roster,/player_id:player\.id/);
});
