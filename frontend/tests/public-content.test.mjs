import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { fileURLToPath } from 'node:url';
import { createServer } from 'node:http';
import { compile } from 'svelte/compiler';
import { render } from 'svelte/server';
import { articleLink, greekDate, inlineTokens, markdownBlocks, newsLink, safePublicHref } from '../src/lib/public/content.ts';

test('public human-readable news links preserve opaque IDs and explicit organization', () => {
  const article = { id: 'case-sensitive-ID', title: 'Νέα Ομάδας!', organization_id: 'lefteria_fc_academy' };
  assert.equal(articleLink(article), '/news/νεα-ομαδας--case-sensitive-ID');
  assert.equal(articleLink(article, 'lefteria_fc_academy'), '/news/νεα-ομαδας--case-sensitive-ID?organization_id=lefteria_fc_academy');
  assert.equal(newsLink(null), '/news');
  const filtered = new URL(newsLink('lefteria_fc_academy', 'Ακαδημία', 3), 'https://example.test');
  assert.equal(filtered.searchParams.get('organization_id'), 'lefteria_fc_academy');
  assert.equal(filtered.searchParams.get('category'), 'Ακαδημία');
  assert.equal(filtered.searchParams.get('page'), '3');
  assert.equal(new URL(newsLink(null, 'Ακαδημία', 2), 'https://example.test').searchParams.has('organization_id'), false);
  assert.equal(greekDate('invalid-date'), '');
  assert.match(greekDate('2026-11-01'), /1 Νοεμβρίου 2026/);
});

test('public Markdown permits safe links and preserves text without executing HTML or unsafe protocols', () => {
  for (const unsafe of ['javascript:alert(1)', 'data:text/html,test', '//other.example', 'https://user:pass@example.test', '/api/admin/users', '/api/uploads/player_docs/private.pdf']) {
    assert.equal(safePublicHref(unsafe), null);
  }
  assert.equal(safePublicHref('https://example.test/story'), 'https://example.test/story');
  assert.equal(safePublicHref('/news/story--id'), '/news/story--id');
  assert.deepEqual(inlineTokens('[unsafe](javascript:alert) **bold**'), [{ kind: 'text', text: 'unsafe' }, { kind: 'text', text: ' ' }, { kind: 'strong', text: 'bold' }]);
  assert.equal(markdownBlocks('# Title\n\n- One\n- Two\n\n> Quote')[1].kind, 'list');
  const malformed = '['.repeat(100000);
  assert.deepEqual(inlineTokens(malformed), [{ kind: 'text', text: malformed }]);
});

function installPublicModuleHooks(origin) {
  return registerHooks({
    resolve(specifier, context, nextResolve) {
      if (specifier === '$app/env/private' && origin) return { url: 'data:text/javascript,' + encodeURIComponent('export const INTERNAL_API_ORIGIN=' + JSON.stringify(origin)), shortCircuit: true };
      if (specifier.startsWith('.') && /\/src\/(?:lib\/(?:public|server|api)|routes)\//.test(context.parentURL ?? '') && !/\.[a-z]+$/i.test(specifier)) {
        const candidate = new URL(specifier + '.ts', context.parentURL);
        if (existsSync(candidate)) return nextResolve(candidate.href, context);
      }
      return nextResolve(specifier, context);
    },
    load(url, context, nextLoad) {
      if (url.endsWith('.css')) return { format: 'module', source: 'export {};', shortCircuit: true };
      if (url.endsWith('.svelte')) {
        const filename = fileURLToPath(url);
        return { format: 'module', source: compile(readFileSync(filename, 'utf8'), { filename, generate: 'server' }).js.code, shortCircuit: true };
      }
      return nextLoad(url, context);
    }
  });
}

test('rendered public article and cards escape source text and do not invent unpublished content', async () => {
  const hooks = installPublicModuleHooks();
  try {
    const { default: Markdown } = await import('../src/lib/public/MarkdownContent.svelte');
    const html = render(Markdown, { props: { content: '# Safe heading\n\n<script>alert(1)</script> **Strong** [Unsafe](javascript:alert)' } }).body;
    assert.match(html, /Safe heading/);
    assert.match(html, /&lt;script>/);
    assert.doesNotMatch(html, /<script>|href="javascript:/);
    assert.match(html, /<strong>Strong<\/strong>/);
    const { default: Cards } = await import('../src/lib/public/NewsCards.svelte');
    assert.doesNotMatch(render(Cards, { props: { articles: [] } }).body, /Fixture|news-card-body/);
    const cards = render(Cards, { props: { articles: [{ id: 'article', organization_id: 'lefteria_fc', title: '<script>title</script>', excerpt: 'Recorded excerpt', content: '', image_url: null, category: 'Νέα', audience: 'first_team', is_featured: false, created_at: null }] } }).body;
    assert.match(cards, /Recorded excerpt/);
    assert.doesNotMatch(cards, /<script>title/);
    const { default: Unavailable } = await import('../src/lib/public/Unavailable.svelte');
    assert.match(render(Unavailable).body, /public-content-unavailable/);
  } finally { hooks.deregister(); }
});

test('public SSR keeps global news and Academy navigation separate from FC site data without forwarding credentials', async () => {
  const first = { id: 'first', organization_id: 'lefteria_fc', title: 'Fixture FC article', excerpt: '', content: '', image_url: null, category: 'Νέα', audience: 'first_team', is_featured: false, created_at: '2026-10-31T12:00:00Z' };
  const academyArticle = { ...first, id: 'academy-only', organization_id: 'lefteria_fc_academy', title: 'Fixture Academy global article', category: 'Ακαδημία', audience: 'academy', created_at: '2026-11-01T12:00:00Z' };
  const separatorArticle = { ...first, id: 'record--legacy', title: 'Recorded article' };
  const group = { id: 'academy-group', organization_id: 'lefteria_fc_academy', name: 'Fixture public Academy group', age_range: 'U12', description: '', training_schedule: '', banner_url: null };
  const fcSite = { organization_id: 'lefteria_fc', club: null, teams: [], academy_groups: [], fixtures: [], news: [first] };
  const academySite = { ...fcSite, organization_id: 'lefteria_fc_academy', academy_groups: [group], news: [academyArticle] };
  const requests = [];
  const server = createServer((request, response) => {
    requests.push({ url: new URL(request.url, 'http://fixture.test'), cookie: request.headers.cookie, authorization: request.headers.authorization });
    const url = requests.at(-1).url;
    response.setHeader('content-type', 'application/json');
    if (url.pathname === '/api/public/site') response.end(JSON.stringify(url.searchParams.get('organization_id') === 'lefteria_fc_academy' ? academySite : fcSite));
    else if (url.pathname === '/api/public/news/academy-only') response.end(JSON.stringify(academyArticle));
    else if (url.pathname === '/api/public/news/record--legacy') response.end(JSON.stringify(separatorArticle));
    else if (url.pathname === '/api/public/news') response.end(JSON.stringify({ items: [academyArticle], total: 30, offset: Number(url.searchParams.get('offset')), limit: Number(url.searchParams.get('limit')) }));
    else { response.statusCode = 404; response.end('{}'); }
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const hooks = installPublicModuleHooks('http://127.0.0.1:' + server.address().port);
  try {
    const { loadPublicHome, loadPublicNews, loadPublicArticle } = await import('../src/lib/server/public-content.ts');
    const home = await loadPublicHome(new URL('https://preview.test/'));
    assert.equal(home.available, true, JSON.stringify(requests));
    assert.equal(home.organization, 'lefteria_fc');
    assert.deepEqual(home.site.academy_groups, []);
    assert.equal(home.site.news[0].id, 'first');
    assert.equal(home.news.items[0].id, 'academy-only');
    assert.equal(home.navigationGroups[0].id, 'academy-group');
    assert.equal(requests.find(row => row.url.pathname === '/api/public/news').url.searchParams.has('organization_id'), false);
    const { default: Home } = await import('../src/routes/+page.svelte');
    const homeHtml = render(Home, { props: { data: home } }).body;
    assert.match(homeHtml, /Fixture Academy global article/);
    assert.doesNotMatch(homeHtml, /Fixture FC article/);
    assert.match(homeHtml, /href="\/academy#group-academy-group"/);
    assert.match(homeHtml, /href="\/news\/fixture-academy-global-article--academy-only"/);
    const news = await loadPublicNews(new URL('https://preview.test/news?category=' + encodeURIComponent('Ακαδημία') + '&page=2'));
    assert.equal(news.newsScope, null);
    const newsRequest = requests.filter(row => row.url.pathname === '/api/public/news').at(-1).url;
    assert.equal(newsRequest.searchParams.has('organization_id'), false);
    assert.equal(newsRequest.searchParams.get('category'), 'Ακαδημία');
    assert.equal(newsRequest.searchParams.get('offset'), '12');
    const { default: News } = await import('../src/routes/news/+page.svelte');
    const newsHtml = render(News, { props: { data: news } }).body;
    assert.match(newsHtml, />ΟΛΑ<\/a>/);
    for (const category of ['Νέα', 'Αποτελέσματα', 'Μεταγραφές', 'Ακαδημία']) assert.ok(newsHtml.includes(category));
    const nextLink = [...newsHtml.matchAll(/href="([^"]+)"/g)].map(match => match[1].replaceAll('&amp;', '&')).find(href => href.includes('page=3'));
    const nextUrl = new URL(nextLink, 'https://preview.test');
    assert.equal(nextUrl.searchParams.get('category'), 'Ακαδημία');
    assert.equal(nextUrl.searchParams.has('organization_id'), false);
    const scoped = await loadPublicNews(new URL('https://preview.test/news?organization_id=lefteria_fc_academy'));
    assert.equal(scoped.newsScope, 'lefteria_fc_academy');
    assert.equal(requests.filter(row => row.url.pathname === '/api/public/news').at(-1).url.searchParams.get('organization_id'), 'lefteria_fc_academy');
    const article = await loadPublicArticle(new URL('https://preview.test/news/fixture--academy-only'), 'fixture--academy-only');
    assert.equal(article.newsScope, null);
    assert.equal(article.article.id, 'academy-only');
    assert.equal(requests.find(row => row.url.pathname === '/api/public/news/academy-only').url.searchParams.has('organization_id'), false);
    const generatedPath = articleLink(separatorArticle);
    const roundtrip = await loadPublicArticle(new URL('https://preview.test' + generatedPath), decodeURIComponent(generatedPath.slice('/news/'.length)));
    assert.equal(roundtrip.article.id, 'record--legacy');
    const legacyArticle = await loadPublicArticle(new URL('https://preview.test/news/record--legacy'), 'record--legacy');
    assert.equal(legacyArticle.article.id, 'record--legacy');
    assert.equal(requests.some(row => row.url.pathname === '/api/public/news/legacy'), false);
    assert.ok(requests.every(row => row.cookie === undefined && row.authorization === undefined));
    await assert.rejects(loadPublicNews(new URL('https://preview.test/news?organization_id=all')), error => error.status === 400);
  } finally { hooks.deregister(); await new Promise(resolve => server.close(resolve)); }
});
