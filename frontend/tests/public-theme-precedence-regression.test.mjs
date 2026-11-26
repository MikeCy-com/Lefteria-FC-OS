import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync,readdirSync} from 'node:fs';
import {fileURLToPath} from 'node:url';
import {join,relative} from 'node:path';
import {compile} from 'svelte/compiler';

const read=path=>readFileSync(new URL('../src/'+path,import.meta.url),'utf8');
const native=read('lib/public/public.css');
const tokens=read('lib/public/global-design.css');
const shell=read('lib/public/PublicShell.svelte');
const blocks=read('lib/public/WebsitePageBlocks.svelte');
const standalone=read('routes/pages/[slug]/+page.svelte');
const browser=readFileSync(new URL('../scripts/ui-visual-audit.mjs',import.meta.url),'utf8');

test('legacy shared public styles never force an independent font/color or control geometry',()=>{
  assert.doesNotMatch(native,/\.public-site \*,\s*\.public-dialog \*\s*\{[^}]*letter-spacing:\s*0\s*!important/);
  assert.doesNotMatch(native,/font-family:'Fira Sans Extra Condensed',sans-serif\s*!important/);
  assert.doesNotMatch(native,/(?:background|color|border-color)\s*:\s*(?:white|black)\b/i);
  assert.match(native,/\.public-site :is\(\.card,[^{]+\{ border-radius:var\(--lf-layout-card-radius,0px\)/);
  assert.match(native,/\.public-site :is\(\.btn-primary,[^}]+border-radius:var\(--lf-layout-button-radius,0px\)/);
  assert.match(native,/\.public-site input:not\(\[type="checkbox"\]\)[^}]+border-radius:var\(--lf-layout-button-radius,0px\)/);
  assert.match(native,/max-width:var\(--lf-layout-page-max-width,1400px\)/);
  assert.match(tokens,/--public-gold:var\(--lf-color-accent\)/);
});

test('standalone published CMS page takes its width/padding from its own page override exactly once',()=>{
  assert.match(standalone,/--lf-page-content-max-width:\$\{localPageWidth\}/);
  assert.match(standalone,/--lf-page-content-side:\$\{localPageSide\}/);
  assert.match(standalone,/\.website-page-header-inner\{[\s\S]*?max-width:var\(--lf-page-content-max-width,1200px\)/);
  assert.match(standalone,/\.website-page-content\{[\s\S]*?padding:clamp\(28px,4\.5vw,68px\) 0 clamp\(60px,7vw,108px\)/);
  assert.match(standalone,/\.website-page-return\{[\s\S]*?margin-inline:var\(--lf-page-content-side/);
  assert.match(standalone,/\.website-page-content\{padding:32px 0 64px;\}/);
  assert.match(tokens,/\.website-page-content\s*\{\s*max-width:[^\n]*\n\s*\/\*[\s\S]*?padding-inline:0;/);
  assert.doesNotMatch(tokens,/\.website-page-content\s*\{[^}]*padding-inline:var\(--lf-page-content-side/);
});

test('one CMS embedded page override controls only its own heading and block geometry',()=>{
  assert.match(shell,/validPageLayout\(layout\)&&layout\?\.\[key\]!=null/);
  assert.match(shell,/--lf-embedded-page-max-width:\$\{embeddedDimension\(section\.page\.layout/);
  assert.match(shell,/--lf-embedded-page-side:\$\{embeddedDimension\(section\.page\.layout/);
  assert.match(shell,/--lf-embedded-page-gap:\$\{embeddedDimension\(section\.page\.layout/);
  assert.match(shell,/margin:0 auto var\(--lf-embedded-page-gap/);
  assert.match(shell,/side_padding','page-side-padding',16\)/);

  assert.match(shell,/max-width:var\(--lf-embedded-page-max-width,1280px\)/);
  assert.match(shell,/padding-inline:var\(--lf-embedded-page-side,0px\)/);
  assert.match(tokens,/\.public-section\.public-embedded-page\s*\{\s*padding-inline:0/);
  assert.match(blocks,/--cms-side:\$\{dimension\('side_padding','page-side-padding'\)\}/);
  assert.match(blocks,/--cms-max-width:\$\{dimension\('max_width','page-max-width'\)\}/);
});

test('original editorial title faces cannot override a published text role with local importance',()=>{
  const home=read('lib/public/home-reference.css');
  assert.ok(!home.includes('font-family:var(--home-display)!important'));
  assert.ok(!native.includes("font-family:'Fira Sans',sans-serif !important;"));
  assert.ok(native.includes('.public-news-body h3 { font-family:var(--lf-h3-family'));
});

test('CMS semantic heading typography comes directly from backend role values, mobile wins over desktop',()=>{
  for(const role of ['h2','h3','h4','h5','h6']){
    assert.match(blocks,new RegExp('--lf-'+role+'-family'));
    assert.match(blocks,new RegExp('--lf-'+role+'-desktop'));
    assert.match(blocks,new RegExp('--lf-'+role+'-weight'));
    assert.match(blocks,new RegExp('--lf-'+role+'-line'));
    assert.match(blocks,new RegExp('--lf-'+role+'-mobile'));
  }
  const mobile=blocks.lastIndexOf('@media(max-width:767px)');
  const desktop=blocks.lastIndexOf('.website-blocks h6{font-family:');
  assert.ok(mobile>desktop,'Responsive role declarations must appear AFTER desktop declarations');
  assert.match(shell,/font-family:var\(--lf-h2-family/);
  assert.match(shell,/font-size:var\(--lf-h2-mobile/);
  assert.match(standalone,/font-family:var\(--lf-h1-family/);
  assert.match(standalone,/font-size:var\(--lf-h1-mobile/);
  assert.match(browser,/Published navigation font must override original/);
  assert.match(browser,/Original generic cards must inherit global card radius/);
  for(const [name,component] of [['PublicShell',shell],['WebsitePageBlocks',blocks],['standalone CMS',standalone]]){
    assert.ok(compile(component,{filename:name+'.svelte',generate:'server'}).js.code.length>100,name);
  }
});


test('native page width and insets have one owner instead of conflicting theme and luxury rules',()=>{
  const luxury=read('lib/public/ui-refresh.css');
  assert.match(tokens,/padding-inline:max\(var\(--lf-layout-gutter,28px\),var\(--public-page-inset,24px\)\)/);
  assert.doesNotMatch(luxury,/\.public-section:not\(\.public-embedded-page\)\s*\{/);
  assert.doesNotMatch(luxury,/\.public-page-heading:not\(\.academy-intro\):not\(\.team-heading\)\s*\{[^}]*padding-inline/);
  assert.match(native,/\.catalog\{max-width:var\(--public-content-width,1400px\)/);
  assert.match(native,/\.match-detail\{max-width:var\(--lf-layout-page-max-width,1200px\)/);
  assert.match(native,/\.person-content\{max-width:var\(--public-content-width,1400px\)/);
  assert.match(tokens,/\.shop-container\s*\{\s*width:min\(var\(--public-content-width,1280px\)/);
  assert.ok(tokens.includes('.public-section:not(.public-embedded-page):not(.team-cms-region)'),
    'Native sections must not apply their centered inset to CMS-only team sections');
  assert.ok(tokens.includes('.public-section.team-cms-region {\\n  padding-inline:0;'.replace('\\n','\n')),
    'Team editorial blocks must own their own side padding');
  assert.ok(tokens.includes('margin-inline:calc(0px - var(--public-page-gutter,16px))'),
    'Catalog editorial blocks must cancel only their parent native gutter');
  assert.match(read('routes/team/+page.svelte'),/class="public-section team-cms-region"/);
  assert.match(read('lib/public/PublicCatalogList.svelte'),/class="catalog-cms-region"/);
});

test('every public route stylesheet (not just the seven showcase pages) inherits the palette',()=>{
  const routesRoot=fileURLToPath(new URL('../src/routes/',import.meta.url));
  const pages=[];
  function visit(directory){
    for(const entry of readdirSync(directory,{withFileTypes:true})){
      const pathname=join(directory,entry.name);
      if(entry.isDirectory())visit(pathname);
      else if(entry.name==='+page.svelte'){
        const route=relative(routesRoot,pathname).replaceAll('\\','/');
        if(!route.startsWith('admin/')&&!route.startsWith('app/'))pages.push({route,pathname});
      }
    }
  }
  visit(routesRoot);
  assert.ok(pages.length>=35,'The full public route tree must be audited, not only showcase pages');
  for(const {route,pathname} of pages){
    const source=readFileSync(pathname,'utf8');
    const pos=source.indexOf('<style');
    const markup=pos<0?source:source.slice(0,pos);
    assert.doesNotMatch(markup,/(?:bg|text|border|fill|stroke|from|via|to)-\[\s*#[\da-fA-F]{3,8}\s*\]/i,
      route+' contains a direct Tailwind paint value outside the published palette');
    assert.doesNotMatch(markup,/(?<![\w-])(?:bg|text|border)-(?:white|black|zinc|gray|amber|yellow|orange|neutral|slate)(?:-\d+)?\b/i,
      route+' contains a legacy fixed named paint class');
    if(pos<0)continue;
    const css=source.slice(pos);
    const normalized=css.replace(
      /var\(--(?:lf-(?:color-[\w-]+|feedback-[\w-]+)|public-gold)\s*,\s*#[\da-fA-F]{6}\)/g,
      'THEME_FALLBACK'
    );
    assert.doesNotMatch(normalized,/#[\da-fA-F]{3,8}\b/,
      route+' contains a literal paint color outside a validated theme token');
    assert.doesNotMatch(css,/(?:background(?:-color)?|border-color|color)\s*:\s*(?:white|black)\b/i,
      route+' contains fixed white/black instead of a published semantic color');
  }
});
