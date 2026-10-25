import test from 'node:test';
import assert from 'node:assert/strict';
import { createApiClient } from '../src/lib/api/client.ts';

test('generated client uses the API path and typed successful response', async () => {
  const client = createApiClient({ baseUrl: 'https://club.example', fetch: async (request) => {
    assert.equal(request.url, 'https://club.example/api/health/live');
    assert.equal(request.method, 'GET');
    assert.equal(request.cache, 'no-store');
    return Response.json({ status: 'live' });
  }});
  const result = await client.GET('/api/health/live');
  assert.deepEqual(result.data, { status: 'live' });
  assert.equal(result.error, undefined);
});

test('unavailable database remains a 503 error response, not success data', async () => {
  const client = createApiClient({ baseUrl: 'https://club.example', fetch: async () =>
    Response.json({ status: 'unavailable' }, { status: 503 })
  });
  const result = await client.GET('/api/health/ready');
  assert.equal(result.response.status, 503);
  assert.equal(result.data, undefined);
  assert.deepEqual(result.error, { status: 'unavailable' });
});
