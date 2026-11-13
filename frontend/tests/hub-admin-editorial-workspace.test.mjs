import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const page=read('frontend/src/routes/admin/hub/+page.svelte');
const policy=read('frontend/src/lib/admin/hub-editorial.ts');
const catalog=read('frontend/src/lib/admin/module-catalog.ts');
const workspace=read('frontend/src/lib/admin/AdminWorkspace.svelte');
test('HUB admin workspace remains session and manager-authorized',()=>{
 assert.match(page,/ADMIN_AUTH_CONTEXT/);
 assert.match(page,/hasAdminPermission\(\$auth\.session\.user\.permissions,'news:read'\)/);
 assert.match(page,/\['manager','super_admin','editor'\]/);
 assert.match(page,/const manager=\$derived/);
 assert.match(page,/if\(!source\|\|!owner\|\|source\.organization_id!==owner\|\|!manager\|\|busy\)return/);
 assert.match(page,/credentials:'same-origin',cache:'no-store'/);
 assert.match(page,/'X-Lefteria-Admin':'1'/);
 assert.match(page,/auth\.invalidateSession\(\)/);
 assert.match(page,/auth\.bootstrap\(\)/);
});
test('AI brief is always separate from human approval and protected rendering',()=>{
 assert.match(page,/\/api\/admin\/hub\/ai\/education-briefs/);
 assert.match(page,/age_min:ageMin,age_max:ageMax/);
 assert.match(page,/\/api\/admin\/hub\/editorial/);
 assert.match(page,/runAction\('text-review','review_required','passed'\)/);
 assert.match(page,/runAction\('visual-approve','review_required'\)/);
 assert.match(page,/runAction\('final-approve','approved'\)/);
 assert.match(page,/runAction\('publish','published'\)/);
 assert.match(page,/inspected=inspected\.filter\(id=>id!==media\.id\)/);
 assert.match(page,/onload=\{\(\)=>\{inspected=/);
 assert.doesNotMatch(page,/{@html|innerHTML/);
});
test('editorial DTO validator rejects cross-organization details and unverified transitions',()=>{
 assert.match(policy,/d\.organization_id===owner/);
 assert.match(policy,/d\.items\.every/);
 assert.match(policy,/d\.visuals\.every/);
 assert.match(policy,/d\.version===version\+1/);
 assert.match(catalog,/hub_editorial.*status: 'live', href: '\/admin\/hub'/);
 assert.match(workspace,/data-testid="admin-hub-editorial-module"/);
});
