import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=path=>readFileSync(new URL('../src/'+path,import.meta.url),'utf8');
const native=read('lib/public/public.css');
const tokens=read('lib/public/global-design.css');
const shell=read('lib/public/PublicShell.svelte');
const blocks=read('lib/public/WebsitePageBlocks.svelte');
const standalone=read('routes/pages/[slug]/+page.svelte');
const browser=readFileSync(new URL('../scripts/ui-visual-audit.mjs',import.meta.url),'utf8');

test('legacy shared public styles never force an independent font/color or control geometry',()=>{
  assert.doesNotMatch(native,/\.public-site \*,\s*\.public-dialog \*\s*\{[^}]*!important/);
  assert.doesNotMatch(native,/font-family:'Fira Sans Extra Condensed',sans-serif\s*!important/);
  assert.doesNotMatch(native,/(?:background|color|border-color)\s*:\s*(?:white|black)\b/i);
  assert.match(native,/\.public-site :is\(\.card,[^{]+\{ border-radius:var\(--lf-layout-card-radius,0px\)/);
  assert.match(native,/\.public-site :is\(\.btn-primary,[^{]+border-radius:var\(--lf-layout-button-radius,0px\)/);
  assert.match(native,/\.public-site input:not\(\[type="checkbox"\]\)[^{]+border-radius:var\(--lf-layout-button-radius,0px\)/);
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
  assert.match(shell,/max-width:var\(--lf-embedded-page-max-width,1280px\)/);
  assert.match(shell,/padding-inline:var\(--lf-embedded-page-side,0px\)/);
  assert.match(tokens,/\.public-section\.public-embedded-page\s*\{\s*padding-inline:0/);
  assert.match(blocks,/--cms-side:\$\{dimension\('side_padding','page-side-padding'\)\}/);
  assert.match(blocks,/--cms-max-width:\$\{dimension\('max_width','page-max-width'\)\}/);
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
