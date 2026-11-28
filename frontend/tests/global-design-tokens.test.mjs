import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {DEFAULT_GLOBAL_DESIGN,GLOBAL_DESIGN_SLUG,TEXT_ROLES,parseGlobalDesign,
  publishedGlobalDesign,globalDesignCssVars,parseGlobalDesignDraft,globalDesignContrastChecks} from '../src/lib/public/global-design.ts';
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
  const unreadableSurface=structuredClone(DEFAULT_GLOBAL_DESIGN);
  unreadableSurface.colors.surface='#ffffff';
  assert.equal(parseGlobalDesign(JSON.stringify(unreadableSurface)),null,'surface must preserve readable foreground and links');
  unreadableSurface.colors.surface='#171717';
  unreadableSurface.colors.link='#171717';
  assert.equal(parseGlobalDesign(JSON.stringify(unreadableSurface)),null,'surface links must remain readable');
  unreadableSurface.colors.link='#f6bb38';
  unreadableSurface.colors.muted='#171717';
  assert.equal(parseGlobalDesign(JSON.stringify(unreadableSurface)),null,'surface muted labels must remain readable');
  assert.match(api,/ColorContrast\(surface, colors\.GetProperty\("foreground"\)/);
  assert.match(api,/ColorContrast\(surface, colors\.GetProperty\("muted"\)/);
  assert.match(api,/ColorContrast\(surface, colors\.GetProperty\("link"\)/);
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

test('published WYSIWYG layout choices are typed, strict, organization-scoped and legacy compatible',()=>{
  const oldTheme={text:DEFAULT_GLOBAL_DESIGN.text,colors:DEFAULT_GLOBAL_DESIGN.colors};
  assert.deepEqual(parseGlobalDesign(JSON.stringify(oldTheme)),oldTheme,'Old published themes must continue working');
  assert.match(globalDesignCssVars(oldTheme),/--lf-layout-motto-height:120px/);
  const changed=structuredClone(DEFAULT_GLOBAL_DESIGN);
  changed.layout.gutter=64;
  changed.layout.card_gap=40;
  changed.layout.card_radius=24;
  changed.layout.button_radius=16;
  changed.layout.motto_height=360;
  assert.deepEqual(parseGlobalDesign(JSON.stringify(changed)),changed);
  const result=globalDesignCssVars(changed);
  for(const token of [
    '--lf-layout-gutter:64px','--lf-layout-card-gap:40px',
    '--lf-layout-card-radius:24px','--lf-layout-button-radius:16px',
    '--lf-layout-motto-height:360px'
  ])assert.ok(result.includes(token),token);
  for(const [key,value] of [
    ['gutter',999],['section_gap',500],['card_gap',-1],
    ['card_radius',3],['button_radius','999px'],['motto_height',999]
  ]) {
    const invalid=structuredClone(changed);invalid.layout[key]=value;
    assert.equal(parseGlobalDesign(JSON.stringify(invalid)),null,key);
  }
  const injected=structuredClone(changed);
  injected.layout['css']='display:none';
  assert.equal(parseGlobalDesign(JSON.stringify(injected)),null);
  assert.match(api,/layout\.EnumerateObject\(\)\.Count\(\) != 6/);
  assert.match(api,/root\.TryGetProperty\("layout", out var layout\)/);
  assert.match(api,/item\.Choices\.Contains\(number\)/);
  assert.match(editor,/data-testid="global-design-layout-controls"/);
  assert.match(editor,/updateLayout\(key as keyof GlobalDesignLayout/);
  assert.match(editor,/--preview-h1-mobile/);
  assert.doesNotMatch(editor,/clamp\(18px,8vw,36px\)!important/);
});

test('redesign consumes the published palette and layout instead of hardcoded values',()=>{
  const skin=read('frontend/src/lib/public/ui-refresh.css');
  const palette=read('frontend/src/lib/public/global-design.css');
  for(const key of [
    'bg','text','accent','surface','muted','border','link'
  ]) assert.ok((skin+palette).includes('--lf-color-'+key),'not wired: '+key);
  for(const key of [
    'gutter','section-gap','card-gap','card-radius','button-radius','motto-height'
  ]) assert.ok(skin.includes('--lf-layout-'+key),'layout not wired: '+key);
  assert.match(palette,/--public-gold:var\(--lf-color-accent\)/);
  assert.match(palette,/--public-panel:var\(--lf-color-surface\)/);
  assert.doesNotMatch(skin,/rgb\(246[, ]+187[, ]+56/i);
  assert.doesNotMatch(skin,/(?:background|border-color|color)\s*:\s*#[a-f0-9]{3,8}\b/i);
  // Native CMS per-block WYSIWYG spacing must not be overwritten by new skin.
  assert.doesNotMatch(skin,/\.website-block-wrap\b|\.website-blocks\b|--cms-(?:gap|side|max-width|mt|mb)/);
  assert.match(read('frontend/src/lib/public/WebsitePageBlocks.svelte'),/--cms-gap:\$\{dimension\('gap','page-gap'\)\}/);
});

test('global editor now controls default page width, padding and top/bottom margins',()=>{
  const theme=structuredClone(DEFAULT_GLOBAL_DESIGN);
  theme.layout.page_max_width=1440;
  theme.layout.page_gap=48;
  theme.layout.page_side_padding=32;
  theme.layout.page_margin_top=64;
  theme.layout.page_margin_bottom=80;
  assert.deepEqual(parseGlobalDesign(JSON.stringify(theme)),theme);
  const vars=globalDesignCssVars(theme);
  for(const pair of [
    '--lf-layout-page-max-width:1440px',
    '--lf-layout-page-gap:48px',
    '--lf-layout-page-side-padding:32px',
    '--lf-layout-page-margin-top:64px',
    '--lf-layout-page-margin-bottom:80px'
  ])assert.ok(vars.includes(pair),pair);
  for(const key of ['page_max_width','page_gap','page_side_padding','page_margin_top','page_margin_bottom']){
    const invalid=structuredClone(theme); invalid.layout[key]=999;
    assert.equal(parseGlobalDesign(JSON.stringify(invalid)),null,key+' must be allowlisted');
    assert.ok(editor.includes(key),key+' must appear in the WYSIWYG editor');
    assert.ok(api.includes('"'+key+'"'),key+' must be validated by server');
  }
  const oldSix={...theme.layout};
  for(const key of ['page_max_width','page_gap','page_side_padding','page_margin_top','page_margin_bottom'])delete oldSix[key];
  assert.ok(parseGlobalDesign(JSON.stringify({...theme,layout:oldSix})),'6-option published layouts remain valid');
});

test('safe intermediate palette drafts preserve all edits but cannot be published',()=>{
 const draft=structuredClone(DEFAULT_GLOBAL_DESIGN);
 draft.text.h1.desktop=61;draft.layout.page_gap=48;
 draft.colors.background='#ffffff';
 assert.deepEqual(parseGlobalDesignDraft(JSON.stringify(draft)),draft);
 assert.equal(parseGlobalDesign(JSON.stringify(draft)),null);
 assert.ok(globalDesignContrastChecks(draft).some(check=>!check.passes));
 draft.colors.background=DEFAULT_GLOBAL_DESIGN.colors.background;
 assert.deepEqual(parseGlobalDesign(JSON.stringify(draft)),draft);
 draft.colors.background='red;display:none';
 assert.equal(parseGlobalDesignDraft(JSON.stringify(draft)),null);
});
test('editor preserves draft palette and expands legacy layout keys before editing',()=>{
 assert.match(editor,/parseGlobalDesignDraft\(value\)/);
 assert.ok(editor.includes('...DEFAULT_GLOBAL_LAYOUT,...next.layout'));
 assert.ok(editor.includes('global-design-contrast-review'));
 assert.ok(editor.includes('global-design-responsive-preview'));
 assert.ok(editor.includes('theme.text[role][previewMode]'));
 assert.ok(admin.includes('!parseGlobalDesign(draft.blocks[0].text)'));
});
