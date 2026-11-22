import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const file=(path)=>readFileSync(new URL('../'+path,import.meta.url),'utf8');
const home=file('src/lib/public/home-reference.css');
const shared=file('src/lib/public/club-polish.css');
const academy=file('src/routes/academy/+page.svelte');
const team=file('src/routes/team/+page.svelte');
const published=file('src/routes/pages/[slug]/+page.svelte');
const mobile=file('src/lib/mobile/components/mobile-theme.css');
const login=file('src/lib/mobile/components/MobileLogin.svelte');
const shell=file('src/lib/mobile/components/MobileShell.svelte');
const app=file('src/routes/app/[...path]/+page.svelte');
const base=file('src/lib/public/public.css');

// This test inspects the last effective declaration for explicitly scoped
// display typography. A 16px reading baseline is NOT part of the reduction.
function clampIn(source,selector,index=-1) {
  const escaped=selector.replace(/[.*+?^${}()|[\]\\]/g,'\\$&');
  const rules=[...source.matchAll(new RegExp(escaped+'\\s*\\{([^}]+)\\}','g'))];
  assert.ok(rules.length,'missing selector '+selector);
  const values=[...rules.at(index)[1].matchAll(/font-size:\s*clamp\(\s*(\d+)px\s*,\s*([\d.]+)vw\s*,\s*(\d+)px\s*\)/g)];
  assert.ok(values.length,'missing font-size clamp in '+selector);
  return values.at(-1).slice(1).map(Number);
}

test('public display hierarchy matches source Fira while body text stays legible',()=>{
  const selector='.public-site .home-reference[data-testid="home-page"] .public-hero-copy h1';
  const h=clampIn(home,selector,0);
  const phone=clampIn(home,selector);
  assert.ok(h[0]>=44 && h[2]<=80,'Desktop hero must fit a headline without 100px type');
  assert.ok(phone[0]>=34 && phone[2]<=46,'Phone hero stays compact but readable');
  const a=clampIn(academy,':global(.public-site[data-route="/academy"]) .academy-intro h1',1);
  assert.ok(a[2]<=75 && a[0]>=40,'Academy hero remains condensed but more compact');
  assert.ok(academy.includes('.academy-intro h1{font-size:46px}'),'Phone override must not restore 64px headings');
  assert.ok(shared.includes('font:italic 900 clamp(43px,4.3vw,74px)'),'First Team masthead reduced');
  assert.match(team,/font:italic 900 clamp\(43px,3\.9vw,66px\)/);
  assert.match(published,/font:italic 900 clamp\(36px,5vw,78px\)/);
  assert.match(home,/public-news-card:first-child h3/);
  assert.match(shared,/\.public-group-card h3 \{font-size:clamp\(23px,1\.9vw,29px\)/);
  assert.match(base,/:where\(\.public-site,\.public-dialog\) \{[^\n]*font-size:16px/);
  assert.match(home,/text-wrap:balance/);
});

test('mobile dashboards have restrained titles while protecting controls and body copy',()=>{
  const hero=clampIn(mobile,'.lf-mobile-theme.mobile-shell .role-home .hero h1');
  assert.ok(hero[2]<=40 && hero[0]>=26);
  const panels=clampIn(mobile,'.lf-mobile-theme.mobile-shell .family-heading h1');
  assert.ok(panels[2]<=24 && panels[0]>=18);
  assert.match(mobile,/--lf-type-body: 13px/);
  assert.match(mobile,/--lf-type-caption: 10px/);
  assert.match(mobile,/font-size:max\(16px,1rem\)/);
  assert.match(mobile,/\(pointer:coarse\)/);
  assert.match(mobile,/font-size:clamp\(12px,1vw,13px\)/);
  assert.match(login,/font:italic 900 clamp\(34px,3\.7vw,56px\)/);
  assert.match(login,/\.login-welcome h2\{font-size:31px\}/);
  assert.match(shell,/min-height:68px/);
  assert.match(app,/<MobileShell/);
});

test('public and app templates still compile without replacing functional content',()=>{
  for(const [filename,source] of [
    ['academy/+page.svelte',academy],['team/+page.svelte',team],
    ['pages/[slug]/+page.svelte',published],
    ['MobileLogin.svelte',login],['MobileShell.svelte',shell]
  ]) assert.ok(compile(source,{filename,generate:'server'}).js.code.length>100,filename);
  assert.match(academy,/buildAcademyTerritoryDirectory/);
  assert.match(team,/<PublicShell/);
  assert.match(published,/<WebsitePageBlocks/);
  assert.match(login,/onVerifyOtp/);
  assert.doesNotMatch(shared,/(^|\n)\s*(?:\.admin-|\.mobile-|body\s*\{)/);
});

test('mobile shell honors original app header, bottom bar and safe-area layout',()=>{
  const originalShell=mobile.slice(mobile.indexOf('/* Original Lefteria mobile shell parity'));
  assert.ok(originalShell.length>500,'original shell parity overrides must exist');
  assert.match(originalShell,/min-height:\s*calc\(72px \+ env\(safe-area-inset-top/);
  assert.match(originalShell,/min-height:\s*68px/);
  assert.match(originalShell,/\.bottom-nav\s*\{[^}]*background:\s*var\(--lf-bg\)/s);
  assert.match(originalShell,/\.bottom-items > button\.active\s*\{[^}]*background:\s*transparent/s);
  assert.match(mobile,/--lf-body:\s*'Fira Sans'/);
  assert.match(mobile,/--lf-heading:\s*'Fira Sans Extra Condensed'/);
  assert.match(shell,/padding-bottom:env\(safe-area-inset-bottom/);
  assert.match(shell,/selectedPrimaryTab === tab/);
});
