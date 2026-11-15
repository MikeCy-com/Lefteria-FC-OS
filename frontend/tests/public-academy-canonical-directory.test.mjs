import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const get = file => readFileSync(new URL('../../' + file,import.meta.url),'utf8');

test('Academy directory reads canonical groups rather than exposing disconnected public snapshot groups',()=>{
  const reader=get('backend/src/LefteriaFc.Infrastructure/PublicContent/PostgresPublicContentRepository.cs');
  const migration=get('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115110000_CanonicalAcademyPublicDirectory.cs');
  assert.match(reader,/organizationId == "lefteria_fc_academy"/);
  assert.match(reader,/club\.read_public_academy_group_directory\(\)::text/);
  assert.match(reader,/if \(approvedJson is null\) return null;/);
  assert.doesNotMatch(reader,/Read<PublicAcademyGroup>\(PublicContentKinds\.AcademyGroup\)/);
  assert.match(migration,/FROM club\.academy_groups g/);
  assert.match(migration,/LEFT JOIN club\.academy_group_profiles p/);
  assert.match(migration,/LEFT JOIN public_content\.documents d ON d\.kind='academy_group'/);
  assert.match(migration,/d\.id=g\.id/);
  assert.match(migration,/WHERE g\.organization_id='lefteria_fc_academy'/);
  assert.match(migration,/club\.roster_import_state WHERE id='initial'/);
  for(const key of ['id','name','age_range','city','location_name','training_schedule','description','banner_url','season','display_order'])
    assert.ok(migration.includes("'"+key+"'"),'Missing public group field '+key);
  assert.doesNotMatch(migration,/player_phone|medical_notes|guardian|parent_contact|password_hash/);
});

test('Academy group database bridge enforces restricted reader, retains original presentation and checks real PostgreSQL',()=>{
  const migration=get('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115110000_CanonicalAcademyPublicDirectory.cs');
  const grants=get('ops/postgres/identity-app-grants.sql');
  const privilege=get('ops/postgres/identity-app-permissions-check.sql');
  const checks=get('ops/postgres/public-academy-directory-checks.sql');
  const ci=get('.github/workflows/rewrite-build.yml');
  assert.match(migration,/LANGUAGE sql STABLE SECURITY DEFINER/);
  assert.match(migration,/SET search_path=pg_catalog,pg_temp/);
  assert.match(migration,/REVOKE ALL ON FUNCTION club\.read_public_academy_group_directory\(\)/);
  assert.match(grants,/REVOKE ALL ON TABLE club\.academy_group_profiles FROM PUBLIC, :"app_role";/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION club\.read_public_academy_group_directory\(\)/);
  assert.match(grants,/prosecdef IS DISTINCT FROM true/);
  assert.match(grants,/has_any_column_privilege/);
  assert.match(privilege,/club\.read_public_academy_group_directory\(\)/);
  assert.match(privilege,/has_column_privilege\(current_user,'club\.academy_groups','name','SELECT'\)/);
  assert.match(checks,/v_ghost/);
  assert.match(checks,/Manager updated public profile/);
  assert.match(checks,/jsonb_object_keys\(v_group\)/);
  assert.match(checks,/ROLLBACK;/);
  assert.match(ci,/public-academy-directory-checks\.sql/);
});
