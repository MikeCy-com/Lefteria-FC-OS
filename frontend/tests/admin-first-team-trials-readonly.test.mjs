import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const read=(path)=>readFileSync(new URL('../src/'+path,import.meta.url),'utf8');
const page=read('routes/admin/trials/+page.svelte');
const access=read('lib/admin/trials-access.ts');
const frame=read('lib/admin/AdminFrame.svelte');
const workspace=read('lib/admin/AdminWorkspace.svelte');

test('admin first-team trials panel remains inside authenticated FC scope',()=>{
  assert.ok(page.includes("$auth.status === 'authenticated' && $auth.session"));
  assert.ok(page.includes('canViewFirstTeamTrials($auth.session.user)'));
  assert.ok(page.includes('data-testid="admin-trials-denied"'));
  assert.ok(page.includes('AdminSessionStatus'));
  assert.ok(page.includes('AdminFrame'));
  assert.ok(access.includes("org.id === 'lefteria_fc'"));
  assert.ok(access.includes("'settings:read'"));
  assert.ok(access.includes("'marketing:read'"));
  assert.ok(frame.includes('data-testid="admin-trials-link"'));
  assert.ok(workspace.includes('data-testid="admin-trials-module"'));
  assert.ok(frame.includes("active === 'trials'"));
});

test('admin first-team trial status is public projection only and never collects applicant data',()=>{
  assert.ok(page.includes("fetch('/api/trials/settings'"));
  assert.ok(page.includes("credentials: 'omit'"));
  assert.ok(page.includes("cache: 'no-store'"));
  assert.ok(page.includes('result.open === false'));
  assert.ok(page.includes('data-testid="admin-trials-closed"'));
  assert.ok(page.includes('data-testid="admin-trials-unavailable"'));
  assert.ok(page.includes('href="/trials"'));
  assert.doesNotMatch(page,/<form\b|<input\b|<textarea\b|MapPost|fetch\([^\n]*method:\s*'POST'|applicant_id|date_of_birth|medical|height_cm|weight_kg/i);
  assert.doesNotMatch(access,/^\s*export function .*Write/i);
});
