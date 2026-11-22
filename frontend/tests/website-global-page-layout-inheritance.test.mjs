import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {
  DEFAULT_PAGE_LAYOUT,PAGE_LAYOUT_CHOICES,
  resolveWebsitePageLayout,validPageLayout
} from '../src/lib/public/website-pages.ts';
import {
  DEFAULT_GLOBAL_DESIGN,DEFAULT_GLOBAL_LAYOUT,
  globalPageDefaults,parseGlobalDesign,globalDesignCssVars
} from '../src/lib/public/global-design.ts';

const read=p=>readFileSync(new URL('../src/'+p,import.meta.url),'utf8');

test('without a local override every page inherits its organization global defaults',()=>{
  const team=structuredClone(DEFAULT_GLOBAL_DESIGN);
  team.layout.page_max_width=1440;
  team.layout.page_gap=48;
  team.layout.page_side_padding=32;
  team.layout.page_margin_top=64;
  team.layout.page_margin_bottom=80;
  const defaults=globalPageDefaults(team);
  assert.deepEqual(resolveWebsitePageLayout(null,defaults),{
    max_width:1440,gap:48,side_padding:32,margin_top:64,margin_bottom:80
  });
  assert.deepEqual(resolveWebsitePageLayout(undefined,globalPageDefaults(DEFAULT_GLOBAL_DESIGN)),DEFAULT_PAGE_LAYOUT);
  assert.match(globalDesignCssVars(team),/--lf-layout-page-max-width:1440px/);
  // Same slug in the other organization receives ONLY its own global values.
  assert.deepEqual(resolveWebsitePageLayout(null,globalPageDefaults(DEFAULT_GLOBAL_DESIGN)),DEFAULT_PAGE_LAYOUT);
  assert.notDeepEqual(defaults,globalPageDefaults(DEFAULT_GLOBAL_DESIGN));
});

test('per-page settings override only the edited fields and retain inheritance elsewhere',()=>{
  const global={max_width:1440,gap:48,side_padding:32,margin_top:64,margin_bottom:80};
  assert.deepEqual(resolveWebsitePageLayout({gap:8},global),{
    ...global,gap:8
  });
  assert.deepEqual(resolveWebsitePageLayout({max_width:960,margin_bottom:0},global),{
    ...global,max_width:960,margin_bottom:0
  });
  assert.deepEqual(resolveWebsitePageLayout({gap:null,side_padding:12},global),{
    ...global,side_padding:12
  });
  const original={max_width:1280,gap:24,side_padding:16};
  assert.deepEqual(resolveWebsitePageLayout(original,global),{
    ...global,...original
  },'Existing explicit page records retain their saved width/padding/gap');
  assert.equal(validPageLayout({}),true);
  assert.equal(validPageLayout({gap:8}),true);
  assert.equal(validPageLayout({margin_top:80}),true);
  assert.equal(validPageLayout({max_width:999}),false);
  assert.equal(validPageLayout({margin_bottom:-1}),false);
  assert.equal(validPageLayout({gap:'24px'}),false);
  assert.equal(validPageLayout({custom_css:'display:none'}),false);
  assert.equal(validPageLayout([]),false);
  assert.deepEqual(Object.keys(PAGE_LAYOUT_CHOICES),[
    'max_width','gap','side_padding','margin_top','margin_bottom'
  ]);
});

test('original six-key global theme stays readable and gains fallback page defaults',()=>{
  const legacy=structuredClone(DEFAULT_GLOBAL_DESIGN);
  const layout=legacy.layout;
  for(const key of ['page_max_width','page_gap','page_side_padding','page_margin_top','page_margin_bottom'])
    delete layout[key];
  const parsed=parseGlobalDesign(JSON.stringify(legacy));
  assert.ok(parsed);
  assert.deepEqual(globalPageDefaults(parsed),{
    max_width:DEFAULT_GLOBAL_LAYOUT.page_max_width,
    gap:DEFAULT_GLOBAL_LAYOUT.page_gap,
    side_padding:DEFAULT_GLOBAL_LAYOUT.page_side_padding,
    margin_top:DEFAULT_GLOBAL_LAYOUT.page_margin_top,
    margin_bottom:DEFAULT_GLOBAL_LAYOUT.page_margin_bottom
  });
  assert.match(globalDesignCssVars(parsed),/--lf-layout-page-side-padding:16px/);
});

test('WYSIWYG page only persists override fields, with per-field reset-to-global control',()=>{
  const builder=read('routes/admin/website-pages/+page.svelte');
  const blocks=read('lib/public/WebsitePageBlocks.svelte');
  const canvas=read('lib/admin/WebsiteVisualCanvas.svelte');
  assert.match(builder,/data-testid="cms-page-layout-overrides"/);
  assert.match(builder,/e\.currentTarget\.value===''?\s*\?/);
  assert.match(builder,/if\(value===null\)delete next\[key\]/);
  assert.match(builder,/draft\.layout=Object\.keys\(next\)\.length\?next:null/);
  assert.match(builder,/publishedPageDefaults=\$derived\(globalPageDefaults\(parseGlobalDesign/);
  assert.match(builder,/resolveWebsitePageLayout\(draft\.layout,publishedPageDefaults\)/);
  for(const key of Object.keys(PAGE_LAYOUT_CHOICES))
    assert.ok(builder.includes('key as keyof WebsitePageLayout'),'Editor must support '+key);
  assert.match(blocks,/dimension\('max_width','page-max-width'\)/);
  assert.match(blocks,/dimension\('margin_top','page-margin-top'\)/);
  assert.match(blocks,/dimension\('margin_bottom','page-margin-bottom'\)/);
  assert.match(blocks,/margin-block:var\(--cms-page-top\) var\(--cms-page-bottom\)/);
  assert.match(canvas,/resolveWebsitePageLayout\(pageLayout\)/);
  for(const [name,source] of [['WYSIWYG editor',builder],['published blocks',blocks],['preview canvas',canvas]]){
    assert.ok(compile(source,{filename:name+'.svelte',generate:'server'}).js.code.length>100,name);
  }
});

test('API accepts only allowlisted partial page settings; publishing remains authorized',()=>{
  const api=readFileSync(new URL('../../backend/src/LefteriaFc.Api/PublicContent/WebsitePageEndpoints.cs',import.meta.url),'utf8');
  assert.match(api,/record WebsitePageLayout\(\s*int\? MaxWidth = null/);
  assert.match(api,/int\? MarginTop = null, int\? MarginBottom = null/);
  assert.match(api,/JsonUnmappedMemberHandling\.Disallow/);
  assert.match(api,/MarginTop is null or 0 or 8/);
  assert.match(api,/MarginBottom is null or 0 or 8/);
  assert.match(api,/layout\.EnumerateObject\(\)\.Count\(\) != 11/);
  assert.match(api,/ValidateGlobalDesign\(slug, body\.Page\)/);
  assert.match(api,/Authorized\(context, owner, "news:write"/);
});
