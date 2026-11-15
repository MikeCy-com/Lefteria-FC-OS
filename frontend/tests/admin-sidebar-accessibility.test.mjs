import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = (path) => readFileSync(new URL('../src/lib/admin/' + path, import.meta.url), 'utf8');
const marker = '/* Admin navigation readability and responsive accessibility */';
const css = read('admin.css');
const readableNavigation = css.split(marker)[1];

test('admin sidebar navigation ends with an accessible typography layer', () => {
  assert.ok(readableNavigation, 'readability overrides must be present after existing 4K/mobile rules');
  assert.match(readableNavigation, /\.admin-app \.admin-sidebar a \{[\s\S]*?min-height:48px;[\s\S]*?font-size:16px;/);
  assert.match(readableNavigation, /\.admin-app \.admin-sidebar \.admin-nav-section \{[\s\S]*?font-size:17px;/);
  assert.match(readableNavigation, /\.admin-app \.admin-sidebar \.admin-nav-tool \{[\s\S]*?font-size:16px;/);
  assert.match(readableNavigation, /\.admin-app \.admin-sidebar \.admin-nav-label \{[\s\S]*?font-size:13px;/);
  assert.match(readableNavigation, /white-space:normal;/);
  assert.match(readableNavigation, /overflow-wrap:break-word;/);
});

test('admin navigation remains usable on high-density, narrow and mobile layouts', () => {
  assert.match(readableNavigation, /@media \(min-width:2560px\)/);
  assert.match(readableNavigation, /@media \(min-width:721px\) and \(max-width:920px\)/);
  assert.match(readableNavigation, /@media \(max-width:720px\)/);
  assert.match(readableNavigation, /scroll-snap-type:x proximity;/);
  assert.match(readableNavigation, /overflow-x:auto;/);
  assert.match(readableNavigation, /\.admin-app \.admin-sidebar a,[\s\S]*?\.admin-app \.admin-sidebar \.admin-nav-tool \{[\s\S]*?font-size:16px;/);
  assert.match(readableNavigation, /scrollbar-width:thin;/);
});

test('admin navigation retains keyboard focus, selected page contrast and permission gates', () => {
  assert.match(readableNavigation, /\.admin-app \.admin-sidebar a:focus-visible \{[\s\S]*?outline:3px solid #ffca65;/);
  assert.match(readableNavigation, /\[aria-current='page'\]/);
  assert.match(readableNavigation, /@media \(prefers-contrast:more\)/);
  const frame = read('AdminFrame.svelte');
  assert.match(frame, /aria-label="Διαχείριση συλλόγου"/);
  assert.match(frame, /href="#admin-main"/);
  assert.match(frame, /aria-current=\{active === 'account'/);
  assert.match(frame, /adminSectionsFor\(user\)/);
  assert.match(frame, /canReadClub\(user\)/);
  assert.match(frame, /canViewFirstTeamTrials\(user\)/);
  assert.match(frame, /data-testid="admin-news-link"/);
  assert.match(frame, /data-testid="admin-announcements-link"/);
});
