import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {websiteMenuParentAllowed,withWebsitePages} from '../src/lib/public/website-pages.ts';
const source=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const page=(org,parent)=>({organization_id:org,slug:'academy-information',published_at:'2026-11-15T00:00:00Z',
  page:{title:'Title',summary:'',seo_title:'',seo_description:'',menu_parent:parent,menu_order:0,
    blocks:[{type:'paragraph',text:'Content',url:null}]}});
const nav=[
  {id:'team',label:'Team',icon:'info',items:[['team-home','Team','/team']]},
  {id:'academy',label:'Academy',icon:'info',items:[['academy-home','Academy','/academy']]},
  {id:'matches',label:'Matches',icon:'info',items:[['matches-home','Matches','/team?tab=schedule']]}
];
test('CMS menu destinations reject cross-owner team and match parents',()=>{
  assert.equal(websiteMenuParentAllowed('lefteria_fc_academy','team'),false);
  assert.equal(websiteMenuParentAllowed('lefteria_fc_academy','matches'),false);
  assert.equal(websiteMenuParentAllowed('lefteria_fc','academy'),false);
  for(const org of ['lefteria_fc','lefteria_fc_academy']){
    assert.equal(websiteMenuParentAllowed(org,'root'),true);
    assert.equal(websiteMenuParentAllowed(org,'news'),true);
    assert.equal(websiteMenuParentAllowed(org,'hidden'),true);
  }
  assert.equal(websiteMenuParentAllowed('lefteria_fc_academy','academy'),true);
  assert.equal(websiteMenuParentAllowed('lefteria_fc','team'),true);
  assert.equal(websiteMenuParentAllowed('lefteria_fc','matches'),true);
});
test('public navigation never exposes previously published cross-owner CMS entries',()=>{
  const academy=page('lefteria_fc_academy','team');
  const team=page('lefteria_fc','academy');
  assert.deepEqual(withWebsitePages(nav,[academy],'lefteria_fc_academy'),nav);
  assert.deepEqual(withWebsitePages(nav,[team],'lefteria_fc'),nav);
  const academyAllowed=withWebsitePages(nav,[page('lefteria_fc_academy','academy')],'lefteria_fc_academy');
  assert.equal(academyAllowed[1].items.length,2);
  const teamAllowed=withWebsitePages(nav,[page('lefteria_fc','team')],'lefteria_fc');
  assert.equal(teamAllowed[0].items.length,2);
});
test('API enforces same owner guard for writes and admin only shows valid options',()=>{
  const api=source('backend/src/LefteriaFc.Api/PublicContent/WebsitePageEndpoints.cs');
  assert.match(api,/ValidateMenuPlacement\(owner, body.Page\)/);
  assert.match(api,/content.MenuParent is "team" or "matches"/);
  assert.match(api,/content.MenuParent == "academy"/);
  const admin=source('frontend/src/routes/admin/website-pages/+page.svelte');
  assert.match(admin,/websiteMenuParentAllowed\(organization, parent.value\)/);
  assert.match(admin,/visibleParents\.some/);
  assert.match(admin,/each visibleParents as parent/);
});
