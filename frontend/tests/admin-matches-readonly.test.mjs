import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const source = readFileSync(new URL('../src/routes/admin/matches/+page.svelte', import.meta.url), 'utf8');
const frame = readFileSync(new URL('../src/lib/admin/AdminFrame.svelte', import.meta.url), 'utf8');
const catalog = readFileSync(new URL('../src/lib/admin/module-catalog.ts', import.meta.url), 'utf8');

test('Matches page uses real published API and rejects fabricated or cross-organization results', () => {
  assert.match(source,/data-testid="admin-matches-page"/);
  assert.match(source,/data-testid="admin-matches-denied"/);
  assert.match(source,/data-testid="admin-matches-unavailable"/);
  assert.match(source,/data-testid="admin-matches-list"/);
  assert.match(source,/verifiedSite\(data, owner\)/);
  assert.match(source,/row\.organization_id !== owner/);
  assert.match(source,/row\.fixtures\.length > 50/);
  assert.match(source,/x\.organization_id === owner/);
  assert.match(source,/request\.abort\(\)/);
  assert.match(source,/cache: 'no-store'/);
  assert.match(source,/\/api\/public\/site\?organization_id=/);
  assert.match(source,/credentials: 'same-origin'/);
  assert.doesNotMatch(source,/\/api\/admin\/(?:payments|fixtures|results)|\bmethod:\s*'(?:PUT|POST|DELETE)'/);
});

test('Club and Academy groups are filtered by the organization set once in the admin header', () => {
  assert.match(source,/preferredAdminOrganization\(\$auth\.session\?\.user \?\? null\)/);
  assert.match(source,/hasAdminPermission\(\$auth\.session\.user\.permissions, 'teams:read'\)/);
  assert.match(source,/hasAdminPermission\(\$auth\.session\.user\.permissions, 'academy:read'\)/);
  assert.match(source,/fixture\.team_id !== team && fixture\.academy_group_id !== team/);
  assert.match(source,/fixture\.match_date\.startsWith\(season\)/);
  assert.match(source,/fixture\.status === 'Completed'/);
  assert.match(source,/fixture\.status === 'Scheduled'/);
  assert.match(source,/fixtureStatus\(fixture\)/);
  assert.match(source,/href=\{'\/match\/' \+ encodeURIComponent\(fixture\.id\)\}/);
  assert.doesNotMatch(frame,/href="\/admin\/matches"/);
  const groupPage=readFileSync(new URL('../src/routes/admin/groups/+page.svelte', import.meta.url),'utf8');
  const groupWorkspace=readFileSync(new URL('../src/lib/admin/AdminGroupWorkspace.svelte',import.meta.url),'utf8');
  assert.match(groupPage,/AdminGroupWorkspace/);
  assert.match(groupWorkspace,/admin-group-matches-link/);
  assert.match(source,/requestedOrganization/);
  assert.match(source,/requestedGroup/);
  assert.match(frame,/includes\(active\)/);
  assert.doesNotMatch(catalog,/id: 'fixtures'/);
});

test('Match page clearly labels its API limitations and does not claim live standings editing', () => {
  assert.match(source,/έως 50/);
  assert.match(source,/βαθμολογία και η επεξεργασία απαιτούν ξεχωριστά επαληθευμένα API/);
  assert.doesNotMatch(source,/dummy|fake|mockScore|Math\.random/);
});
