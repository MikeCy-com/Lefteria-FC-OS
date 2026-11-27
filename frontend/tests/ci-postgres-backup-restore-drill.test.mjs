import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {spawnSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
const base=new URL('../../',import.meta.url);
const scriptUrl=new URL('ops/postgres/disposable-backup-restore-drill.sh',base);
const script=readFileSync(scriptUrl,'utf8');
const workflow=readFileSync(new URL('.github/workflows/rewrite-build.yml',base),'utf8');
const scriptPath=fileURLToPath(scriptUrl);

test('restore drill refuses runs without explicit ephemeral CI acknowledgement',()=>{
  const failed=spawnSync('sh',[scriptPath],{
    encoding:'utf8',timeout:4000,
    env:{PATH:process.env.PATH,POSTGRES_DB:'lefteria_rewrite_identity_tests',
      POSTGRES_USER:'lefteria_tests',POSTGRES_PASSWORD:'synthetic-only'}
  });
  assert.equal(failed.status,1);
  assert.match(failed.stderr,/missing CI-only opt-in/);
  assert.doesNotMatch(failed.stdout,/PASS:/);
});

test('restore drill rejects a production-like database before any SQL command',()=>{
  const failed=spawnSync('sh',[scriptPath],{
    encoding:'utf8',timeout:4000,
    env:{PATH:process.env.PATH,LEFTERIA_DISPOSABLE_RESTORE_ACK:'ci-only',
      POSTGRES_DB:'production',POSTGRES_USER:'lefteria_tests',
      POSTGRES_PASSWORD:'synthetic-only'}
  });
  assert.equal(failed.status,1);
  assert.match(failed.stderr,/unexpected source database/);
});

test('restore drill rejects external hosts and non-ephemeral ports before connecting',()=>{
  for(const env of [
    {PGHOST:'db.example.org'},
    {PGPORT:'5432'}
  ]){
    const failed=spawnSync('sh',[scriptPath],{
      encoding:'utf8',timeout:4000,
      env:{PATH:process.env.PATH,LEFTERIA_DISPOSABLE_RESTORE_ACK:'ci-only',
        POSTGRES_DB:'lefteria_rewrite_identity_tests',POSTGRES_USER:'lefteria_tests',
        POSTGRES_PASSWORD:'synthetic-only',...env}
    });
    assert.equal(failed.status,1);
    assert.match(failed.stderr,/only (local loopback PostgreSQL|the fixed CI PostgreSQL port) is allowed/);
  }
});

test('actual restore is independent, format-checked and cleans its test archive',()=>{
  assert.match(script,/pg_dump/);
  assert.match(script,/--format=custom/);
  assert.match(script,/--no-owner --no-acl/);
  assert.match(script,/pg_restore --list/);
  assert.match(script,/pg_restore --exit-on-error/);
  assert.match(script,/createdb/);
  assert.match(script,/--template=template0/);
  assert.match(script,/source_manifest/);
  assert.match(script,/restored_manifest/);
  assert.match(script,/pg_policy/);
  assert.match(script,/pg_constraint/);
  assert.match(script,/__EFMigrationsHistory/);
  assert.match(script,/synthetic-ci-only/);
  assert.match(script,/DROP TABLE public\.lefteria_ci_restore_canary/);
  assert.match(script,/trap cleanup EXIT/);
  assert.match(script,/dropdb --if-exists/);
  assert.match(script,/rm -rf "\$work_dir"/);
  assert.doesNotMatch(script,/aws s3|scp |rclone|curl |rsync|docker login|pg_dumpall/);
});

test('full PostgreSQL restore drill runs after isolated forward-only recovery in the CI identity job',()=>{
  const migrated=workflow.indexOf('name: Migration apply, idempotent replay and isolated forward recovery');
  const roleProvisioned=workflow.indexOf('name: Provision and check restricted runtime role');
  const restore=workflow.indexOf('name: Prove isolated PostgreSQL backup and restore');
  const later=workflow.indexOf('name: Verify Payabl finance schema');
  assert.ok(migrated>=0&&roleProvisioned>migrated&&restore>roleProvisioned&&later>restore);
  assert.match(workflow,/docker exec -e LEFTERIA_DISPOSABLE_RESTORE_ACK=ci-only/);
  assert.match(workflow,/lefteria_identity_forward_probe/);
  assert.match(workflow,/primary_count.*probe_count/);
  assert.doesNotMatch(workflow,/dotnet ef database update 0/);
  assert.match(workflow,/identity-postgres/);
  assert.ok(workflow.includes('sh /opt/lefteria-postgres/disposable-backup-restore-drill.sh'));
  assert.match(workflow,/postgres:18\.6-alpine -c listen_addresses=127\.0\.0\.1 -p 54329/);
});
