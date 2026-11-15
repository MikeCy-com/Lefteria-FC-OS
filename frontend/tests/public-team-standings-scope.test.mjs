import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { compile } from 'svelte/compiler';

const page = readFileSync(new URL('../src/routes/team/+page.svelte', import.meta.url), 'utf8');

test('team overview is valid Svelte and only uses a verified First Team standing projection', () => {
  assert.ok(compile(page, {filename:'team/+page.svelte', generate:'server'}).js.code.length > 100);
  assert.match(page, /const isSelectedFirstTeam = \$derived\(Boolean\(selectedTeam &&/);
  assert.match(page, /selectedTeam\.organization_id === 'lefteria_fc'/);
  assert.match(page, /const firstTeamStandings = \$derived\(isSelectedFirstTeam/);
  assert.match(page, /const competitions = \$derived\(\[\.\.\.new Set\(firstTeamStandings\.map\(row => row\.competition\)\)\]\)/);
  assert.match(page, /\{#each firstTeamStandings\.filter\(row=>row\.competition===competition\)/);
  assert.doesNotMatch(page, /\{#each \(data\.sections\?\.standings \?\? \[\]\)\.filter\(/);
  assert.match(page, /data-testid="team-standings-unavailable"/);
  assert.match(page, /Δεν έχει δημοσιευτεί επιβεβαιωμένη βαθμολογία για αυτή την ομάδα/);
});

test('non-first Club views never inherit First Team sponsor strip as fallback', () => {
  assert.match(page, /\{#if isSelectedFirstTeam\}\s*<OrganizationSponsorStrip organization="lefteria_fc"/);
  assert.doesNotMatch(page, /data\.site\?\.teams\[0\]\?\.id/);
  assert.match(page, /const matchesTeam =/);
  assert.match(page, /team_id=/);
  assert.match(page, /<PublicTeamMatchOverview \{fixtures\} standings=\{firstTeamStandings\}/);
});

test('SEO, roster fallback and statistics caption describe the actual selected team', () => {
  assert.match(page, /<meta name="description" content=\{\`\$\{selectedTeam\?\.name/);
  assert.match(page, /\{player\.position \|\| selectedTeam\?\.name \|\| 'ΛΕΥΤΕΡΙΑ FC'\}/);
  assert.match(page, /Στατιστικά παικτών \{selectedTeam\?\.name \?\? 'της ομάδας'\}/);
  assert.doesNotMatch(page, /Στατιστικά παικτών πρώτης ομάδας/);
});
