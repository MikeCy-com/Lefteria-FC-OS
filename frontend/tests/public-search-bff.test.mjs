import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { registerHooks } from 'node:module';
import { existsSync } from 'node:fs';

test('only the bounded public-news GET uses its fixed configured upstream without browser credentials', async () => {
  const requests = [];
  const server = createServer((request,response) => {
    requests.push({ url: new URL(request.url,'http://fixture.invalid'), cookie:request.headers.cookie,authorization:request.headers.authorization });
    response.setHeader('content-type','application/json');
    response.end(JSON.stringify({items:[],total:0,offset:0,limit:100}));
  });
  await new Promise(resolve => server.listen(0,'127.0.0.1',resolve));
  const origin = 'http://127.0.0.1:' + server.address().port;
  const hooks = registerHooks({resolve(specifier,context,nextResolve) {
    if(specifier === '$app/env/private') return {url:'data:text/javascript,' + encodeURIComponent('export const INTERNAL_API_ORIGIN=' + JSON.stringify(origin)),shortCircuit:true};
    if(specifier.startsWith('.') && !/\.[a-z]+$/i.test(specifier)) {
      const candidate = new URL(specifier+'.ts',context.parentURL); if(existsSync(candidate)) return nextResolve(candidate.href,context);
    }
    return nextResolve(specifier,context);
  }});
  try {
    const { GET } = await import('../src/routes/api/public/news/+server.ts');
    const invoke = query => GET({url:new URL('http://local.invalid/api/public/news'+query),request:new Request('http://local.invalid/api/public/news',{headers:{cookie:'fictional-private-cookie',authorization:'Bearer fictional-token'}})});
    assert.equal((await invoke('?limit=100')).status,200);
    assert.equal(requests[0].url.pathname,'/api/public/news');
    assert.equal(requests[0].url.searchParams.has('organization_id'),false);
    assert.equal(requests[0].cookie,undefined); assert.equal(requests[0].authorization,undefined);
    assert.equal((await invoke('?organization_id=lefteria_fc_academy&category='+encodeURIComponent('Ακαδημία')+'&offset=12&limit=12')).status,200);
    assert.equal(requests[1].url.searchParams.get('organization_id'),'lefteria_fc_academy');
    assert.equal(requests[1].url.searchParams.get('category'),'Ακαδημία');
    assert.equal(requests[1].url.searchParams.get('offset'),'12');
    for(const query of ['?organization_id=all','?organization_id=','?limit=101','?limit=0','?offset=-1','?offset=20001','?offset=1.5','?limit=1&limit=2','?url=https://attacker.invalid','?category='+ 'x'.repeat(201)])
      assert.equal((await invoke(query)).status,400,query.slice(0,50));
    assert.equal(requests.length,2,'Invalid scope/filter requests must never contact any upstream.');
  } finally { hooks.deregister();await new Promise(resolve => server.close(resolve)); }
});
