import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const read = path => readFileSync(new URL('../../' + path, import.meta.url),'utf8');
const api = read('backend/src/LefteriaFc.Api/PublicContent/AdminNewsEndpoints.cs');
const writer = read('backend/src/LefteriaFc.Infrastructure/PublicContent/PostgresAdminNewsRepository.cs');
const contract = read('backend/src/LefteriaFc.Application/PublicContent/AdminNewsContract.cs');
const migration = read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115100000_PublicNewsEditor.cs');
const reader = read('backend/src/LefteriaFc.Infrastructure/PublicContent/PostgresPublicContentRepository.cs');
const editor = read('frontend/src/lib/admin/AdminNewsEditor.svelte');
const page = read('frontend/src/routes/admin/news/+page.svelte');
const guards = read('backend/src/LefteriaFc.Api/WebAdmin/WebAdminRequestGuards.cs');

test('News writer never accepts imported identifiers, organization, publication timestamp or private fields',()=>{
  assert.match(contract,/record AdminNewsEditable\(/);
  assert.match(contract,/string Title, string Content, string Excerpt, string\? ImageUrl,/);
  assert.doesNotMatch(contract,/record AdminNewsEditable\([^)]*(Id|CreatedAt|OrganizationId|SourceJson|Guardian)/);
  assert.match(contract,/original with/);
  assert.match(contract,/RosterImportValidator.PublicImageUrl/);
  assert.match(contract,/JsonUnmappedMemberHandling\.Disallow/);
  assert.match(api,/RejectDuplicateKeys\(document.RootElement\)/);
  assert.match(api,/JsonUnmappedMemberHandling\.Disallow/);
  assert.match(api,/new JsonDocumentOptions \{ MaxDepth = 32 \}/);
});
test('Published news edits use current session and grant locks, optimistic conflict, atomic audit',()=>{
  assert.match(writer,/identity\.lock_admin_club_authority\(@token,@org\)/);
  assert.match(writer,/EXISTS\(SELECT 1 FROM identity\.admin_organization_grants g/);
  assert.match(writer,/AdminPermissions\.Has\(role, update is null \? "news:read" : "news:write"\)/);
  assert.match(writer,/!secrets\.SameDigest/);
  assert.match(writer,/d\.kind='news' AND d\.organization_id=@org AND d\.id=@id/);
  assert.match(writer,/INSERT INTO public_content\.edits/);
  assert.match(writer,/WHERE edits\.version=@version/);
  assert.match(writer,/WHERE \(@version=0 OR EXISTS\(/);
  assert.match(writer,/WHERE kind='news' AND organization_id=@org AND id=@id AND version=@version/);
  assert.match(writer,/INSERT INTO audit\.security_events/);
  assert.match(writer,/admin\.news\.update/);
  assert.match(writer,/throw Conflict\(\)/);
  assert.match(writer,/await transaction\.CommitAsync\(ct\)/);
  assert.doesNotMatch(writer,/UPDATE public_content\.documents|DELETE FROM public_content|public_content_source_documents/);
});
test('Public homepage and news feeds overlay only safe owner-specific published projections',()=>{
  assert.match(reader,/newsEdits\.TryGetValue\(row\.Id/);
  assert.match(reader,/newsEdits\.TryGetValue\(\(row\.OrganizationId, row\.Id\)/);
  assert.match(reader,/Kind == PublicContentKinds\.News/);
  assert.match(reader,/\.Select\(row => new \{ row\.Id, row\.DataJson \}\)/);
  assert.match(reader,/\.Select\(row => new \{ row\.OrganizationId, row\.Id, row\.DataJson \}\)/);
  assert.doesNotMatch(reader,/editQuery\.ToArrayAsync\(token\)/);
  assert.match(reader,/GroupBy\(row => row\.Id, StringComparer\.Ordinal\)/);
  assert.match(migration,/kind IN \('club','news'\) AND version > 0/);
  assert.match(migration,/news_edits_require_forward_recovery/);
  assert.doesNotMatch(migration,/DELETE FROM public_content\.documents/);
});
test('CMS editor confirms updated JSON and never claims success on invalid/concurrent writes',()=>{
  assert.match(editor,/data-testid="admin-news-edit-form"/);
  assert.match(editor,/expected_version: version, article: pending/);
  assert.match(editor,/'X-Lefteria-Admin': '1'/);
  assert.match(editor,/method: 'PUT'/);
  assert.match(editor,/response\.status === 409/);
  assert.match(editor,/result\.version <= version/);
  assert.match(editor,/JSON\.stringify\(draft\) !== baseline/);
  assert.match(editor,/window\.confirm/);
  assert.match(editor,/onSaved\(\)/);
  assert.match(editor,/controller\.abort\(\)/);
  assert.match(page,/AdminNewsEditor owner=\{organization\}/);
  assert.match(page,/scopedAdminOrganization/);
  assert.doesNotMatch(editor,/\{@html\}|innerHTML|localStorage/);
  assert.match(guards,/256 \* 1024/);
});
