import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read=p=>readFileSync(new URL(p,import.meta.url),'utf8');
const ci=read('../../.github/workflows/rewrite-build.yml');
const lifecycle=read('../../ops/postgres/player-birthday-lifecycle-checks.sql');
const grants=read('../../ops/postgres/player-birthday-admin-grants.sql');
const allow=read('../../ops/postgres/web-admin-auth-permissions-check.sql');

test('CI applies birthday-only grants before admin authority and rollback probes',()=>{
  const grant=ci.indexOf('/player-birthday-admin-grants.sql');
  const probe=ci.indexOf('/player-birthday-lifecycle-checks.sql');
  const roleCheck=ci.indexOf('/web-admin-auth-permissions-check.sql');
  assert.ok(grant>0&&probe>grant&&roleCheck>probe);
  assert.match(ci,/ON_ERROR_STOP=1[^\n]*player-birthday-admin-grants\.sql/);
  assert.match(ci,/ON_ERROR_STOP=1[^\n]*player-birthday-lifecycle-checks\.sql/);
});
test('birthday manager SQL remains private to authorized admin role',()=>{
  assert.match(grants,/birthday_function_exposure/);
  assert.match(grants,/birthday_raw_table_exposure/);
  assert.match(grants,/birthday_helper_exposure/);
  assert.match(allow,/'club\.read_admin_player_birthday_preferences\(text,text,text\)'::regprocedure/);
  assert.match(allow,/'club\.write_admin_player_birthday_preferences\(text,text,text,integer,boolean,text,text,text\)'::regprocedure/);
  assert.doesNotMatch(grants,/GRANT (SELECT|INSERT|UPDATE|DELETE) ON club\.player_birthday/);
});
test('synthetic birthday lifecycle covers strict privacy and historical audit',()=>{
  for(const phrase of [
    'Default birthday preferences not private opt-out',
    'Invalid birthday card color was accepted',
    'Control character in birthday wish accepted',
    'Duplicate private birthday card create was accepted',
    'Private birthday card edit enabled public sharing',
    'Private birthday preference version history missing',
    'Cross-owner birthday data access allowed',
    'Unknown-player birthday data access allowed',
    'Revoked manager could update birthday data',
    'Revoked manager could read birthday data'
  ])assert.ok(lifecycle.includes(phrase),phrase);
  assert.match(lifecycle,/ROLLBACK;/);
  assert.doesNotMatch(lifecycle,/INSERT INTO public_content|INSERT INTO marketing|DELETE FROM club\.players/);
});
