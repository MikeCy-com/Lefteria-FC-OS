import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const source = name=>readFileSync(new URL('../../'+name,import.meta.url),'utf8');

const migration=source('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115120000_AcademyGroupPublicVisibility.cs');
const api=source('backend/src/LefteriaFc.Api/Audiences/AdminAcademyGroupProfileEndpoints.cs');
const ui=source('frontend/src/lib/admin/AdminAcademyGroupProfilePanel.svelte');
const adminGrants=source('ops/postgres/admin-academy-profile-grants.sql');
const runtimeChecks=source('ops/postgres/identity-app-permissions-check.sql');
const boundary=source('ops/postgres/admin-academy-profile-boundary.sql');
const sqlFixture=source('ops/postgres/public-academy-directory-checks.sql');
const openApi=JSON.parse(source('contracts/openapi.json'));
const schema=source('frontend/src/lib/api/schema.d.ts');

test('An unapproved or manager-unpublished Academy group is absent from every public Academy projection',()=>{
  assert.match(migration,/ADD COLUMN is_public boolean NOT NULL DEFAULT true/);
  assert.match(migration,/WHERE g\.organization_id='lefteria_fc_academy' AND p\.is_public/);
  assert.match(migration,/JOIN club\.academy_group_profiles p/);
  assert.match(migration,/LEFT JOIN public_content\.documents d ON d\.kind='academy_group'/);
  assert.match(migration,/CREATE OR REPLACE FUNCTION club\.read_public_academy_group_directory\(\)/);
  assert.match(sqlFixture,/Manager-unpublished Academy group still appears in public directory/);
  assert.match(sqlFixture,/Republished Academy group did not return to public directory/);
  assert.match(sqlFixture,/ROLLBACK;/);
  assert.doesNotMatch(migration,/DELETE FROM club\.academy_groups|UPDATE club\.players|DELETE FROM club\.player_academy_groups/);
});

test('Only verified Academy management can publish or unpublish, with optimistic revision, audit and denial',()=>{
  assert.match(migration,/v_actor:=club\.authorize_admin_academy_profile\(_digest\)/);
  assert.match(migration,/p\.revision=_expected/);
  assert.match(migration,/academy_visibility_revision_conflict/);
  assert.match(migration,/revision=p\.revision\+1/);
  assert.match(migration,/admin\.academy\.group\.public_visibility\.update/);
  assert.match(migration,/CASE WHEN _public THEN 'published' ELSE 'unpublished'/);
  assert.match(migration,/REVOKE ALL ON FUNCTION club\.set_admin_academy_group_publicity/);
  assert.match(adminGrants,/GRANT EXECUTE ON FUNCTION club\.read_admin_academy_group_publicity/);
  assert.match(adminGrants,/GRANT EXECUTE ON FUNCTION club\.set_admin_academy_group_publicity/);
  assert.match(adminGrants,/academy_profile_public_function_detected/);
  assert.match(runtimeChecks,/Public\/mobile runtime can access manager Academy publication controls/);
  assert.match(boundary,/academy_visibility_unauthorized_publication_was_accepted/);
  assert.match(migration,/academy_group_publication_history_requires_forward_recovery/);
});

test('Visibility endpoint requires a real manager and exactly scoped, bounded two-key JSON',()=>{
  assert.match(api,/MapPut\("\/\{groupId\}\/visibility"/);
  assert.match(api,/var cookie=await VerifyAdmin\(admin,organization_id,context,ct\)/);
  assert.match(api,/AdminPermissions.Has\(session\.User\.Role,"academy:write"\)/);
  assert.match(api,/context.Request.Query\["organization_id"\]\.Count!=1/);
  assert.match(api,/ArrayPool<byte>\.Shared\.Rent\(maxLength\+1\)/);
  assert.match(api,/if\(used>maxLength\)/);
  assert.match(api,/properties.Length!=2/);
  assert.match(api,/Distinct\(StringComparer\.Ordinal\)/);
  assert.match(api,/value is <1 or >1000000000/);
  assert.match(api,/visibility.ValueKind is not \(JsonValueKind.True or JsonValueKind.False\)/);
  assert.match(api,/club\.set_admin_academy_group_publicity\(@digest,@id,@revision,@public\)/);
  assert.match(api,/club\.read_admin_academy_group_publicity\(@digest,@id\)/);
  assert.match(api,/ArrayPool<byte>\.Shared\.Return\(bytes,clearArray:true\)/);
  assert.doesNotMatch(api,/Results\.Ok\(true\)|anonymous\/visibility/);
});

test('Admin group editor exposes explicit accessible publish state with conflict, revocation and verified server response',()=>{
  assert.doesNotThrow(()=>compile(ui,{filename:'AdminAcademyGroupProfilePanel.svelte',generate:'server'}));
  assert.match(ui,/is_public:boolean/);
  assert.match(ui,/typeof p\.is_public==='boolean'/);
  assert.match(ui,/record\.revision<1/);
  assert.match(ui,/type="checkbox" bind:checked=\{publiclyVisible\}/);
  assert.match(ui,/data-testid="academy-group-visibility-save"/);
  assert.match(ui,/body:JSON\.stringify\(\{expected_revision:revision,is_public:next\}\)/);
  assert.match(ui,/response\.status===401/);
  assert.match(ui,/response\.status===403/);
  assert.match(ui,/response\.status===409/);
  assert.match(ui,/raw\.revision!==revision\+1\|\|raw\.is_public!==next/);
  assert.match(ui,/Δεν διαγράφει παίκτες/);
  assert.doesNotMatch(ui,/localStorage|\{@html\}|alert\(/);
});

test('OpenAPI and generated TypeScript contract expose the visibility control independently of profile edits',()=>{
  const endpoint=openApi.paths['/api/admin/football/academy-group-profiles/{groupId}/visibility'];
  assert.equal(endpoint.put.operationId,'SetAdminAcademyGroupPublicity');
  assert.deepEqual(endpoint.put.security,[{WebAdmin:[]}]);
  assert.equal(endpoint.put.requestBody.content['application/json'].schema.$ref,
    '#/components/schemas/AcademyGroupVisibilityCommand');
  assert.deepEqual(openApi.components.schemas.AcademyGroupVisibilityCommand.required,['expected_revision','is_public']);
  assert.equal(openApi.components.schemas.AcademyGroupVisibilityCommand.properties.is_public.type,'boolean');
  assert.match(schema,/SetAdminAcademyGroupPublicity/);
  assert.match(schema,/AcademyGroupVisibilityCommand: \{/);
  assert.match(schema,/is_public: boolean/);
});
