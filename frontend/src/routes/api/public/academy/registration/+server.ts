import { INTERNAL_API_ORIGIN } from '$app/env/private';
import { json } from '@sveltejs/kit';
import type { RequestHandler } from './$types';

// Same-origin web adapter: no client-selected upstream, no cookies or
// Authorization forwarded, no sensitive form data logged or stored in SSR.
export const POST: RequestHandler = async ({ request, url }) => {
  const headers = { 'cache-control': 'private, no-store', pragma: 'no-cache' };
  if (request.headers.get('origin') !== url.origin)
    return json({ code: 'origin_denied' }, { status: 403, headers });
  if (request.headers.get('content-type')?.split(';', 1)[0]?.trim() !== 'application/json')
    return json({ code: 'json_required' }, { status: 415, headers });
  if (Number(request.headers.get('content-length') ?? '0') > 131072)
    return json({ code: 'payload_too_large' }, { status: 413, headers });

  // Bound even chunked requests without a Content-Length. Do not stringify
  // or parse the sensitive payload in SvelteKit; the API validates it.
  const reader = request.body?.getReader();
  if (!reader) return json({ code: 'invalid_json' }, { status: 400, headers });
  const chunks: Uint8Array[] = [];
  let size = 0;
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      size += value.byteLength;
      if (size > 131072) return json({ code: 'payload_too_large' }, { status: 413, headers });
      chunks.push(value);
    }
    const apiOrigin = new URL(INTERNAL_API_ORIGIN);
    if (!['http:', 'https:'].includes(apiOrigin.protocol) ||
      apiOrigin.username || apiOrigin.password ||
      apiOrigin.pathname !== '/' || apiOrigin.search || apiOrigin.hash)
      throw new Error('Invalid internal API configuration.');
    const body = new Uint8Array(size);
    let offset = 0;
    for (const chunk of chunks) { body.set(chunk, offset); offset += chunk.length; }
    const response = await fetch(new URL('/api/public/academy/registration', apiOrigin), {
      method: 'POST', credentials: 'omit', cache: 'no-store',
      headers: { origin: url.origin, 'content-type': 'application/json' },
      body, signal: AbortSignal.timeout(12000)
    });
    // Only commit-backed receipts may be shown as success, and never echo
    // server internals or user-submitted medical data to the browser.
    if (response.status === 202) {
      const result = await response.json().catch(() => null);
      if (typeof result?.receipt === 'string' && /^[0-9a-f]{32}$/.test(result.receipt) &&
        Array.isArray(result?.receipts) && result.receipts.length>=1 &&
        result.receipts.length<=5 && result.receipts[0]===result.receipt &&
        result.receipts.every((id:unknown)=>typeof id==='string' && /^[0-9a-f]{32}$/.test(id)) &&
        Array.isArray(result.upload_tokens) &&
        result.upload_tokens.length===result.receipts.length &&
        result.upload_tokens.every((token:unknown)=>typeof token==='string' &&
          /^[0-9a-f]{64}$/.test(token)) &&
        new Set(result.upload_tokens).size===result.upload_tokens.length)
        return json({receipt:result.receipt,receipts:result.receipts,
          upload_tokens:result.upload_tokens},{status:202,headers});
    }
    const status = [400, 403, 413, 415, 422, 429].includes(response.status) ? response.status : 503;
    return json({ code: status === 429 ? 'rate_limited' : status === 422 ? 'invalid_registration' : 'registration_unavailable' }, { status, headers });
  } catch {
    return json({ code: 'registration_unavailable' }, { status: 503, headers });
  }
};
