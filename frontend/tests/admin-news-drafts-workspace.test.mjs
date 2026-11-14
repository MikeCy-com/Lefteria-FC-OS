import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = p => readFileSync(new URL('../../' + p, import.meta.url), 'utf8');
const page = read('frontend/src/routes/admin/news/+page.svelte');
const component = read('frontend/src/lib/admin/AdminNewsDraftWorkspace.svelte');
const api = read('backend/src/LefteriaFc.Api/PublicContent/AdminNewsDraftEndpoints.cs');
const contract = read('backend/src/LefteriaFc.Application/PublicContent/AdminNewsDraftContract.cs');
const store = read('backend/src/LefteriaFc.Infrastructure/PublicContent/PostgresAdminNewsDraftRepository.cs');
const migration = read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115090000_PrivateNewsDraftWorkspace.cs');
const grants = read('ops/postgres/web-admin-auth-grants.sql');
const appCheck = read('ops/postgres/identity-app-permissions-check.sql');
const program = read('backend/src/LefteriaFc.Api/Program.cs');
const guards = read('backend/src/LefteriaFc.Api/WebAdmin/WebAdminRequestGuards.cs');

test('news drafts are private and published endpoints remain separate', () => {
  assert.match(api,/MapGroup\("\/api\/admin\/website\/news-drafts"\)/);
  assert.match(api,/RequireAuthorization\(WebAdminServices.Policy\)/);
  assert.match(api,/group\.MapGet/);
  assert.match(api,/group\.MapPost/);
  assert.match(api,/group\.MapPut/);
  assert.doesNotMatch(api,/Publish|public\/news|admin\.news\.update/);
  assert.match(migration,/CREATE TABLE public_content\.news_drafts/);
  assert.match(migration,/REVOKE ALL ON public_content\.news_drafts FROM PUBLIC/);
  assert.match(migration,/news_drafts_require_forward_recovery/);
  assert.match(grants,/REVOKE ALL ON public_content\.news_drafts FROM PUBLIC, :"app_role"/);
  assert.match(grants,/GRANT SELECT, INSERT ON public_content\.news_drafts TO :"admin_auth_role"/);
  assert.match(appCheck,/Public\/mobile runtime must never read or write unpublished news drafts/);
});

test('drafts require current role, owner grant, version guard and audited writes', () => {
  assert.match(store,/identity\.lock_admin_club_authority\(@token,@org\)/);
  assert.match(store,/AdminPermissions\.Has\(role, capability\)/);
  assert.match(store,/identity\.admin_organization_grants/);
  assert.match(store,/secrets\.CredentialDigest\(password\)/);
  assert.match(store,/WHERE organization_id=@org AND id=@id AND version=@version/);
  assert.match(store,/WHERE id=@session AND revoked_at IS NULL AND expires_at>clock_timestamp\(\)/);
  assert.match(store,/admin\.news_draft\.create/);
  assert.match(store,/admin\.news_draft\.update/);
  assert.match(store,/await transaction\.CommitAsync\(ct\)/);
  assert.doesNotMatch(store,/DELETE FROM|INSERT INTO public_content\.documents|INSERT INTO public_content\.edits/);
  assert.match(api,/RejectDuplicateKeys\(document.RootElement\)/);
  assert.match(contract,/JsonUnmappedMemberHandling\.Disallow/);
  assert.match(contract,/JsonNumberHandling\.Strict/);
});

test('draft panel follows global selection and never fabricates publication', () => {
  assert.match(page,/AdminNewsDraftWorkspace owner=\{organization\} \{canWrite\}/);
  assert.match(page,/scopedAdminOrganization\(/);
  assert.match(component,/data-testid="admin-news-drafts"/);
  assert.match(component,/data-testid="admin-news-draft-form"/);
  assert.match(component,/\'X-Lefteria-Admin\': \'1\'/);
  assert.match(component,/credentials: 'same-origin'/);
  assert.match(component,/cache: 'no-store'/);
  assert.match(component,/selectedAtStart \? 'PUT' : 'POST'/);
  assert.match(component,/response\.status === 409/);
  assert.match(component,/window\.confirm/);
  assert.doesNotMatch(component,/\{@html\}|innerHTML|\/api\/public\/news|localStorage/);
  assert.match(program,/MapAdminNewsDrafts\(\)/);
  assert.match(program,/IAdminNewsDraftRepository, PostgresAdminNewsDraftRepository/);
  assert.match(guards,/\/api\/admin\/website\/news-drafts\/draft_/);
});
