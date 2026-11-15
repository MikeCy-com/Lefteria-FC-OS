import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read = path => readFileSync(new URL('../src/lib/public/'+path,import.meta.url),'utf8');
const footer = read('PublicFooter.svelte');
const css = read('club-polish.css');
const shell = read('PublicShell.svelte');

test('footer remains usable without a configured social profile and never invents one',()=>{
  assert.ok(compile(footer,{filename:'PublicFooter.svelte',generate:'server'}).js.code.length>100);
  assert.match(footer,/const hasPublicSocials = \$derived\(socials\.some/);
  assert.match(footer,/\{#if hasPublicSocials\}/);
  assert.match(footer,/data-testid="footer-social-fallback"/);
  assert.match(footer,/href="\/news">ΤΕΛΕΥΤΑΙΑ ΝΕΑ/);
  assert.match(footer,/href="\/events">ΕΚΔΗΛΩΣΕΙΣ/);
  assert.match(footer,/\{#if club\?\.social_links\[key\]\}/);
  assert.match(footer,/club\.social_links\[key\]/);
  assert.match(footer,/data-testid="public-back-top"/);
});

test('footer actions describe their actual destinations and app capability in Greek',()=>{
  assert.match(footer,/\['tickets','Προϊόντα συλλόγου','\/shop','bag'\]/);
  assert.doesNotMatch(footer, /\['tickets','Εισιτήρια','\/shop','bag'\]/);
  assert.match(footer,/Η ΕΠΙΣΗΜΗ ΜΑΣ ΕΦΑΡΜΟΓΗ/);
  assert.match(footer,/σύμφωνα με τον ρόλο σου/);
  assert.match(footer,/<AppInstallButton \/>/);
  assert.doesNotMatch(footer,/Download Our Official App/);
  assert.match(shell,/<PublicFooter/);
});

test('shared footer styling supports focus, phone layout and PWA install CTA',()=>{
  for(const selector of [
    '.public-footer-follow-empty',
    '.public-footer-social-group > div a',
    '.public-footer-app',
    '.public-app-download',
    '.public-app-download-copy',
    '.public-footer-bottom'
  ])assert.ok(css.includes(selector),'Missing footer refinement '+selector);
  assert.match(css,/\.public-footer :is\(a,button\):focus-visible/);
  assert.match(css,/@media\(max-width:480px\)/);
  assert.match(css,/\.public-footer-follow > \.public-back-top/);
  assert.match(css,/min-height:48px/);
});

test('Academy footer remains inside Academy scope across cities and links',()=>{
  assert.match(footer,/organization = 'lefteria_fc'/);
  assert.match(footer,/const isAcademy = \$derived\(organization === 'lefteria_fc_academy'\)/);
  assert.match(footer,/const links = \$derived\(isAcademy \? academyLinks : clubLinks\)/);
  assert.match(footer,/\['groups','Ηλικιακά τμήματα','\/academy\/groups'\]/);
  assert.match(footer,/\['partners','Συνεργάτες Ακαδημίας','\/partners\?scope=academy'\]/);
  assert.match(footer,/\['sponsors','Χορηγοί Ακαδημίας','\/sponsors\/academy'\]/);
  assert.match(footer,/data-organization=\{organization\}/);
  assert.match(footer,/isAcademy \? publicImages\.academy : publicImages\.city/);
  assert.match(footer,/ΚΑΘΕ ΠΟΛΗ/);
  assert.match(shell,/<PublicFooter \{club\} \{organization\} \/>/);
});
