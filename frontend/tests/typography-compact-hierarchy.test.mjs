import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const file = path => readFileSync(new URL('../src/' + path,import.meta.url),'utf8');
const home = file('lib/public/home-reference.css');
const publicStyle = file('lib/public/club-polish.css');
const mobile = file('lib/mobile/components/mobile-theme.css');
const baseline = file('lib/public/public.css');

// Compare the last effective declaration; older reference rules intentionally
// remain intact for source-image parity and non-typographic layout geometry.
function lastClamp(css,selector) {
  const escaped=selector.replace(/[.*+?^${}()|[\]\\]/g,'\\$&');
  const blocks=[...css.matchAll(new RegExp(escaped+'\\s*\\{([^}]+)\\}','g'))];
  assert.ok(blocks.length,'missing '+selector);
  const matches=[...blocks.at(-1)[1].matchAll(/font-size:\\s*clamp\\((\\d+)px,\\s*([\\d.]+)vw,\\s*(\\d+)px\\)/g)];
  assert.ok(matches.length,'missing clamp for '+selector);
  return matches.at(-1).slice(1).map(Number);
}

test('homepage hero and promotional headings are restrained across desktop and phone',()=>{
  const hero='.public-site .home-reference[data-testid="home-page"] .public-hero-copy h1';
  const heroDeclarations=[...home.matchAll(new RegExp(hero.replace(/[.*+?^${}()|[\]\\]/g,'\\$&')+'\\s*\\{([^}]+)\\}','g'))];
  assert.ok(heroDeclarations.length>=3);
  const desktop=lastClamp(home,heroDeclarations.length ? hero : hero);
  assert.deepEqual(desktop,[34,7.7,40]); // mobile declaration is last in the cascade.
  assert.match(home,/font-size:clamp\\(40px,3\\.05vw,67px\\)/);
  assert.match(home,/font-size:clamp\\(21px,1\\.48vw,27px\\)/);
  assert.match(home,/font-size:clamp\\(18px,1\\.18vw,23px\\)/);
  assert.match(home,/text-wrap:balance;\\s*white-space:normal/);
  assert.ok(desktop[0]>=34,'avoid illegibly tiny mobile display headlines');
});

test('site-wide display scale stays compact, with separate phone and desktop heading caps',()=>{
  assert.match(publicStyle,/--public-title-size:clamp\\(35px,3\\.05vw,49px\\)/);
  assert.match(publicStyle,/--public-title-size:clamp\\(32px,8vw,39px\\)/);
  assert.match(publicStyle,/font-size:clamp\\(32px,2\\.8vw,45px\\)/);
  assert.match(publicStyle,/font-size:clamp\\(21px,1\\.7vw,25px\\)/);
  assert.match(publicStyle,/font-size:clamp\\(37px,3\\.9vw,55px\\)/);
  assert.match(baseline,/:where\\(\\.public-site,\\.public-dialog\\) \\{[^\\n]*font-size:16px/);
});

test('app titles reduce modestly without shrinking body, labels or touch-safe forms',()=>{
  const greeting=lastClamp(mobile,'.lf-mobile-theme.mobile-shell .parent-greeting h1');
  const family=lastClamp(mobile,'.lf-mobile-theme.mobile-shell .family-heading h1');
  assert.deepEqual(greeting,[26,6.8,29]);
  assert.deepEqual(family,[18,1.9,21]);
  assert.match(mobile,/--lf-type-body: 13px/);
  assert.match(mobile,/--lf-type-label: 12px/);
  assert.match(mobile,/font-size:max\\(16px,1rem\\)/);
  assert.match(mobile,/min-height: 44px/);
  assert.doesNotMatch(mobile,/body\\s*\\{\\s*font-size:\\s*11px/);
});
