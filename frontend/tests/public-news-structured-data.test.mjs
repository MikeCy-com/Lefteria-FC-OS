import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { fileURLToPath } from 'node:url';
import { compile } from 'svelte/compiler';
import { render } from 'svelte/server';
import { newsStructuredData } from '../src/lib/public/news-structured-data.ts';

const article = {
  id: 'public-news', organization_id: 'lefteria_fc', title: 'Νέα της ομάδας',
  excerpt: 'Αποτελέσματα & ενημέρωση', image_url: 'https://images.example.test/news.jpg',
  created_at: '2026-11-12T10:00:00+02:00', content: 'Body not needed',
  private_note: 'private-sentinel', author_phone: 'private-sentinel'
};
const extract = markup => {
  const match = markup.match(/<script type="application\/ld\+json">([\s\S]*?)<\/script>/);
  assert.ok(match, 'missing JSON-LD script');
  return JSON.parse(match[1]);
};

test('News structured data contains only visible verified public metadata', () => {
  const markup = newsStructuredData(article, null, true);
  const schema = extract(markup);
  assert.equal(schema['@type'], 'NewsArticle');
  assert.equal(schema.headline, article.title);
  assert.equal(schema.description, article.excerpt);
  assert.equal(schema.dateCreated, '2026-11-12T08:00:00.000Z');
  assert.deepEqual(schema.image, [article.image_url]);
  assert.match(schema.url, /^https:\/\/lefteriafc\.cy\/news\/[^?]+--public-news$/);
  assert.equal(schema.mainEntityOfPage['@id'], schema.url);
  for (const key of ['author','publisher','datePublished','dateModified','articleBody'])
    assert.ok(!(key in schema));
  assert.doesNotMatch(markup, /private-sentinel|Body not needed/);
  const scoped = extract(newsStructuredData({...article,organization_id:'lefteria_fc_academy'}, 'lefteria_fc_academy', true));
  assert.match(scoped.url, /\?organization_id=lefteria_fc_academy$/);
});

test('Suppress unavailable, nonindexable, wrong-owner and invalid identities; omit unverified fields', () => {
  for (const input of [null, {...article,id:'../unsafe'}, {...article,title:''},
    {...article,organization_id:'private-org'}])
    assert.equal(newsStructuredData(input,null,true),null);
  assert.equal(newsStructuredData(article,null,false),null);
  assert.equal(newsStructuredData(article,'lefteria_fc_academy',true),null);
  const schema = extract(newsStructuredData({...article,image_url:'/api/uploads/private.jpg',created_at:'invalid',excerpt:''},null,true));
  assert.ok(!('image' in schema)); assert.ok(!('dateCreated' in schema)); assert.ok(!('description' in schema));
});

test('Real SSR head preserves hostile text as JSON and cannot escape its script', async () => {
  const hostile = '</ScRiPt><script>alert("injection")</script>&\u2028\u2029';
  const poisoned = {...article,title:hostile,excerpt:hostile};
  const hooks = registerHooks({
    resolve(specifier,context,nextResolve) {
      if(specifier.startsWith('.') && /\/src\//.test(context.parentURL??'') && !/\.[a-z]+$/i.test(specifier)) {
        const candidate=new URL(specifier+'.ts',context.parentURL);
        if(existsSync(candidate)) return nextResolve(candidate.href,context);
      }
      return nextResolve(specifier,context);
    },
    load(url,context,nextLoad) {
      if(url.endsWith('.svelte')) {
        const filename=fileURLToPath(url);
        return {format:'module',source:compile(readFileSync(filename,'utf8'),{filename,generate:'server'}).js.code,shortCircuit:true};
      }
      return nextLoad(url,context);
    }
  });
  try {
    const {default:StructuredData}=await import('../src/lib/public/PublicNewsStructuredData.svelte');
    const head=render(StructuredData,{props:{article:poisoned,scope:null,indexable:true}}).head;
    assert.equal((head.match(/<script\b/gi)||[]).length,1);
    assert.equal((head.match(/<\/script\s*>/gi)||[]).length,1);
    assert.equal(extract(head).headline, hostile);
    assert.equal(extract(head).description, hostile);
    assert.doesNotMatch(head, /<script>alert/i);
    assert.doesNotMatch(render(StructuredData,{props:{article:poisoned,scope:null,indexable:false}}).head,/application\/ld\+json/);
    const route=readFileSync(new URL('../src/routes/news/[slug]/+page.svelte',import.meta.url),'utf8');
    assert.match(route,/article=\{data\.articleAvailable \? data\.article : null\}/);
    assert.match(route,/indexable=\{isIndexablePublicOrigin\(page\.url\)\}/);
    assert.doesNotThrow(()=>compile(route,{generate:'server'}));
    assert.doesNotThrow(()=>compile(route,{generate:'client'}));
  } finally { hooks.deregister(); }
});
