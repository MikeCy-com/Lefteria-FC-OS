import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
const src=p=>readFileSync(new URL('../'+p,import.meta.url),'utf8');
const hero=src('src/lib/public/HomeHero.svelte');
const homeCss=src('src/lib/public/home-reference.css');
const navCss=src('src/lib/public/public.css');
const admin=src('src/lib/admin/AdminSignIn.svelte');
const adminCss=src('src/lib/admin/admin.css');
const login=src('src/lib/mobile/components/MobileLogin.svelte');
const audit=src('scripts/ui-visual-audit.mjs');

test('unpublished matches still occupy the verified right card region but never invent a fixture',()=>{
  assert.doesNotThrow(()=>compile(hero,{filename:'HomeHero.svelte',generate:'server'}));
  for(const tag of ['data-testid="public-hero-fixture"','data-testid="hero-fixture-unpublished"',
    'data-testid="hero-fixture-schedule"','Δεν έχει δημοσιευτεί επιβεβαιωμένος επόμενος αγώνας'])
    assert.ok(hero.includes(tag),tag);
  assert.match(hero, /\{#if fixture\}/);
  assert.match(hero, /\{:else\}/);
  assert.match(hero, /fixturePublicLink\(fixture\)/);
  assert.match(hero, /href="\/team\?tab=schedule"/);
  assert.match(homeCss, /\.public-fixture-teams-awaiting/);
  assert.match(homeCss, /\.public-countdown-unpublished/);
  assert.doesNotMatch(hero,/ΩΡΑ: 19:00|ΣΕΚ ΑΓΙΟΥ|2026-11-14/);
});
test('desktop rail reference photo and actual club mark are aligned with main header',()=>{
  assert.match(navCss,/width:142px;height:142px/);
  assert.match(navCss,/--public-rail:154px/);
  assert.match(navCss,/padding:0 clamp\(20px,2vw,35px\) 0 clamp\(36px,6vw,100px\)/);
});
test('Admin Portal login has full-sized emblem, true verified authority and visible security explanation',()=>{
  assert.doesNotThrow(()=>compile(admin,{filename:'AdminSignIn.svelte',generate:'server'}));
  assert.match(admin,/data-testid="admin-login-security-note"/);
  assert.match(admin,/data-testid="admin-login-submit"/);
  assert.match(admin,/onLogin\(username, password, rememberDays\)/);
  assert.match(admin,/class="admin-crest"/);
  assert.match(adminCss,/width:clamp\(150px,12vw,205px\)/);
  assert.match(adminCss,/margin:18px 0 42px/);
  assert.match(adminCss,/justify-content:flex-start/);
  assert.doesNotMatch(admin,/OTP override|bypass|fake login/i);
});
test('member role cards remain visual-only and five feature explanations match screenshot region',()=>{
  assert.doesNotThrow(()=>compile(login,{filename:'MobileLogin.svelte',generate:'server'}));
  for(const marker of ['member-role-player','member-role-parent','member-role-fan','login-benefits-intro',
    'Παρουσίες','Πρόγραμμα','Ανακοινώσεις','Επόμενος αγώνας','Νέα Συλλόγου',
    'data-testid="request-otp-btn"','onVerifyOtp(code, rememberDays)',
    'onChooseRole(identity.role, rememberDays)','data-testid="fan-register-submit"'])
    assert.ok(login.includes(marker),marker);
  assert.match(login,/grid-template-columns:minmax\(0,1\.046fr\) minmax\(0,\.954fr\)/);
  assert.match(login,/min-height:138px;max-height:138px/);
  assert.match(login,/height:calc\(100dvh - 200px\)/);
  assert.match(login,/\(\) => changeStep\('fan_register'\)/);
});
test('Chromium 1672x941 audit records photographed dimensions, not CI success as pixel match',()=>{
  for(const marker of ['homeFixture:rect','adminCrest:rect','adminSecurity:rect',
    'memberWelcome:rect','memberBenefits:rect','fixture.x<1050','crest.width<155',
    'benefits.y<770','referenceLayout'])
    assert.ok(audit.includes(marker),marker);
});
