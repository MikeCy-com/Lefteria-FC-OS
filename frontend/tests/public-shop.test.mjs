import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { fileURLToPath } from 'node:url';
import { createServer } from 'node:http';
import { compile } from 'svelte/compiler';
import { render } from 'svelte/server';
import { formatShopPrice, shopTab, uniquePublishedItems } from '../src/lib/public/shop.ts';

test('shop tab normalization and financial display reject malformed prices', () => {
  assert.equal(shopTab(new URL('https://fixture.test/shop')), 'tickets');
  assert.equal(shopTab(new URL('https://fixture.test/shop?tab=merchandise')), 'merchandise');
  assert.equal(shopTab(new URL('https://fixture.test/shop?tab=unexpected')), 'tickets');
  assert.match(formatShopPrice(25), /25/);
  assert.match(formatShopPrice('9.50'), /9/);
  for (const invalid of ['not-a-price', 'Infinity', -1, 1_000_001]) {
    assert.equal(formatShopPrice(invalid), '—');
  }
});

test('shared catalog records appear once and cross-organization data is rejected', () => {
  const rows = uniquePublishedItems([
    { organization: 'lefteria_fc', items: [
      { id: 'shared', organization_id: 'lefteria_fc', name: 'FC source' },
      { id: 'wrong-owner', organization_id: 'lefteria_fc_academy', name: 'unauthorized FC row' }
    ] },
    { organization: 'lefteria_fc_academy', items: [
      { id: 'shared', organization_id: 'lefteria_fc_academy', name: 'Academy duplicate' },
      { id: 'academy-only', organization_id: 'lefteria_fc_academy', name: 'Academy product' }
    ] }
  ]);
  assert.deepEqual(rows.map(row => row.name), ['FC source', 'Academy product']);
});

test('real SSR shop uses only safe public API and exposes tickets and merchandise without checkout actions', async () => {
  const requests = [];
  let academyUnavailable = false;
  const fc = {
    organization_id: 'lefteria_fc',
    products: [{ id: 'shirt', organization_id: 'lefteria_fc', name: 'Επίσημη φανέλα',
      category: 'Εμφάνιση', description: 'Αυθεντική φανέλα', image_url: null,
      in_stock: true, price: 45, product_type: 'clothing', sizes: ['S', 'M'], delivery_options: [] },
      { id: 'foreign', organization_id: 'wrong-organization', name: 'hidden-CRM-contact',
        category: 'none', description: null, image_url: null, in_stock: true, price: 1, product_type: 'other', sizes: [], delivery_options: [] }],
    tickets: [{ id: 'match-ticket', name: 'Εισιτήριο αγώνα', ticket_type: 'match',
      organization_id: 'lefteria_fc', price: 12, available: true,
      description: 'Διαθέσιμο στον κατάλογο', fixture_id: null, max_quantity: 4 }]
  };
  const academy = {
    organization_id: 'lefteria_fc_academy',
    products: [{ ...fc.products[0], id: 'academy-product', organization_id: 'lefteria_fc_academy', name: '<script>unsafe</script>', price: '16' }],
    tickets: [{ ...fc.tickets[0], id: 'academy-ticket', organization_id: 'lefteria_fc_academy', name: 'Ακαδημία εισιτήριο' }]
  };
  const server = createServer((request, response) => {
    const url = new URL(request.url, 'http://fixture.test');
    requests.push({ path: url.pathname, org: url.searchParams.get('organization_id'),
      cookie: request.headers.cookie, authorization: request.headers.authorization });
    const org = url.searchParams.get('organization_id');
    response.setHeader('content-type', 'application/json');
    if (url.pathname === '/api/public/site') {
      const site = { organization_id: org, club: null, academy_groups: [], teams: [], fixtures: [], news: [] };
      response.end(JSON.stringify(site)); return;
    }
    if (url.pathname === '/api/public/sections') {
      if (org === 'lefteria_fc_academy' && academyUnavailable) {
        response.statusCode = 503; response.end(JSON.stringify({detail:'unavailable'})); return;
      }
      response.end(JSON.stringify(org === 'lefteria_fc_academy' ? academy : fc)); return;
    }
    response.statusCode = 404; response.end('{}');
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const origin = 'http://127.0.0.1:' + server.address().port;
  registerHooks({
    resolve(specifier, context, nextResolve) {
      if (specifier === '$app/state')
        return { url: 'data:text/javascript,' + encodeURIComponent('export const page={url:new URL("https://fixture.test/shop")};'), shortCircuit:true };
      if (specifier === '$app/env/private')
        return { url: 'data:text/javascript,' + encodeURIComponent('export const INTERNAL_API_ORIGIN=' + JSON.stringify(origin)), shortCircuit:true };
      if (specifier.startsWith('.') && /\/src\//.test(context.parentURL ?? '') && !/\.[a-z]+$/i.test(specifier)) {
        const candidate = new URL(specifier + '.ts', context.parentURL);
        if (existsSync(candidate)) return nextResolve(candidate.href, context);
      }
      return nextResolve(specifier, context);
    },
    load(url, context, nextLoad) {
      if (url.endsWith('.css')) return {format:'module',source:'export {};',shortCircuit:true};
      if (url.endsWith('.svelte')) {
        const filename = fileURLToPath(url);
        return {format:'module',source:compile(readFileSync(filename,'utf8'),{filename,generate:'server'}).js.code,shortCircuit:true};
      }
      return nextLoad(url, context);
    }
  });
  try {
    const { load } = await import('../src/routes/shop/+page.server.ts');
    const { default: Shop } = await import('../src/routes/shop/+page.svelte');
    const tickets = await load({url:new URL('https://fixture.test/shop')});
    assert.equal(tickets.tab, 'tickets');
    assert.equal(tickets.catalogAvailable, true);
    assert.equal(tickets.tickets.length, 2);
    assert.equal(tickets.products.length, 2);
    const ticketsHTML = render(Shop, { props: {data:tickets} }).body;
    assert.match(ticketsHTML, /data-testid="tickets-section"/);
    assert.match(ticketsHTML, /Εισιτήριο αγώνα/);
    assert.match(ticketsHTML, /Under Developments/);
    assert.doesNotMatch(ticketsHTML, /data-testid="merchandise-section"|hidden-CRM-contact|<script>unsafe/);

    const merchandise = await load({url:new URL('https://fixture.test/shop?tab=merchandise')});
    const html = render(Shop, {props:{data:merchandise}}).body;
    assert.match(html, /data-testid="merchandise-section"/);
    assert.match(html, /Επίσημη φανέλα/);
    assert.match(html, /Αυθεντική φανέλα/);
    assert.match(html, /ΣΕ ΑΠΟΘΕΜΑ/);
    assert.match(html, /&lt;script>unsafe&lt;\/script>/);
    assert.doesNotMatch(html, /hidden-CRM-contact|data-testid="tickets-section"|checkout-submit/);
    assert.ok(requests.length > 0);
    assert.ok(requests.every(row => row.cookie === undefined && row.authorization === undefined));
    assert.ok(requests.every(row => ['/api/public/site','/api/public/sections'].includes(row.path)));
    assert.ok(requests.every(row => ['lefteria_fc','lefteria_fc_academy'].includes(row.org)));

    academyUnavailable = true;
    const outage = await load({url:new URL('https://fixture.test/shop?tab=merchandise')});
    assert.equal(outage.catalogAvailable, false);
    const unavailableHTML = render(Shop, {props:{data:outage}}).body;
    assert.match(unavailableHTML, /data-testid="public-content-unavailable"/);
    assert.doesNotMatch(unavailableHTML, /Δεν υπάρχουν δημοσιευμένα προϊόντα/);
  } finally {
    await new Promise(resolve => server.close(resolve));
  }
});
