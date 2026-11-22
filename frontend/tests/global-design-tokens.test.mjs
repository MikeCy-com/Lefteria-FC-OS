import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {DEFAULT_GLOBAL_DESIGN,GLOBAL_DESIGN_SLUG,TEXT_ROLES,parseGlobalDesign,
  publishedGlobalDesign,globalDesignCssVars} from '../src/lib/public/global-design.ts';
import {isHomepageEditorialSlug,withWebsitePages} from '../src/lib/public/website-pages.ts';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const api=read('backend/src/LefteriaFc.Api/PublicContent/WebsitePageEndpoints.cs');
const admin=read('frontend/src/routes/admin/website-pages/+page.svelte');
const editor=read('frontend/src/lib/admin/WebsiteGlobalDesignEditor.svelte');
const shell=read('frontend/src/lib/public/PublicShell.svelte');
const css=read('frontend/src/lib/public/global-design.css');
const row=(organization_id='lefteria_fc')=>({
  organization_id,slug:GLOBAL_DESIGN_SLUG,published_at:'2026-11-22T11:00:00Z',
  page:{title:'Global design',summary:'',seo_title:'',seo_description:'',
    menu_parent:'hidden',menu_order:0,
    blocks:[{type:'paragraph',text:JSON.stringify(DEFAULT_GLOBAL_DESIGN),url:null}]}
});
test('global theme requires all nine HTML text roles with exact safe values',()=>{
  assert.equal(TEXT_ROLES.length,12);
  assert.deepEqual(parseGlobalDesign(JSON.stringify(DEFAULT_GLOBAL_DESIGN)),DEFAULT_GLOBAL_DESIGN);
  const bad=structuredClone(DEFAULT_GLOBAL_DESIGN);
  bad.text.h1.desktop=10000;
  assert.equal(parseGlobalDesign(JSON.stringify(bad)),null);
  bad.text.h1.desktop=48;
  bad.text.h2.family='url(https://evil)' ;
  assert.equal(parseGlobalDesign(JSON.stringify(bad)),null);
  bad.text.h2.family='condensed';
  bad.colors.accent='red; background:blue';
  assert.equal(parseGlobalDesign(JSON.stringify(bad)),null);
  bad.colors.accent='#f6bb38';
  bad.text.h3.extra='unexpected';
  assert.equal(parseGlobalDesign(JSON.stringify(bad)),null);
  const inaccessible=structuredClone(DEFAULT_GLOBAL_DESIGN);
  inaccessible.colors.foreground='#101010';
  assert.equal(parseGlobalDesign(JSON.stringify(inaccessible)),null,'body text must remain readable');
  inaccessible.colors.foreground='#ffffff';
  inaccessible.colors.link='#090909';
  assert.equal(parseGlobalDesign(JSON.stringify(inaccessible)),null,'links must remain readable');
  assert.match(api,/ColorContrast\(bg, colors\.GetProperty\("foreground"\)/);
  assert.equal(parseGlobalDesign('{invalid}'),null);
  assert.equal(parseGlobalDesign('a'.repeat(5001)),null);
});
test('themes remain hidden tenant-specific published-only records, not pages',()=>{
  const fc=row();
  assert.deepEqual(publishedGlobalDesign([fc],'lefteria_fc'),DEFAULT_GLOBAL_DESIGN);
  assert.equal(publishedGlobalDesign([fc],'lefteria_fc_academy'),null);
  assert.equal(publishedGlobalDesign([{...fc,published_at:null}],'lefteria_fc'),null);
  assert.equal(publishedGlobalDesign([{...fc,page:{...fc.page,menu_parent:'root'}}],'lefteria_fc'),null);
  const academy=row('lefteria_fc_academy');
  assert.deepEqual(publishedGlobalDesign([academy],'lefteria_fc_academy'),DEFAULT_GLOBAL_DESIGN);
  assert.equal(isHomepageEditorialSlug(GLOBAL_DESIGN_SLUG),true);
  assert.deepEqual(withWebsitePages([], [fc], 'lefteria_fc'),[]);
});
test('token renderer emits only curated CSS vars and responsive heading selectors',()=>{
  assert.equal(globalDesignCssVars(null),'');
  const styles=globalDesignCssVars(DEFAULT_GLOBAL_DESIGN);
  for(const role of TEXT_ROLES) {
    assert.ok(styles.includes('--lf-'+role+'-desktop:'));
    assert.ok(styles.includes('--lf-'+role+'-mobile:'));
  }
  assert.match(styles,/--lf-color-accent:#f6bb38/);
  for(const token of ['surface','muted','border','link'])assert.ok(styles.includes('--lf-color-'+token+':'));
  for(const role of ['caption','label','card'])assert.match(css,new RegExp('--lf-'+role+'-mobile'));
  assert.match(shell,/publishedGlobalDesign\(page\.data\?\.websitePages\?\.\[organization\]/);
  assert.match(shell,/data-global-design=\{globalTheme/);
  for(const heading of ['h1','h2','h3','h4','h5','h6','p']) {
    assert.ok(css.includes(' .public-main '+heading+'{'));
    assert.ok(css.includes('--lf-'+heading+'-mobile'));
  }
  assert.match(css,/@media\(max-width:767px\)/);
});
test('design settings use existing private draft approval with exact API validation',()=>{
  assert.match(api,/ValidateGlobalDesign\(body\.Slug, body\.Page\)/);
  assert.match(api,/ValidateGlobalDesign\(slug, body\.Page\)/);
  assert.match(api,/styles\.EnumerateObject\(\)\.Count\(\) != 12/);
  assert.match(api,/content\.MenuParent != "hidden"/);
  assert.match(admin,/data-testid="edit-global-design"/);
  assert.match(admin,/function openGlobalDesign/);
  assert.match(admin,/parseGlobalDesign\(draft\.blocks\[0\]\.text\)/);
  assert.match(admin,/WebsiteGlobalDesignEditor value=\{draft\.blocks\[0\]/);
  assert.match(admin,/expected_version:selected\?\.version,page:draft/);
  assert.match(admin,/expected_version:selected\.version,publish/);
  assert.match(editor,/data-testid="global-design-editor"/);
  assert.ok(compile(admin,{filename:'admin/website-pages/+page.svelte',generate:'server'}).js.code.length>100);
  assert.ok(compile(editor,{filename:'WebsiteGlobalDesignEditor.svelte',generate:'server'}).js.code.length>100);
  assert.ok(compile(shell,{filename:'PublicShell.svelte',generate:'server'}).js.code.length>100);
});
