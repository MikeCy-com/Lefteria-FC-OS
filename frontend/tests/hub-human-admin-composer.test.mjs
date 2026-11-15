import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const page=read('frontend/src/routes/admin/hub/+page.svelte');
const backend=read('backend/src/LefteriaFc.Api/Hub/AdminHubEndpoints.cs');

test('human draft composer uses existing authenticated, organization-scoped API only',()=>{
  assert.match(page,/async function createHumanDraft\(\)/);
  assert.match(page,/hasAdminPermission\(\$auth\.session\?\.user\.permissions\?\?\[\],'news:write'\)/);
  assert.match(page,/if\(!owner\|\|!canWrite\|\|busy\)return/);
  assert.match(page,/fetch\('\/api\/admin\/hub\/editorial\?organization_id='\+owner/);
  assert.match(page,/method:'POST',credentials:'same-origin',cache:'no-store'/);
  assert.match(page,/'X-Lefteria-Admin':'1'/);
  assert.match(page,/audience_parent:humanParent,audience_player:humanPlayer/);
  assert.match(page,/age_min:humanAgeRestricted\?humanAgeMin:null,age_max:humanAgeRestricted\?humanAgeMax:null/);
  assert.match(page,/response\.status!==201/);
  assert.match(page,/\.status!=='ai_draft'/);
  assert.match(page,/auth\.invalidateSession\(\)/);
  assert.match(page,/auth\.bootstrap\(\)/);
  assert.match(backend,/MapPost\("\/api\/admin\/hub\/editorial"/);
  assert.match(backend,/hub\.create_admin_draft\(/);
});

test('composer protects child permissions and editorial separation',()=>{
  assert.match(page,/humanParent\|\|humanPlayer/);
  assert.match(page,/humanAgeMin<4\|\|humanAgeMax>99/);
  assert.match(page,/statusFilter='ai_draft';reload\+\+/);
  assert.match(page,/Το ανθρώπινο πρόχειρο αποθηκεύτηκε ιδιωτικά/);
  assert.match(page,/όχι αλλαγή ρόλου ή εξουσιοδότηση κηδεμόνα/);
  assert.match(page,/δοκιμαστική ροή Payabl/);
  assert.match(page,/Η επισκόπηση συνδρομών είναι διαθέσιμη μόνο σε Super Admin/);
  assert.doesNotMatch(page,/{@html|innerHTML|localStorage|sessionStorage/);
  assert.doesNotMatch(page,/\/api\/mobile\/hub\/fan-subscription\/checkout/);
});

test('editorial status picker only filters the already verified selected-organization response',()=>{
  assert.match(page,/validHubList\(data,owner\)/);
  assert.match(page,/items=data\.items/);
  assert.match(page,/filteredItems=\$derived\(items\.filter/);
  assert.match(page,/\{#each filteredItems as item \(item\.id\)\}/);
  assert.match(page,/\{statusLabels\[item\.status\]\}/);
});

test('existing protected revision, image quarantine and rejection endpoints are reachable from admin workflow',()=>{
  assert.match(page,/async function reviseDraft\(\)/);
  assert.match(page,/async function quarantineImage\(\)/);
  assert.match(page,/async function returnRejectedForRevision\(\)/);
  assert.match(page,/async function rejectImage\(mediaId:string\)/);
  assert.match(page,/\/revise\?organization_id=/);
  assert.match(page,/\/images\/quarantine\?organization_id=/);
  assert.match(page,/\/return-for-revision\?organization_id=/);
  assert.match(page,/\/reject\?organization_id=/);
  assert.match(page,/file\.type!=='image\/jpeg'/);
  assert.match(page,/file\.size>1048576/);
  assert.match(page,/base64:btoa\(chunks\.join\(''\)\),alt_text:alt/);
  assert.match(page,/source\.status!=='ai_draft'/);
  assert.match(page,/source\.status!=='review_required'/);
  assert.match(page,/validHubTransition\(result,owner,source\.id,source\.version,'ai_draft'\)/);
  assert.match(page,/validHubTransition\(result,owner,source\.id,source\.version,'review_required'\)/);
  assert.match(backend,/MapPost\("\/api\/admin\/hub\/editorial\/\{id:guid\}\/revise"/);
  assert.match(backend,/MapPost\("\/api\/admin\/hub\/editorial\/\{id:guid\}\/images\/quarantine"/);
  assert.match(backend,/MapPost\("\/api\/admin\/hub\/editorial\/\{id:guid\}\/return-for-revision"/);
  assert.match(backend,/MapPost\("\/api\/admin\/hub\/editorial\/\{id:guid\}\/images\/\{mediaId:guid\}\/reject"/);
});
