import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {validMatchQueueRow,matchQueueConflict} from '../src/lib/admin/group-match-queue.ts';
const row={opponent_id:'12345678-1234-1234-1234-123456789abc',playing_field_id:'12345678-1234-1234-1234-123456789def',match_date:'2027-01-03',kickoff_local:'10:30',competition:'League',home_game:true};
test('queue dates, times and catalog identities are validated',()=>{
  assert.equal(validMatchQueueRow(row),true);
  for(const patch of [{match_date:'2027-02-30'},{kickoff_local:'24:00'},{opponent_id:'invalid'},{competition:'a'.repeat(161)}])
    assert.equal(validMatchQueueRow({...row,...patch}),false);
  assert.equal(matchQueueConflict(row,[{...row,kickoff_local:'10:30:00'}]),true);
  assert.equal(matchQueueConflict(row,[{...row,kickoff_local:'11:30'}]),false);
  assert.equal(matchQueueConflict(row,[{...row,match_date:'2027-01-04'}]),false);
});
test('bulk queue is scoped, explicitly confirmed, rechecks catalog before writes and compiles',()=>{
  const source=readFileSync(new URL('../src/lib/admin/AdminGroupMatchDraftPanel.svelte',import.meta.url),'utf8');
  assert.doesNotThrow(()=>compile(source,{filename:'AdminGroupMatchDraftPanel.svelte',generate:'server'}));
  for(const value of ['admin-bulk-match-queue','!confirmed||!queue.length','if(!stillAllowed())return;','freshAssignment','matchQueueConflict(row,existing)','queue=queue.slice(1)','queue.length>=30'])assert.ok(source.includes(value),value);
});
