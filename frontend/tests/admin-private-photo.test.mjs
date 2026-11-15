import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');

const page=read('frontend/src/routes/admin/profile/+page.svelte');
const frame=read('frontend/src/lib/admin/AdminFrame.svelte');
const avatar=read('frontend/src/lib/admin/AdminProfilePhoto.svelte');
const api=read('backend/src/LefteriaFc.Api/WebAdmin/AdminProfilePhotoEndpoints.cs');
const guard=read('backend/src/LefteriaFc.Api/WebAdmin/WebAdminRequestGuards.cs');
const boundary=read('ops/postgres/admin-self-photo-boundary.sql');
const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115300000_AdminSelfProfilePhoto.cs');

test('admin profile and account menu compile with actor-bound private avatar and real photo controls',()=>{
  for(const [name,source] of [['admin-profile',page],['admin-frame',frame],['admin-avatar',avatar]]){
    assert.ok(compile(source,{filename:name+'.svelte',generate:'server'}).js.code.length>100);
  }
  assert.match(page,/data-testid="admin-self-photo-form"/);
  assert.match(page,/bind:this=\{photoInput\}/);
  assert.match(page,/form\.append\('file',file,file\.name\)/);
  assert.match(page,/response\.status!==204/);
  assert.match(page,/auth\.invalidateSession\(\)/);
  assert.match(page,/adminPhotoRevision\.update\(/);
  assert.match(page,/user\?\.id!==actor/);
  assert.match(page,/method:'POST'/);
  assert.match(page,/method:'DELETE'/);
  assert.match(frame,/AdminProfilePhoto actorId=\{user\.id\}/);
  assert.match(page,/AdminProfilePhoto actorId=\{user\.id\}/);
  assert.match(avatar,/\{#key actorId\}/);
  assert.match(avatar,/\/api\/admin\/auth\/profile\/photo\?revision=/);
  assert.doesNotMatch(page,/localStorage|sessionStorage|createObjectURL|@html|innerHTML/);
});

test('admin photo endpoints require live authenticated self-session, origin guard and bounded sanitized bytes',()=>{
  assert.match(api,/MapGet\("\/api\/admin\/auth\/profile\/photo"/);
  assert.match(api,/MapPost\("\/api\/admin\/auth\/profile\/photo"/);
  assert.match(api,/MapDelete\("\/api\/admin\/auth\/profile\/photo"/);
  assert.equal((api.match(/RequireAuthorization\(WebAdminServices\.Policy\)/g)||[]).length,3);
  assert.match(api,/service\.CurrentAsync\(cookie,token\)/);
  assert.match(api,/secrets\.TokenDigest\(cookie\)/);
  assert.match(api,/IAvatarImageSanitizer sanitizer/);
  assert.match(api,/CryptographicOperations\.ZeroMemory/);
  assert.match(api,/CacheControl="private, no-store"/);
  assert.match(api,/XContentTypeOptions="nosniff"/);
  assert.match(guard,/TrustedMutation\(context\.Request, options\)/);
  assert.match(guard,/ownPhotoUpload/);
  assert.match(guard,/MobileAvatarLimits\.RequestBytes/);
  assert.doesNotMatch(api,/public\/photo|avatar_url|localStorage|recipient_id/);
});

test('database denies raw private photo access and missing-session read or writes',()=>{
  assert.match(migration,/SECURITY DEFINER/);
  assert.match(migration,/SET search_path=pg_catalog,pg_temp/);
  assert.match(migration,/identity\.admin_profile_photos/);
  assert.match(migration,/identity\.read_admin_own_profile_photo/);
  assert.match(migration,/identity\.write_admin_own_profile_photo/);
  assert.match(boundary,/has_table_privilege/);
  assert.match(boundary,/Anonymous admin photo read was accepted/);
  assert.match(boundary,/Anonymous admin photo deletion was accepted/);
  assert.match(boundary,/Anonymous admin photo upload was accepted/);
});
