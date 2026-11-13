import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const source=readFileSync(new URL('../src/routes/admin/hub/+page.svelte',import.meta.url),'utf8');

test('a visual inspection marker belongs to an exact owner, content, revision and version',()=>{
  assert.match(source,/\[source\.organization_id,source\.id,source\.revision_id,source\.version,mediaId\]\.join\(':'\)/);
  assert.match(source,/source\.visuals\.some\(v=>!inspected\.includes\(inspectionKey\(source,v\.id\)\)\)/);
  assert.match(source,/\{@const proof=inspectionKey\(detail,media\.id\)\}/);
  assert.match(source,/showPreview=proof;inspected=inspected\.filter\(id=>id!==proof\)/);
  assert.match(source,/showPreview===proof&&inspectionKey\(detail,media\.id\)===proof/);
});
test('image errors and revision changes cannot claim visual approval',()=>{
  assert.match(source,/onerror=\{\(\)=>\{inspected=inspected\.filter\(id=>id!==proof\);\}\}/);
  assert.match(source,/organization===detail\.organization_id/);
  assert.match(source,/revision_id='\+detail\.revision_id\+'&version='\+detail\.version/);
  assert.match(source,/detail=null;inspected=\[\];showPreview=null/);
  assert.match(source,/if\(action==='visual-approve'/);
  assert.doesNotMatch(source,/inspected\.includes\(v\.id\)/);
});
