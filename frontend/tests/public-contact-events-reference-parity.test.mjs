import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { compile } from 'svelte/compiler';
import { safeContactEmail, contactEmailDraftHref } from '../src/lib/public/contact-email.ts';
import { catalogListHref } from '../src/lib/public/PublicCatalog.ts';

const read = name => readFileSync(new URL('../src/'+name,import.meta.url),'utf8');

test('Contact layout restores original information cards and accessible form, without a fake sent confirmation',()=>{
  const page=read('routes/contact/+page.svelte');
  assert.doesNotThrow(()=>compile(page,{filename:'contact/+page.svelte',generate:'server'}));
  for(const id of ['contact-page','contact-details','contact-form','contact-name','contact-email','contact-subject','contact-message','contact-submit'])
    assert.match(page,new RegExp('data-testid="'+id+'"'));
  for(const label of ['ΠΛΗΡΟΦΟΡΙΕΣ ΣΥΛΛΟΓΟΥ','ΣΤΕΙΛΤΕ ΜΗΝΥΜΑ','ΔΙΕΥΘΥΝΣΗ','ΤΗΛΕΦΩΝΟ','EMAIL'])
    assert.ok(page.includes(label),label);
  assert.match(page,/organization_id=lefteria_fc_academy/);
  assert.match(page,/safeContactEmail\(club\?\.email\)/);
  assert.match(page,/contactEmailDraftHref\(validEmail/);
  assert.match(page,/δεν αποθηκεύει ούτε στέλνει δεδομένα/);
  assert.doesNotMatch(page,/contact-success|\/?api\/contact|fetch\(|σας εστάλη επιτυχώς/);
  assert.match(page,/aria-current=\{data\.organization === 'lefteria_fc_academy'/);
});

test('Contact draft uses published recipient only, encodes CRLF and rejects unsafe/incomplete addresses',()=>{
  const fields={name:'Ανδρέας Πα.',replyEmail:'reply@example.org',organizationLabel:'ΑΚΑΔΗΜΙΑ',subject:'Εγγραφή Ακαδημίας',message:'Γεια σας, ενδιαφέρομαι.'};
  assert.equal(safeContactEmail('info@example.org'),'info@example.org');
  for(const input of [null,{},'','bad','one@example.org?subject=inject','a@example.org\r\nBcc:evil@example.org','a@localhost','evil.com'])
    assert.equal(safeContactEmail(input),null,JSON.stringify(input));
  const href=contactEmailDraftHref('info@example.org',fields);
  assert.ok(href?.startsWith('mailto:info@example.org?subject='));
  const parsed=new URL(href);
  assert.equal(parsed.protocol,'mailto:');
  assert.match(parsed.searchParams.get('subject'),/Εγγραφή Ακαδημίας · ΑΚΑΔΗΜΙΑ/);
  assert.match(parsed.searchParams.get('body'),/Ανδρέας Πα./);
  assert.match(parsed.searchParams.get('body'),/reply@example.org/);
  assert.match(parsed.searchParams.get('body'),/Γεια σας/);
  assert.equal(contactEmailDraftHref('info@example.org',{...fields,replyEmail:'invalid'}),null);
  assert.equal(contactEmailDraftHref('info@example.org',{...fields,message:'   '}),null);
  assert.equal(contactEmailDraftHref('info@example.org',{...fields,subject:'   '}),null);
});

test('Events original-style filter chips preserve server authority and all selected dimensions',()=>{
  const list=read('lib/public/PublicCatalogList.svelte');
  assert.doesNotThrow(()=>compile(list,{filename:'PublicCatalogList.svelte',generate:'server'}));
  for(const id of ['filter-scope-all','filter-scope-first-team','filter-scope-academy','filter-when-upcoming','filter-when-past','filter-when-all','filter-type-all'])
    assert.match(list,new RegExp('data-testid="'+id+'"'));
  assert.match(list,/data\.kind === 'events'/);
  assert.match(list,/data\.eventTypes as value/);
  assert.match(list,/aria-current=\{data\.scope === 'academy'/);
  assert.equal(catalogListHref('events','academy','past','camp'),'/events?scope=academy&when=past&type=camp');
  assert.equal(catalogListHref('events','first_team','upcoming','all'),'/events?scope=first_team&when=upcoming');
  assert.equal(catalogListHref('events',null,'all','all'),'/events?when=all');
  assert.equal(catalogListHref('events','academy','all','trial'),'/events?scope=academy&when=all&type=trial');
  assert.doesNotMatch(list,/<script[^>]+src=|\{@html/);
});
