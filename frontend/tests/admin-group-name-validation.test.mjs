import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const page=readFileSync(new URL('../src/routes/admin/groups/+page.svelte',import.meta.url),'utf8');
const forbiddenControl=/[\x00-\x1f\x7f]/;
const validGroupName=value=>value.length>=1&&value.length<=120&&
  value===value.trim()&&!forbiddenControl.test(value);

test('First Team and Academy creation accepts original-style Latin/Greek group names',()=>{
  for(const name of ['First Team', 'U17', 'U15', 'Κ-12', 'Ακαδημία 2017', 'F.C. ΛΕΥΤΕΡΙΑ']) {
    assert.equal(validGroupName(name),true,name);
  }
});
test('group names reject control characters, wrapping whitespace, and oversized input',()=>{
  for(const name of ['', ' Team', 'Team ', 'Team\nother', 'Team\tother',
    'Team\u0000other','Team\u007fother','X'.repeat(121)]){
    assert.equal(validGroupName(name),false,JSON.stringify(name));
  }
});
test('UI and server group creation both protect name length and actual control characters',()=>{
  assert.match(page,/name.length > 120/);
  assert.match(page,/name !== name.trim\(\)/);
  assert.ok(page.includes(String.raw`/[\x00-\x1f\x7f]/.test(name)`),
    'Expected a single-escaped control-character class in the Svelte component');
  assert.equal(page.includes(String.raw`/[\\x00-\\x1f\\x7f]/.test(name)`),false,
    'The double-escaped pattern rejects valid Latin letters such as f and x');
  assert.match(page,/\/api\/admin\/football\/groups\/with-profile\?/);
  assert.match(page,/organization_id:selected/);
  assert.match(page,/value.kind !==/);
});

test('group directory rejects duplicate IDs and blank group names before keyed rendering',()=>{
  assert.match(page,/const uniqueIds = new Set<string>\(\)/);
  assert.match(page,/uniqueIds\.has\(row\.id\)/);
  assert.match(page,/uniqueIds\.add\(row\.id\)/);
  assert.match(page,/row\.name\.length > 0/);
  assert.match(page,/verifiedDirectory\(raw,owner\)/);
  assert.match(page,/unavailable = true/);
});