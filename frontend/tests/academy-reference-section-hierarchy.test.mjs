import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { compile } from 'svelte/compiler';

const page=readFileSync(new URL('../src/routes/academy/+page.svelte',import.meta.url),'utf8');
test('Academy visual sections follow the supplied reference without deleting operational sections',()=>{
  assert.doesNotThrow(()=>compile(page,{filename:'academy/+page.svelte',generate:'server'}));
  const ordered=[
    'class="public-page-heading academy-intro"',
    'class="academy-program-overview"',
    'class="academy-age-groups public-section"',
    'class="academy-philosophy public-section"',
    'class="academy-reference-fees"',
    'class="public-section academy-explore"',
    'class="academy-key-figures public-section"',
    'id="academy-fixtures"',
    'class="academy-enquiry public-section"',
    '<OrganizationSponsorStrip',
    'class="public-section academy-public-reviews"'
  ];
  let cursor=-1;
  for(const selector of ordered){
    const pos=page.indexOf(selector);
    assert.ok(pos>cursor,'Missing or out-of-order section: '+selector);
    cursor=pos;
  }
  for(const marker of [
    'data-testid="academy-hero-register"',
    'data-testid="academy-registration-cta-btn"',
    'data-testid="academy-stats"',
    'data-testid="academy-development-philosophy"',
    'data-testid="academy-enquiry-section"',
    'data-testid="academy-public-reviews"',
    'academyGroupLink(group)',
    'data.site?.fixtures',
    'data.site?.academy_groups',
    'academyStats',
    'academyReviews'
  ]) assert.ok(page.includes(marker),'Operational feature removed: '+marker);
});
test('published group picture cards are scoped Academy → city → location and never First Team',()=>{
  assert.match(page,/organization_id === 'lefteria_fc_academy'/);
  assert.match(page,/buildAcademyTerritoryDirectory\(scopedGroups\)/);
  assert.match(page,/const academyCities = \$derived\(academyTerritory\.cities\)/);
  assert.doesNotMatch(page,/group\.city\?\.trim\(\)|group\.location_name\?\.trim\(\)/);
  assert.match(page,/each academyCities as city \(city\.id\)/);
  assert.match(page,/each city\.locations as location \(location\.id\)/);
  assert.match(page,/each academyTerritory\.unassigned as group \(group\.id\)/);
  assert.match(page,/data-testid="academy-landing-unassigned-groups"/);
  assert.match(page,/data-testid="academy-landing-verified-city"/);
  assert.match(page,/data-testid="academy-landing-verified-location"/);
  assert.match(page,/aria-label="Πόλεις Ακαδημίας"/);
  assert.match(page,/each location\.groups as group/);
  assert.match(page,/group\.banner_url \|\| publicImages\.academy/);
  assert.match(page,/data-testid="academy-age-groups"/);
  assert.match(page,/href=\{academyGroupLink\(group\)\}/);
  assert.doesNotMatch(page,/href="\/team\?tab=staff"|scope=first_team/);
  assert.match(page,/επιβεβαιώνονται από την Ακαδημία/);
  assert.doesNotMatch(page,/€20|€60|€100|άμεση χρέωση/);
  assert.match(page,/href="\/academy\/registration"/);
});
