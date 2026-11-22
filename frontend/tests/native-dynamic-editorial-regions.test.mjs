import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {nativeEditorialBlocks} from '../src/lib/public/native-editorial-regions.ts';
import {isHomepageEditorialSlug,nativeEditorialOwnerAllowed,withWebsitePages,
  } from '../src/lib/public/website-pages.ts';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const api=read('backend/src/LefteriaFc.Api/PublicContent/WebsitePageEndpoints.cs');
const admin=read('frontend/src/routes/admin/website-pages/+page.svelte');
const canvas=read('frontend/src/lib/admin/WebsiteVisualCanvas.svelte');
const team=read('frontend/src/routes/team/+page.svelte');
const teamLoader=read('frontend/src/routes/team/+page.server.ts');
const news=read('frontend/src/routes/news/+page.svelte');
const newsLoader=read('frontend/src/routes/news/+page.server.ts');
const sponsors=read('frontend/src/lib/public/PublicCatalogList.svelte');
const sponsorsLoader=read('frontend/src/routes/sponsors/+page.server.ts');
const region='canonical-team-roster-sections';
const page=(slug=region,owner='lefteria_fc')=>({
  organization_id:owner,slug,published_at:'2026-11-22T08:00:00Z',
  page:{title:'Native region',summary:'',seo_title:'',seo_description:'',
    menu_parent:'hidden',menu_order:0,blocks:[
      {type:'heading',text:'Visible headline',url:null},
      {type:'paragraph',text:'Visible explanation',url:null},
      {type:'button',text:'Learn more',url:'/about'},
      {type:'image',text:'Public banner',url:'/media/approved.webp'}
    ]}
});
const firstTeam=['canonical-team-roster-sections','canonical-team-schedule-sections',
  'canonical-team-results-sections','canonical-team-statistics-sections'];
const shared=['canonical-news-sections','canonical-sponsors-sections'];
test('native multi-block regions use only approved published and exact-owner snapshots',()=>{
  const fc=page();
  assert.deepEqual(nativeEditorialBlocks([fc],'lefteria_fc',region),fc.page.blocks);
  assert.deepEqual(nativeEditorialBlocks([fc],'lefteria_fc_academy',region),[]);
  assert.deepEqual(nativeEditorialBlocks([{...fc,published_at:null}],'lefteria_fc',region),[]);
  assert.deepEqual(nativeEditorialBlocks([{...fc,page:{...fc.page,menu_parent:'root'}}],'lefteria_fc',region),[]);
  assert.deepEqual(nativeEditorialBlocks([{...fc,page:{...fc.page,menu_order:2}}],'lefteria_fc',region),[]);
  assert.deepEqual(nativeEditorialBlocks([{...fc,page:{...fc.page,blocks:Array(13).fill(fc.page.blocks[0])}}],'lefteria_fc',region),[]);
  assert.deepEqual(nativeEditorialBlocks([{...fc,page:{...fc.page,blocks:[{type:'button',text:'Unsafe',url:'javascript:alert(1)'}]}}],'lefteria_fc',region),[]);
  assert.deepEqual(nativeEditorialBlocks([{...fc,page:{...fc.page,blocks:[{type:'image',text:'Unsafe',url:'//other.example/x'}]}}],'lefteria_fc',region),[]);
  assert.deepEqual(nativeEditorialBlocks([{...fc,page:{...fc.page,blocks:[{type:'heading',text:'Heading with unsafe URL',url:'/other'}]}}],'lefteria_fc',region),[]);
});
test('native public routing and owner boundaries prevent cross-team content borrowing',()=>{
  for(const slug of [...firstTeam,...shared]){
    assert.equal(isHomepageEditorialSlug(slug),true);
    assert.deepEqual(withWebsitePages([], [page(slug)],'lefteria_fc'),[]);
  }
  for(const slug of firstTeam)assert.equal(nativeEditorialOwnerAllowed(slug,'lefteria_fc_academy'),false);
  for(const slug of shared) {
    assert.equal(nativeEditorialOwnerAllowed(slug,'lefteria_fc_academy'),true);
    const academy=page(slug,'lefteria_fc_academy');
    assert.deepEqual(nativeEditorialBlocks([academy],'lefteria_fc_academy',slug),academy.page.blocks);
    assert.deepEqual(nativeEditorialBlocks([academy],'lefteria_fc',slug),[]);
  }
  assert.equal(nativeEditorialOwnerAllowed('unapproved-region','lefteria_fc'),false);
  assert.match(api,/ValidateNativeEditorialRegions\(owner, body\.Slug, body\.Page\)/);
  assert.match(api,/ValidateNativeEditorialRegions\(owner, slug, body\.Page\)/);
  assert.match(api,/firstTeam && owner != "lefteria_fc"/);
  assert.match(api,/content\.Blocks\.Count is < 1 or > 12/);
});
test('First Team tabs retain real player, match and statistics projections',()=>{
  for(const key of ['roster','schedule','results','statistics'])assert.match(teamLoader,new RegExp('canonical-team-'+key+'-sections'));
  assert.match(teamLoader,/loadTeamPage\(url\)/);
  assert.match(teamLoader,/nativeEditorialBlocks\(pages,'lefteria_fc',region\)/);
  assert.match(team,/data-testid="team-cms-region"/);
  assert.match(team,/WebsitePageBlocks pageLayout=\{data\.editorialLayout\} blocks=\{data\.editorialBlocks \?\? \[\]\}/);
  for(const required of ['selectedTeamId','firstTeamStandings','team-scoped-standings','team-heading-photo',
    'PublicTeamMatchOverview','PublicMatchScheduleTable'])assert.ok(team.includes(required),required);
  assert.ok(compile(team,{filename:'team/+page.svelte',generate:'server'}).js.code.length>100);
});
test('News and Sponsors editorial blocks never replace article cards, sponsor cards and filters',()=>{
  assert.match(newsLoader,/loadPublicNews\(url\)/);
  assert.match(newsLoader,/readWebsitePages\(news\.organization\)/);
  assert.match(news,/data-testid="news-cms-region"/);
  assert.match(news,/NewsCards articles=\{data\.news\.items\}/);
  assert.match(news,/public-pagination/);
  assert.match(sponsorsLoader,/loadPublicCatalog\(url,'sponsors'\)/);
  assert.match(sponsorsLoader,/readWebsitePages\(catalog\.organization\)/);
  assert.match(sponsors,/data-testid="sponsors-cms-region"/);
  assert.match(sponsors,/data\.kind==='sponsors'/);
  for(const word of ['data.cards','data.scope']) assert.ok(sponsors.includes(word));
  assert.ok(compile(news,{filename:'news/+page.svelte',generate:'server'}).js.code.length>100);
  assert.ok(compile(sponsors,{filename:'PublicCatalogList.svelte',generate:'server'}).js.code.length>100);
});
test('admin opens native editable blocks with role check, 12-block cap and separate approval',()=>{
  assert.match(admin,/function openNativeRegion/);
  assert.match(admin,/nativeEditorialOwnerAllowed\(target,organization\)/);
  assert.match(admin,/data-testid="cms-native-regions"/);
  assert.match(admin,/maxBlocks = \$derived\(isNativeRegion \? 12 : 40\)/);
  assert.match(admin,/draft\.blocks\.length>12/);
  assert.match(admin,/maxBlocks=\{maxBlocks\}/);
  assert.match(admin,/expected_version:selected\?\.version,page:draft/);
  assert.match(admin,/expected_version:selected\.version,publish/);
  assert.match(canvas,/maxBlocks=40/);
  assert.ok(compile(admin,{filename:'admin/website-pages/+page.svelte',generate:'server'}).js.code.length>100);
  assert.ok(compile(canvas,{filename:'WebsiteVisualCanvas.svelte',generate:'server'}).js.code.length>100);
});
