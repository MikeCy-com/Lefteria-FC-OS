import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=p=>readFileSync(new URL('../src/'+p,import.meta.url),'utf8');
const provider=read('routes/admin/settings/providers/+page.svelte');
const settings=read('routes/admin/settings/+page.svelte');
const api=readFileSync(new URL('../../backend/src/LefteriaFc.Api/WebAdmin/AdminProviderSettingsEndpoints.cs',import.meta.url),'utf8');
const program=readFileSync(new URL('../../backend/src/LefteriaFc.Api/Program.cs',import.meta.url),'utf8');

test('provider and settings pages compile, mirror provider panels and restrict navigation',()=>{
  for(const [name,source] of [['providers',provider],['settings',settings]])
    assert.ok(compile(source,{filename:name+'.svelte',generate:'server'}).js.code.length>100);
  assert.match(settings,/data-testid="admin-settings-provider-link"/);
  assert.match(settings,/role==='super_admin'/);
  assert.match(provider,/data-testid="admin-twilio-settings"/);
  assert.match(provider,/data-testid="admin-payabl-settings"/);
  assert.match(provider,/data-testid="admin-provider-refresh"/);
  assert.match(provider,/data-testid="admin-provider-payabl-finance"/);
  assert.match(provider,/if !isSuperAdmin/);
});

test('provider status is authenticated, Super Admin only, never exposes credential strings',()=>{
  assert.match(program,/app\.MapAdminProviderSettings\(\)/);
  assert.match(api,/RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(api,/auth\.CurrentAsync\(/);
  assert.match(api,/session\.User\.Role!="super_admin"/);
  assert.match(api,/CacheControl="private, no-store"/);
  assert.match(api,/account_sid_configured=sidOk/);
  assert.match(api,/auth_token_configured=tokenOk/);
  assert.match(api,/from_number_configured=fromOk/);
  assert.doesNotMatch(api,/auth_token\s*=\s*twilio\.AuthToken|account_sid\s*=\s*twilio\.AccountSid|from_number\s*=\s*twilio\.FromNumber/);
  assert.doesNotMatch(api,/MapPost|SendOtpAsync|SendAsync/);
});

test('sandbox-only payabl health does not claim credential editing, live captures or SMS tests',()=>{
  assert.match(provider,/\/api\/admin\/finance\/payabl\/status\?organization_id=/);
  assert.match(provider,/environment==='sandbox'/);
  assert.match(provider,/x\.live_transactions===false/);
  assert.match(provider,/editing_available:false/);
  assert.match(provider,/test_send_available:false/);
  assert.doesNotMatch(provider,/method:'POST'|method:"POST"|localStorage|sessionStorage/);
});
