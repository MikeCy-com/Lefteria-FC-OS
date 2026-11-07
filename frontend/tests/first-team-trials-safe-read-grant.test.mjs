import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const load=(path)=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const grants=load('ops/postgres/identity-app-grants.sql');
const runtime=load('ops/postgres/identity-app-permissions-check.sql');

test('reviewed grant exposes only fixed closed display to runtime, never settings table',()=>{
  assert.ok(grants.includes('REVOKE ALL ON TABLE club.first_team_trial_settings FROM PUBLIC'));
  assert.ok(grants.includes('REVOKE ALL ON FUNCTION club.read_first_team_trial_display() FROM PUBLIC'));
  assert.ok(grants.includes('GRANT EXECUTE ON FUNCTION club.read_first_team_trial_display() TO :"app_role"'));
  assert.ok(grants.includes('p.prosecdef IS DISTINCT FROM true'));
  assert.ok(grants.includes('search_path=pg_catalog,pg_temp'));
  assert.doesNotMatch(grants,/GRANT (?:SELECT|INSERT|UPDATE|DELETE) ON (?:TABLE )?club\.first_team_trial_settings/);
});
test('runtime database privilege audit enforces closed projection and no private DML',()=>{
  assert.ok(runtime.includes("'club.read_first_team_trial_display()','EXECUTE'"));
  assert.ok(runtime.includes("'club.first_team_trial_settings'"));
  assert.ok(runtime.includes('SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER'));
  assert.ok(runtime.includes('WHERE is_open'));
  assert.ok(runtime.includes('Unverified trial registration unexpectedly opened'));
});
