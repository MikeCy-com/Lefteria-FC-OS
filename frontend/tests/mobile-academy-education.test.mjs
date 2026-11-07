import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { registerHooks } from 'node:module';
import { compile } from 'svelte/compiler';
import { render } from 'svelte/server';
import {
  ACADEMY_EDUCATION_ARTICLES, EDUCATION_STAGES, EDUCATION_TOPICS,
  academyEducationFor
} from '../src/lib/mobile/academy-education.ts';
import { allowedTabs, normalizeTab } from '../src/lib/mobile/navigation.ts';

test('education materials cover four stages and both family audiences without sensitive data',()=>{
  assert.equal(EDUCATION_STAGES.length,4);
  assert.equal(EDUCATION_TOPICS.length,4);
  assert.equal(ACADEMY_EDUCATION_ARTICLES.length,8);
  const ids=new Set();
  for(const stage of EDUCATION_STAGES){
    const articles=academyEducationFor(stage.id);
    assert.equal(articles.length,2,stage.title);
    assert.equal(academyEducationFor(stage.id,'parent').length,1);
    assert.equal(academyEducationFor(stage.id,'player').length,1);
  }
  assert.equal(academyEducationFor('10-12','all','psychology').length,1);
  assert.equal(academyEducationFor('10-12','parent','psychology').length,0);
  assert.equal(academyEducationFor('10-12','parent','communication').length,1);
  for(const article of ACADEMY_EDUCATION_ARTICLES){
    assert.ok(!ids.has(article.id));ids.add(article.id);
    assert.ok(article.title.length>6&&article.summary.length>15);
    assert.equal(article.points.length,3);
    assert.ok(article.points.every(point=>point.length>15));
    assert.ok(/[?;;]$/.test(article.discussion), 'Greek question may use semicolon punctuation');
    assert.doesNotMatch(JSON.stringify(article),
      /phone|medical_record|private_token|student_id|student_number|password|api_key/i);
  }
  assert.equal(allowedTabs('parent').includes('education'),true);
  for(const role of ['fan','player','coach','management'])
    assert.equal(allowedTabs(role).includes('education'),false);
  assert.equal(normalizeTab('education','parent'),'education');
  for(const role of ['fan','player','coach','management'])
    assert.equal(normalizeTab('education',role),'home');
});

registerHooks({
  resolve(specifier,context,nextResolve){
    if(specifier.endsWith('.css'))
      return {url:'data:text/javascript,export%20default%20%7B%7D',shortCircuit:true};
    return nextResolve(specifier,context);
  },
  load(url,context,nextLoad){
    if(url.endsWith('.svelte')){
      const filename=fileURLToPath(url);
      return {format:'module',source:compile(readFileSync(filename,'utf8'),
        {filename,generate:'server'}).js.code,shortCircuit:true};
    }
    return nextLoad(url,context);
  }
});

test('Academy education SSR includes age filters, educational disclaimers and safe editorial only',async()=>{
  const {default:Education}=await import('../src/lib/mobile/components/MobileAcademyEducation.svelte');
  const html=render(Education).body;
  for(const value of ['parent-education-hub','education-editorial-status','education-materials',
    '6–9 ετών','10–12 ετών','13–15 ετών','16–18 ετών',
    'Μαθαίνουμε μαζί','Για γονείς','Για αθλητές','Όλα τα θέματα','Διάβασε περισσότερα',
    'επίσημη δημοσίευση']){
    assert.ok(html.includes(value),value);
  }
  assert.equal((html.match(/data-testid="education-article"/g)||[]).length,2);
  assert.match(html,/class="education-result-count[^"]*"[^>]*role="status"[^>]*aria-live="polite"/);
  assert.match(html,/class="education-stage-hint[^"]*" aria-live="polite"/);
  assert.doesNotMatch(html,/data-testid="parent-home-child"|family-child-calendar|guardian_phone|Bearer/);
  const route=readFileSync(new URL('../src/routes/app/[...path]/+page.svelte',import.meta.url),'utf8');
  assert.match(route,/activeTab === 'education' && role === 'parent' && !composing/);
  assert.match(route,/<MobileAcademyEducation \/>/);
  assert.match(route,/onEducation=\{\(\) => openTab\('education'\)\}/);
  const home=readFileSync(new URL('../src/lib/mobile/components/MobileParentHome.svelte',import.meta.url),'utf8');
  assert.match(home,/data-testid="parent-shortcut-education"/);
  assert.match(home,/onEducation\(\)/);
});

test('first-party education has no unauthorized Academy approval or fake publish backend',()=>{
  const text=readFileSync(new URL('../src/lib/mobile/components/MobileAcademyEducation.svelte',import.meta.url),'utf8');
  assert.match(text,/Δεν αποτελεί|δεν αποτελεί/);
  assert.match(text,/θα προστεθούν μετά από έλεγχο περιεχομένου και δικαιωμάτων/);
  assert.doesNotMatch(text,/\/api\/|fetch\(|onMount\(|createClient\(|player_id|date_of_birth/);
});
