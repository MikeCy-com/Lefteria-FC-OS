import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const script=readFileSync(new URL('../../ops/postgres/academy-enquiry-grant-readiness.sql',import.meta.url),'utf8');

test('Academy lead release audit is read-only, isolated and does not enable collection',()=>{
  assert.match(script,/BEGIN READ ONLY;/);
  assert.match(script,/ROLLBACK;/);
  assert.ok(script.includes("current_database() IN ('postgres', 'template0', 'template1')"));
  assert.ok(script.includes('marketing.store_academy_lead(uuid,text,text,text,integer,text,text,text,boolean,boolean,text)'));
  assert.ok(script.includes("search_path=pg_catalog,pg_temp"));
  assert.ok(script.includes("has_function_privilege(runtime_name,function_oid,'EXECUTE')"));
  assert.ok(script.includes("has_schema_privilege(runtime_name,'marketing','USAGE')"));
  assert.ok(script.includes("WHERE grantee=0 AND privilege_type='EXECUTE'"));
  assert.ok(script.includes("c.relname IN ('public_leads','lead_events','lead_rate_buckets')"));
  assert.ok(script.includes("NOT rel.relrowsecurity"));
  for(const privilege of ['SELECT','INSERT','UPDATE','DELETE'])
    assert.ok(script.includes("has_table_privilege(runtime_name,rel.oid,'"+privilege+"')"));
  assert.doesNotMatch(script,/^\s*(?:GRANT|REVOKE|INSERT|UPDATE|DELETE|ALTER|CREATE|DROP)\s/im);
});

test('pre-activation audit is not wired into main database deployment',()=>{
  const compose=readFileSync(new URL('../../compose.yaml',import.meta.url),'utf8');
  const grants=readFileSync(new URL('../../ops/postgres/identity-app-grants.sql',import.meta.url),'utf8');
  assert.doesNotMatch(compose,/academy-enquiry-grant-readiness/);
  assert.doesNotMatch(grants,/academy-enquiry-grant-readiness/);
});
