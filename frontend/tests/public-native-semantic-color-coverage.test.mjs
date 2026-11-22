import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=path=>readFileSync(new URL('../src/'+path,import.meta.url),'utf8');
const paths=[
  'routes/academy/+page.svelte',
  'routes/academy/[slug]/+page.svelte',
  'routes/academy/groups/+page.svelte',
  'routes/academy/registration/+page.svelte',
  'routes/contact/+page.svelte',
  'routes/team/+page.svelte',
  'routes/login/+page.svelte',
  'routes/pages/[slug]/+page.svelte'
];
const scopedFallback=/var\(--lf-(?:color-(?:bg|surface|text|muted|border|link|accent)|feedback-[\w-]+),#[\da-fA-F]{3,8}\)/g;

test('every legacy native page color is a semantic published-theme token or a documented feedback color',()=>{
  for(const path of paths){
    const source=read(path);
    assert.ok(source.includes('<style'),path+' must have local presentation');
    const css=source.slice(source.indexOf('<style'));
    const tokenized=css.replace(scopedFallback,'THEME_TOKEN');
    assert.doesNotMatch(tokenized,/#[\da-fA-F]{3,8}\b/,
      path+' uses a color that is not inside an approved theme fallback');
    assert.doesNotMatch(css,/rgb\(\s*246\s+187\s+56\s*\//i,
      path+' still uses a noneditable golden tint');
    assert.match(css,/var\(--lf-color-/,
      path+' must use the global WYSIWYG design palette');
    assert.ok(compile(source,{filename:path,generate:'server'}).js.code.length>100);
  }
});

test('image URLs, content and native workflows are not replaced by page palette overrides',()=>{
  const team=read('routes/team/+page.svelte');
  const login=read('routes/login/+page.svelte');
  const registration=read('routes/academy/registration/+page.svelte');
  const academy=read('routes/academy/+page.svelte');
  const contact=read('routes/contact/+page.svelte');
  assert.match(team,/url\("\$\{publicImages\.pitch\}"\)/);
  assert.match(login,/welcomeImage\.replaceAll\('"','%22'\)/);
  assert.match(registration,/ctx\.strokeStyle=\/\^#\[0-9a-fA-F\]\{6\}\$\/\.test\(currentAccent\)\?currentAccent/);
  assert.match(registration,/getComputedStyle\(el\)\.getPropertyValue\('--public-gold'\)/);
  assert.match(registration,/getContext\('2d'\)/);
  assert.match(academy,/data-testid="academy-stats"/);
  assert.match(contact,/data-testid="contact-form"/);
  assert.match(team,/data-testid="public-team-selector"/);
  for(const source of [academy,contact,registration,team])
    assert.doesNotMatch(source,/<img[^>]+src="https:\/\/source\.unsplash\.com/,
      'A new static image source cannot replace original club media');
});
