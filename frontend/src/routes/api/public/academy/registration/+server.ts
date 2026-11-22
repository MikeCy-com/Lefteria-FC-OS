import { INTERNAL_API_ORIGIN } from '$app/env/private';
import { json } from '@sveltejs/kit';
import type { RequestHandler } from './$types';
import {parseAcademyRegistrationReceipts} from '../../../../../lib/academy/registration-receipts';

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
      // Preserve PR #463's strict receipt validation (including sparse,
      // duplicate and mismatched sibling IDs) before accepting upload tokens.
      const verified = parseAcademyRegistrationReceipts(result);
      if (verified && Array.isArray(result?.upload_tokens) &&
        result.upload_tokens.length === verified.receipts.length &&
        Array.from({length: result.upload_tokens.length}, (_, i) => i)
          .every(index => Object.hasOwn(result.upload_tokens, index) &&
            typeof result.upload_tokens[index] === 'string' &&
            /^[0-9a-f]{64}$/.test(result.upload_tokens[index])) &&
        new Set(result.upload_tokens).size === result.upload_tokens.length)
        return json({...verified, upload_tokens: result.upload_tokens},
          {status: 202, headers});
    }
    const status = [400, 403, 413, 415, 422, 429].includes(response.status) ? response.status : 503;
    return json({ code: status === 429 ? 'rate_limited' : status === 422 ? 'invalid_registration' : 'registration_unavailable' }, { status, headers });
  } catch {
    return json({ code: 'registration_unavailable' }, { status: 503, headers });
  }
};
