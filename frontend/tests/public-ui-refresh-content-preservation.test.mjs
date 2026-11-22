import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = path => readFileSync(new URL('../../' + path, import.meta.url), 'utf8');
const shell = read('frontend/src/lib/public/PublicShell.svelte');
const css = read('frontend/src/lib/public/ui-refresh.css');
const home = read('frontend/src/routes/+page.svelte');
const hero = read('frontend/src/lib/public/HomeHero.svelte');
const nav = read('frontend/src/lib/public/PublicNavigation.svelte');
const footer = read('frontend/src/lib/public/PublicFooter.svelte');

test('UI refresh is opt-in, public-only and leaves media, words and dimensions to existing components', () => {
  assert.match(shell, /import '\.\/ui-refresh\.css';/);
  assert.match(shell, /class="public-site" lang="el" data-ui-refresh="2026"/);
  assert.match(css, /\.public-site\[data-ui-refresh="2026"\]/);
  assert.match(css, /@media \(max-width: 767px\)/);
  assert.match(css, /@media \(prefers-reduced-motion: reduce\)/);
  assert.match(css, /@media \(prefers-contrast: more\)/);

  // A stylesheet-only refresh must never replace photos, cover images or
  // copy, hide controls, or reset previously approved type sizes/faces.
  assert.doesNotMatch(css, /url\s*\(|background-image\s*:|object-(?:fit|position)\s*:|font-size\s*:|font-family\s*:|(?:^|[;\s])content\s*:|display\s*:\s*none|visibility\s*:\s*hidden/i);
  assert.doesNotMatch(css, /(^|[\s;{}])!important\b/);
});

test('existing homepage, fixture, navigation and footer remain the content owners', () => {
  for (const expression of [
    /<HomeHero\b/, /<HomeDestinations\b/, /<HomeNews\b/,
    /<Fixtures\b/, /<HomeAcademySpotlight\b/, /<SidelineBanner\b/
  ]) assert.match(home, expression);
  assert.match(hero, /firstTeamHeroBanner\(site\?\.teams\)/);
  assert.match(hero, /nextPublicFixture\(fixtureRows, now\)/);
  assert.match(hero, /fixturePublicLink\(fixture\)/);
  assert.match(nav, /withWebsitePages\(publicNavigation, publishedPages, organization\)/);
  assert.match(nav, /<SiteSearch\b/);
  assert.match(nav, /href="\/app\/login"/);
  assert.match(nav, /href="\/cart"/);
  assert.match(footer, /const isAcademy = \$derived/);
  assert.match(footer, /club\?\.social_links/);
  assert.match(shell, /<WebsitePageBlocks\b/);
});


test('visual refresh cannot disable interaction or conceal existing controls', () => {
  // Presentation changes must not turn working menus, forms or links into
  // visually present but unusable or invisible elements.
  assert.doesNotMatch(css, /pointer-events\s*:\s*none\b/i);
  assert.doesNotMatch(css, /user-select\s*:\s*none\b/i);
  assert.doesNotMatch(css, /opacity\s*:\s*0(?:\.0+)?\s*(?:;|})/i);
  assert.doesNotMatch(css, /position\s*:\s*fixed\b/i);
  assert.doesNotMatch(css, /z-index\s*:/i);
});
