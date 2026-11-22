import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const layout=read('frontend/src/routes/admin/+layout.svelte');
const css=read('frontend/src/lib/admin/admin-ui-refresh.css');
const frame=read('frontend/src/lib/admin/AdminFrame.svelte');
const login=read('frontend/src/lib/admin/AdminSignIn.svelte');
const navigation=read('frontend/src/lib/admin/module-catalog.ts');

test('admin UI refresh is isolated, opt-in, and retains original CSS contract',()=>{
  assert.match(layout,/import '\.\.\/\.\.\/lib\/admin\/admin\.css';/);
  assert.match(layout,/import '\.\.\/\.\.\/lib\/admin\/admin-ui-refresh\.css';/);
  assert.match(layout,/class="admin-app" data-ui-refresh="2026"/);
  assert.match(css,/\.admin-app\[data-ui-refresh="2026"\]/);
  assert.match(css,/@media \(max-width: 720px\)/);
  assert.match(css,/@media \(prefers-reduced-motion: reduce\)/);
  assert.match(css,/@media \(prefers-contrast: more\)/);
  assert.doesNotMatch(css,/url\s*\(|background-image\s*:|object-(?:fit|position)\s*:|font-(?:size|family)\s*:|(?:^|[;\s])content\s*:|display\s*:\s*none|visibility\s*:\s*hidden/i);
  assert.doesNotMatch(css,/!important/);
  assert.doesNotMatch(css,/\.public-site\b|\.mobile-shell\b/);
});

test('admin role, session, menu, and photograph owners remain the original components',()=>{
  assert.match(layout,/createAdminAuthController/);
  assert.match(layout,/controller\.bootstrap\(\)/);
  assert.match(frame,/adminSectionsFor\(user\)/);
  assert.match(frame,/data-testid="admin-logout"/);
  assert.match(frame,/data-testid="admin-profile-menu"/);
  assert.match(frame,/href="\/admin\/settings"/);
  assert.match(frame,/src="\/mobile\/club-logo.png"/);
  assert.match(login,/publicImages\.hero/);
  assert.match(login,/data-testid="admin-login-username"/);
  assert.match(login,/data-testid="admin-login-password"/);
  assert.match(login,/data-testid="admin-login-submit"/);
  assert.match(navigation,/hasAdminPermission\(user\.permissions/);
  assert.match(navigation,/item\.status/);
});
