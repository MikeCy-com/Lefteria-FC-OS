import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const file = path => readFileSync(new URL('../../'+path, import.meta.url),'utf8');
const migration=file('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115910000_WebsitePages.cs');
const api=file('backend/src/LefteriaFc.Api/PublicContent/WebsitePageEndpoints.cs');
const admin=file('frontend/src/routes/admin/website-pages/+page.svelte');
const blocks=file('frontend/src/lib/public/WebsitePageBlocks.svelte');
const nav=file('frontend/src/lib/public/website-pages.ts');
const publicNav=file('frontend/src/lib/public/PublicNavigation.svelte');
const page=file('frontend/src/routes/pages/[slug]/+page.svelte');
const loader=file('frontend/src/lib/server/website-pages.ts');
const appGrants=file('ops/postgres/identity-app-grants.sql');
const authGrants=file('ops/postgres/web-admin-auth-grants.sql');
const accessChecks=file('ops/postgres/identity-app-permissions-check.sql');

test('drafts are never directly readable by the public API role',()=>{
  assert.match(migration,/draft_json jsonb NOT NULL/);
  assert.match(migration,/published_json jsonb NULL/);
  assert.match(migration,/WHERE organization_id=_org AND published_json IS NOT NULL/);
  assert.match(migration,/SECURITY DEFINER/);
  assert.match(migration,/search_path=pg_catalog,pg_temp/);
  assert.match(migration,/REVOKE ALL ON public_content.website_pages FROM PUBLIC/);
  assert.match(appGrants,/REVOKE ALL ON public_content.website_pages FROM PUBLIC, :"app_role"/);
  assert.match(appGrants,/GRANT EXECUTE ON FUNCTION public_content.read_published_website_pages/);
  assert.match(accessChecks,/Website drafts must remain private/);
  assert.match(authGrants,/GRANT UPDATE \(draft_json,published_json,published_at,version,updated_at\)/);
  assert.doesNotMatch(loader,/credentials:\s*'include'|Cookie|Authorization/);
});

test('all mutations revalidate session, exact owner grant and news writer, and publication additionally needs a manager',()=>{
  assert.match(api,/identity.lock_admin_club_authority/);
  assert.match(api,/AdminPermissions.Has\(role, capability\)/);
  assert.match(api,/identity.admin_organization_grants/);
  assert.match(api,/secrets.SameDigest/);
  assert.match(api,/requirePublisher && role is not \("super_admin" or "manager"\)/);
  assert.match(api,/published_json=CASE WHEN @publish THEN draft_json ELSE NULL END/);
  assert.match(api,/version=@version/);
  assert.match(api,/Audit\(db, tx, actor, session/);
  assert.match(api,/UnmappedMemberHandling.Disallow/);
  assert.match(api,/Duplicate property/);
});

test('visual builder provides accessible block controls, guarded publication, SEO and organization targeting',()=>{
  assert.match(admin,/data-testid="website-page-builder"/);
  assert.match(admin,/Εναλλακτικό κείμενο εικόνας/);
  assert.match(admin,/Μετακίνηση πάνω/);
  assert.match(admin,/Μετακίνηση κάτω/);
  assert.match(admin,/περιγραφή/i);
  assert.match(admin,/seo_title/);
  assert.match(admin,/menu_parent/);
  assert.match(admin,/expected_version:selected.version,publish/);
  assert.match(admin,/dirty \|\| saving/);
  assert.match(admin,/hasAdminPermission/);
  assert.match(admin,/Σελίδες · Visual Builder/);
});

test('public page output is typed blocks without raw HTML and menu respects organization',()=>{
  assert.match(nav,/p.organization_id === organization/);
  assert.match(nav,/withWebsitePages/);
  assert.match(nav,/menu_order/);
  assert.match(publicNav,/withWebsitePages\(publicNavigation, publishedPages, organization\)/);
  assert.match(page,/<WebsitePageBlocks blocks=\{content.blocks\}/);
  assert.doesNotMatch(blocks, /\{@html\}|innerHTML|document\.write/);
  assert.match(blocks,/rel="noopener noreferrer"/);
  assert.match(loader,/isPublishedWebsitePage\(item, org\)/);
});
