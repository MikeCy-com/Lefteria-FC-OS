import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {
  DEFAULT_GLOBAL_DESIGN,parseGlobalDesign,globalDesignCssVars
} from '../src/lib/public/global-design.ts';
const read=p=>readFileSync(new URL('../src/'+p,import.meta.url),'utf8');
const shared=['lib/public/public.css','lib/public/club-polish.css','lib/public/home-reference.css'];
test('all shared public legacy styles resolve their palette from the backend design token contract',()=>{
  for(const path of shared){
    const css=read(path);
    const tokenless=css.replace(/var\(--lf-(?:color-(?:bg|surface|text|muted|border|link|accent)|feedback-[\w-]+),#[\da-fA-F]{6}\)/g,'TOKEN');
    assert.doesNotMatch(tokenless,/#[\da-fA-F]{3,8}\b/,path+' contains a direct noneditable color');
    assert.doesNotMatch(css,/(?:rgba?|hsla?)\s*\(/i,path+' contains a noneditable color function');
    assert.match(css,/var\(--lf-color-/);
    assert.match(css,/\.public-site|\.public-section/);
  }
});
test('published global palette controls browser light/dark mode without changing native content',()=>{
  assert.match(globalDesignCssVars(DEFAULT_GLOBAL_DESIGN),/--lf-color-scheme:dark/);
  const light=structuredClone(DEFAULT_GLOBAL_DESIGN);
  light.colors={
    background:'#f5f5f0',foreground:'#151515',accent:'#145db3',
    surface:'#ffffff',muted:'#4d4d4d',border:'#666666',link:'#145db3'
  };
  assert.ok(parseGlobalDesign(JSON.stringify(light)));
  assert.match(globalDesignCssVars(light),/--lf-color-scheme:light/);
  assert.match(read('lib/public/global-design.css'),/color-scheme:var\(--lf-color-scheme,dark\)/);
  const shell=read('lib/public/PublicShell.svelte');
  for(const css of ['public.css','club-polish.css','global-design.css','ui-refresh.css'])
    assert.match(shell,new RegExp("import '\\./"+css.replaceAll('.','\\.')+"'"));
  assert.match(shell,/publishedGlobalDesign\(page\.data\?\.websitePages\?\.\[organization\]/);
  assert.match(shell,/style=\{globalTheme \? globalDesignCssVars\(globalTheme\)/);
});
test('premium UI is presentation-only and does not make a competing global theme',()=>{
  const skin=read('lib/public/ui-refresh.css');
  assert.match(skin,/--ui-accent:var\(--lf-color-accent/);
  assert.match(skin,/--ui-card-radius:var\(--lf-layout-card-radius/);
  for(const forbidden of [
    /background-image\s*:/,/object-(?:fit|position)\s*:/,
    /font-(?:size|family)\s*:/,/url\s*\(/,
    /\.website-block-wrap\b/,/\.website-blocks\b/
  ]) assert.doesNotMatch(skin,forbidden);
});
