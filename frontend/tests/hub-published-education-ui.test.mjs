import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const controller=read('frontend/src/lib/mobile/controller.ts');
const validator=read('frontend/src/lib/mobile/published-education.ts');
const component=read('frontend/src/lib/mobile/components/MobilePublishedHubEducation.svelte');
const route=read('frontend/src/routes/app/[...path]/+page.svelte');
test('real parent/player HUB mounts authenticated approved-content reader, not for fans',()=>{
 assert.match(route,/import MobilePublishedHubEducation from/);
 assert.match(route,/\{#if role === 'parent' \|\| role === 'player'\}\s*<MobilePublishedHubEducation \{controller\} viewerRole=\{role\}/);
 assert.match(component,/getPublishedHubEducation\(organization,0\)/);
 assert.match(component,/onMount\(\(\)=>/);
 assert.match(component,/if\(!alive\|\|current!==request\|\|page\.audience!==viewerRole\)return/);
 assert.match(component,/item\.body/);
 assert.doesNotMatch(component,/innerHTML|{@html/);
});
test('client and API protect from role and same-role identity swaps',()=>{
 assert.match(controller,/state\.user\?\.current_role_available/);
 assert.match(controller,/state\.user\?\.id!==actorId\|\|state\.user\.role!==role/);
 assert.match(controller,/token!==actorToken/);
 assert.match(controller,/revision!==actorRevision/);
 assert.match(validator,/page\.audience!==expected/);
 assert.match(validator,/a\.organization_id===organization&&a\.audience===expected/);
 assert.match(validator,/expected!=='player'\|\|\(a\.age_min===null&&a\.age_max===null\)/);
});
