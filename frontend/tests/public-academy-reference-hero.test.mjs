import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const source=readFileSync(new URL('../src/routes/academy/+page.svelte',import.meta.url),'utf8');

test('Academy reference hero is full-bleed and safe when CMS has no academy group banner',()=>{
  assert.ok(compile(source,{filename:'routes/academy/+page.svelte',generate:'server'}).js.code.length>100);
  assert.match(source,/group\.organization_id==='lefteria_fc_academy'&&group\.banner_url/);
  assert.match(source,/\|\| publicImages\.academy/);
  assert.match(source,/<PublicImage src=\{academyBanner\} alt="" class="academy-hero-image"/);
  assert.match(source,/<h1>ΛΕΥΤΕΡΙΑ FC <span class="academy-title-gold">ΑΚΑΔΗΜΙΑ<\/span><\/h1>/);
  assert.match(source,/ΔΙΑΠΛΑΣΗ <span>·<\/span> ΑΝΑΠΤΥΞΗ/);
  assert.match(source,/object-fit:cover;object-position:center 42%/);
  assert.match(source,/linear-gradient\(90deg,#080909f3/);
  assert.match(source,/min-height:clamp\(320px,21vw,385px\)/);
  assert.match(source,/class="academy-program-overview"/);
  assert.match(source,/data-testid="academy-age-groups"/);
  assert.match(source,/@media\(max-width:700px\)/);
});
test('Academy still provides verified group cards, linked training, local matches and sponsors',()=>{
  assert.match(source,/data-testid="academy-landing-page"/);
  assert.match(source,/academyGroupLink\(group\)/);
  assert.match(source,/group\.training_schedule/);
  assert.match(source,/id="academy-fixtures"/);
  assert.match(source,/organization="lefteria_fc_academy"/);
  assert.match(source,/<OrganizationSponsorStrip organization="lefteria_fc_academy"/);
  assert.match(source,/href="\/academy\/groups"/);
  assert.match(source,/href="\/news\?organization_id=lefteria_fc_academy"/);
  assert.doesNotMatch(source,/organization="lefteria_fc"(?:\s|>)/);
});
