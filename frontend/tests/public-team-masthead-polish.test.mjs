import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const team=readFileSync(new URL('../src/routes/team/+page.svelte',import.meta.url),'utf8');
const css=readFileSync(new URL('../src/lib/public/club-polish.css',import.meta.url),'utf8');
const browser=readFileSync(new URL('../scripts/ui-visual-audit.mjs',import.meta.url),'utf8');

test('First Team hero compiles and never borrows an Academy banner',()=>{
  assert.ok(compile(team,{filename:'routes/team/+page.svelte',generate:'server'}).js.code.length>100);
  assert.match(team,/const teamHeadingPhoto = \$derived\(/);
  assert.match(team,/selectedTeam\?\.organization_id === 'lefteria_fc' \? selectedTeam\.banner_url : null/);
  assert.match(team,/data\.site\?\.club\?\.stadium_image_url \|\| publicImages\.pitch/);
  assert.match(team,/data-testid="team-heading-photo"/);
  assert.match(team,/<PublicImage src=\{teamHeadingPhoto\} alt="" eager/);
  assert.doesNotMatch(team,/teamHeadingPhoto[^;]*publicImages\.academy/);
  assert.match(team,/organization="lefteria_fc" sponsors=\{data\.sections\?\.sponsors \?\? \[\]\}/);
});

test('First Team masthead does not override the separate verified matches hub',()=>{
  assert.match(team,/class:matches-reference-heading=\{data\.tab === 'schedule' \|\| data\.tab === 'results'\}/);
  assert.match(css,/\.team-heading:not\(\.matches-reference-heading\)/);
  assert.match(css,/\.team-heading-photo img\[data-fallback="true"\]/);
  assert.match(css,/@media\(max-width:767px\)/);
  assert.match(css,/overflow-x:auto/);
  assert.match(css,/focus-visible/);
  assert.match(team,/\['overview','Η ΟΜΑΔΑ'\]/);
  assert.match(team,/\['venues','ΓΗΠΕΔΑ'\]/);
});

test('real Chromium screenshots measure the Team masthead and its tab bar',()=>{
  assert.match(browser,/teamHeading:rect\('\.team-heading:not\(\.matches-reference-heading\)'\)/);
  assert.match(browser,/teamCover:rect\('\[data-testid="team-heading-photo"\]'\)/);
  assert.match(browser,/teamTabs:rect\('\.team-heading \.team-tabs'\)/);
  assert.match(browser,/route\.name==='first-team'/);
  assert.match(browser,/first-team: photographic club hero and working tabs/);
});
