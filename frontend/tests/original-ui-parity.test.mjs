import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read=(path)=>readFileSync(new URL('../src/'+path,import.meta.url),'utf8');

test('public skin retains exact original Lefteria FC type, palette and geometry',()=>{
  const css=read('lib/public/public.css');
  const refinements=read('lib/public/brand-parity.css');
  const shell=read('lib/public/PublicShell.svelte');
  assert.match(css,/Fira Sans Extra Condensed/);
  assert.match(css,/Fira Sans/);
  assert.match(css,/--public-gold:#f5a623/);
  assert.match(css,/--public-black:#080808/);
  assert.match(css,/--public-rail:116px/);
  assert.match(css,/--public-header:76px/);
  assert.match(refinements,/--public-gold: #f5a623/);
  assert.match(refinements,/--public-black: #080808/);
  assert.match(refinements,/@media \(min-width:2560px\)/);
  assert.ok(shell.indexOf("import './brand-parity.css'")>shell.indexOf("import './editorial.css'"),
    'Brand refinements must load after the original source-like public styles');
  assert.doesNotMatch(refinements,/#[0-9a-f]{6}[^\n]*(?:blue|navy)/i);
});

test('native main/public menu preserves original nine categories and restores keyboard focus',async()=>{
  const menu=read('lib/public/PublicNavigation.svelte');
  const {publicNavigation}=await import('../src/lib/public/navigation.ts');
  assert.deepEqual(publicNavigation.map(x=>x.id),
    ['home','club','team','academy','matches','news','events','shop','contact']);
  for(const marker of ['public-sidebar','main-navigation','public-menu-drawer',
    'public-menu-navigation','public-menu-close','public-menu-back']){
    assert.ok(menu.includes(marker),marker);
  }
  assert.match(menu,/returnFocus = active instanceof HTMLElement \? active : null/);
  assert.match(menu,/focus\(\{ preventScroll: true \}\)/);
  assert.match(menu,/onclose=\{\(\) => \{ menuOpen = false; releaseScroll\(\); restoreFocus\(\); \}\}/);
  assert.match(menu,/menu\.showModal\(\)/);
  assert.match(menu,/menu\.close\(\)/);
  assert.match(menu,/close\(false\)/);
  assert.match(menu,/aria-expanded=\{menuOpen/);
});

test('mobile roles retain original Fira font sizes and same logo asset',()=>{
  const mobile=read('lib/mobile/components/mobile-theme.css');
  const shell=read('lib/mobile/components/MobileShell.svelte');
  for(const [name,value] of Object.entries({
    'display':'26px','title':'16px','card':'14px',
    'body':'13px','label':'12px','caption':'10px'
  }))assert.ok(mobile.includes('--lf-type-'+name+': '+value),name);
  assert.match(mobile,/--lf-gold: #f5a623/);
  assert.match(mobile,/Fira Sans Extra Condensed/);
  assert.match(shell,/src="\/mobile\/club-logo\.png"/);
  for(const testid of ['mobile-app','bottom-nav','role-switcher','mobile-notifications','mobile-profile'])
    assert.ok(shell.includes(testid),testid);
});
