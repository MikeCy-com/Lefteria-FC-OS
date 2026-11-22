import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const routes=[
  'academy/+page.svelte',
  'academy/[slug]/+page.svelte',
  'academy/groups/+page.svelte',
  'academy/registration/+page.svelte',
  'contact/+page.svelte',
  'pages/[slug]/+page.svelte',
  'login/+page.svelte'
];

test('original public page-local accents inherit the global published palette',()=>{
  const published=read('frontend/src/lib/public/global-design.css');
  assert.match(published,/--public-gold:var\(--lf-color-accent\)/);
  for(const route of routes){
    const source=read('frontend/src/routes/'+route);
    const styles=source.slice(source.indexOf('<style'));
    assert.match(styles,/var\(--public-gold\)/,route);
    assert.doesNotMatch(styles,/#f6bb38\b/i,'Hardcoded original gold overrides CMS accent: '+route);
  }
});

test('global refresh cannot mutate native WYSIWYG page geometry or public media',()=>{
  const css=read('frontend/src/lib/public/ui-refresh.css');
  const blocks=read('frontend/src/lib/public/WebsitePageBlocks.svelte');
  assert.doesNotMatch(css,/\.website-blocks\b|\.website-block-wrap\b|--cms-(?:gap|side|max-width|mt|mb)/);
  for(const token of ['--cms-max-width','--cms-gap','--cms-side','--cms-min-height','--cms-mt','--cms-mb'])
    assert.ok(blocks.includes(token),token);
  for(const pattern of [/background-image\s*:/,/object-fit\s*:/,/object-position\s*:/,/url\s*\(/])
    assert.doesNotMatch(css,pattern,'No media override allowed in refresh CSS');
});
