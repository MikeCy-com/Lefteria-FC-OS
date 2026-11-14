import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const page=readFileSync(new URL('../src/routes/trials/+page.svelte',import.meta.url),'utf8');
const css=readFileSync(new URL('../src/lib/public/public.css',import.meta.url),'utf8');
const loader=readFileSync(new URL('../src/routes/trials/+page.server.ts',import.meta.url),'utf8');

test('first-team trials retain original Lefteria branding and closed state',()=>{
  for(const id of ['trials-page','trials-back-link','trials-headline','trials-closed'])
    assert.ok(page.includes('data-testid="'+id+'"'),id);
  assert.ok(page.includes('PublicShell'));
  assert.ok(page.includes('navigationGroups={data.navigationGroups}'));
  assert.ok(page.includes('organization="lefteria_fc"'));
  assert.doesNotMatch(page,/<main\b/);
  assert.ok(page.includes('ΠΡΩΤΗ ΟΜΑΔΑ'));
  assert.ok(page.includes('Γίνε μέλος της πρώτης ομάδας LEFTERIA FC'));
  assert.ok(page.includes('Οι εγγραφές για δοκιμαστικά είναι προσωρινά κλειστές. Παρακαλούμε δοκιμάστε ξανά σύντομα.'));
  assert.ok(page.includes('ΕΤΟΙΜΟΣ ΝΑ ΞΕΚΙΝΗΣΕΙΣ ΤΟ ΤΑΞΙΔΙ ΣΟΥ;'));
  assert.ok(css.includes('#f5a623'));
  assert.ok(css.includes("'Fira Sans'"));
  assert.ok(page.includes('href="/team"'));
  assert.ok(page.includes('href="/contact"'));
});

test('first-team trial page never suggests unverified live enrollment or collects athletes data',()=>{
  assert.ok(page.includes('δεν ζητάμε προσωπικά στοιχεία αθλητών'));
  assert.ok(page.includes('δεν εμφανίζουμε ψευδή επιβεβαίωση'));
  assert.doesNotMatch(page,/<form\b|<input\b|<textarea\b|axios|fetch\(|on:submit|onsubmit|data-testid="trials-success"/i);
  assert.doesNotMatch(loader,/POST|insert|write|twilio|grant/i);
  assert.ok(loader.includes('loadPublicSite'));
});
