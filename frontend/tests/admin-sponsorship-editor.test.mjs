import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {adminSectionsFor} from '../src/lib/admin/module-catalog.ts';
const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const actor=(permissions,organizations=['lefteria_fc'],role='editor')=>({
  id:'fixture',username:'fixture',name:'Fixture',role,permissions,
  organizations:organizations.map(id=>({id,name:id}))
});
const listing=user=>adminSectionsFor(user).flatMap(category=>category.modules);
test('sponsor admin navigation uses the exact current sponsor capability and organization grants',()=>{
  assert.equal(listing(actor(['sponsors:read'])).find(item=>item.id==='sponsors')?.href,'/admin/sponsors');
  assert.ok(!listing(actor(['gallery:read'])).some(item=>item.id==='sponsors'));
  assert.ok(!listing(actor(['news:read'])).some(item=>item.id==='sponsors'));
  assert.ok(!listing(actor(['sponsors:read'],[])).some(item=>item.id==='sponsors'));
  assert.equal(listing(actor(['sponsors:read'],['lefteria_fc_academy']))
    .find(item=>item.id==='sponsors')?.status,'live');
  const frame=read('frontend/src/lib/admin/AdminFrame.svelte');
  assert.match(frame,/liveIds\.has\('sponsors'\)/);
  assert.match(frame,/href="\/admin\/sponsors"/);
});
test('admin Svelte screen is real, scope-controlled and distinguishes draft vs approved state',()=>{
  const page=read('frontend/src/routes/admin/sponsors/+page.svelte');
  assert.doesNotThrow(()=>compile(page,{filename:'admin/sponsors/+page.svelte',generate:'server'}));
  assert.match(page,/ADMIN_AUTH_CONTEXT/);
  assert.match(page,/hasAdminPermission\(\$auth\.session\.user\.permissions,'sponsors:read'\)/);
  assert.match(page,/hasAdminPermission\(\$auth\.session\.user\.permissions,'sponsors:write'\)/);
  assert.match(page,/\['manager','super_admin'\]/);
  assert.match(page,/credentials:'same-origin'/);
  assert.match(page,/validRow\(row,owner\)/);
  assert.match(page,/item\.organization_id!==organization/);
  assert.match(page,/asset\.organization_id!==organization/);
  assert.match(page,/expected_version:selected.version/);
  assert.match(page,/if\(response.status===409\)/);
  assert.match(page,/\/publication/);
  assert.match(page,/PublishedMediaBrowser/);
  assert.doesNotMatch(page,/\{@html\}|innerHTML|localStorage|FormData/);
});
test('API always checks current source identity, exact admin owner and manager publication rights',()=>{
  const api=read('backend/src/LefteriaFc.Api/PublicContent/AdminSponsorshipEndpoints.cs');
  const auth=read('backend/src/LefteriaFc.Api/PublicContent/WebsitePageEndpoints.cs');
  assert.match(api,/RequireWritableIdentity\(db,tx,owner,change\.Kind,change\.ItemId,ct\)/);
  assert.match(api,/await RequireImported\(db,tx,org,kind,id,ct\)/);
  assert.match(api,/WebsitePageEndpoints\.Authorized\(context,owner,"sponsors:write"/);
  assert.match(api,/WebsitePageEndpoints\.Authorized\(context, owner, "sponsors:read"/);
  assert.match(api,/requirePublisher:true/);
  assert.match(api,/WHERE organization_id=@org AND kind=@kind/);
  assert.match(api,/AND item_id=@id AND version=@expected/);
  assert.match(api,/ON CONFLICT DO NOTHING/);
  assert.match(api,/admin\.sponsorship\.(?:draft|publish|unpublish)/);
  assert.match(api,/JsonUnmappedMemberHandling\.Disallow/);
  assert.match(api,/Unique\(json.RootElement\)/);
  assert.match(auth,/internal static async Task<T> Authorized<T>/);
  assert.match(auth,/identity\.lock_admin_club_authority/);
  assert.match(auth,/credential_digest/);
  assert.match(auth,/reader\.GetBoolean\(5\)/);
});
test('public site consumes only approved sponsor/partner copy, leaving private imports untouched',()=>{
  const repository=read('backend/src/LefteriaFc.Infrastructure/PublicContent/PostgresPublicSectionsRepository.cs');
  const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115980000_SponsorshipEditorial.cs');
  const appGrants=read('ops/postgres/public-sections-app-grants.sql');
  const adminGrants=read('ops/postgres/web-admin-auth-grants.sql');
  assert.match(repository,/read_published_sponsorship/);
  assert.doesNotMatch(repository,/sponsorship_editorial/);
  assert.match(repository,/item with \{/);
  assert.match(repository,/item with \{/);
  assert.match(repository,/LogoUrl = copy.ImageUrl/);
  assert.match(repository,/HeroImageUrl = copy.ImageUrl/);
  assert.match(migration,/published_json IS NOT NULL/);
  assert.match(migration,/kind IN \('sponsor','partner'\)/);
  assert.match(migration,/current_sponsorship_exists/);
  assert.match(migration,/REVOKE ALL ON FUNCTION public_content.read_published_sponsorship\(text\) FROM PUBLIC/);
  assert.match(appGrants,/GRANT EXECUTE ON FUNCTION public_content.read_published_sponsorship\(text\)/);
  assert.match(adminGrants,/GRANT UPDATE \(draft_json,published_json,published_at,version,updated_at\)/);
  assert.match(adminGrants,/REVOKE ALL ON FUNCTION public_content.current_sponsorship_exists/);
  assert.ok(!migration.includes('DROP TABLE public_sections.documents'));
  assert.match(read('backend/src/LefteriaFc.Api/Program.cs'),/app\.MapAdminSponsorships\(\)/);
});
test('contracts bound editable fields and exclude source IDs, contracts and money',()=>{
  const dto=read('backend/src/LefteriaFc.Application/PublicContent/SponsorshipEditorialContracts.cs');
  assert.match(dto,/sealed record SponsorshipEditorialCopy/);
  for(const field of ['Name','Description','ImageUrl','WebsiteUrl','DisplayOrder'])
    assert.match(dto,new RegExp('\\b'+field+'\\b'));
  for(const disallowed of ['BankAccount','CardNumber','AmountCents','ContractDocument','PrivateContact'])
    assert.doesNotMatch(dto,new RegExp('\\b'+disallowed+'\\b'));
  assert.match(dto,/uri\.Scheme == Uri\.UriSchemeHttps/);
  assert.match(dto,/Clean\(Name\)/);
});

test('native record creation never permits a supplied source ID or anonymous publication',()=>{
  const api=read('backend/src/LefteriaFc.Api/PublicContent/AdminSponsorshipEndpoints.cs');
  const repository=read('backend/src/LefteriaFc.Infrastructure/PublicContent/PostgresPublicSectionsRepository.cs');
  const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261121900000_NativeSponsorshipDrafts.cs');
  const ui=read('frontend/src/routes/admin/sponsors/+page.svelte');
  const gateway=read('deploy/preview/generate-edge.mjs');
  assert.match(api,/sealed record SponsorshipNativeCreate\(string Kind, SponsorshipEditorialCopy Draft\)/);
  assert.match(api,/group.MapPost\("\/native"/);
  assert.match(api,/WebsitePageEndpoints.Authorized\(context, owner, "sponsors:write"/);
  assert.match(api,/Guid.NewGuid\(\).ToString\("N"\)/);
  assert.match(api,/is_native,draft_json/);
  assert.match(api,/WHERE NOT public_content.current_sponsorship_exists/);
  assert.match(api,/admin\.sponsorship\.create/);
  assert.match(api,/await RequireWritableIdentity/);
  assert.match(api,/SELECT is_native FROM public_content.sponsorship_editorial/);
  assert.match(api,/WHERE organization_id=@org AND kind=@kind AND item_id=@id/);
  assert.match(repository,/approvedNative = approved.Where\(e => e.IsNative/);
  assert.match(repository,/!e.IsNative/);
  assert.match(repository,/Concat\(nativeSponsors\)/);
  assert.match(repository,/Concat\(nativePartners\)/);
  assert.match(migration,/ADD COLUMN is_native boolean NOT NULL DEFAULT false/);
  assert.match(migration,/ck_sponsorship_native_identity/);
  assert.match(migration,/WHERE organization_id=_org AND published_json IS NOT NULL/);
  assert.match(migration,/native_sponsorship_requires_forward_recovery/);
  assert.match(gateway,/\['post','\/api\/admin\/website\/sponsorships\/native'\]/);
  assert.match(ui,/data-testid="sponsorship-create"/);
  assert.match(ui,/credentials:'same-origin'/);
  assert.match(ui,/if\(!validRow\(payload,owner\)\|\|!payload.is_native/);
  assert.match(ui,/selected.is_native\?/);
  assert.match(ui,/createOpen/);
  assert.doesNotMatch(ui,/\{@html\}|innerHTML|localStorage|FormData/);
  assert.doesNotThrow(()=>compile(ui,{filename:'admin/sponsors/+page.svelte',generate:'server'}));
});
