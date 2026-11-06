import type { RequestHandler } from './$types';
import { isIndexablePublicOrigin, robotsFor } from '../../lib/public/seo';

// No untrusted Host header is echoed. The SvelteKit event URL comes from
// the configured ORIGIN in production; all non-public origins are disallowed.
export const GET: RequestHandler = ({ url }) => {
  const indexable = isIndexablePublicOrigin(url);
  return new Response(robotsFor(url), {
    status: 200,
    headers: {
      'content-type': 'text/plain; charset=utf-8',
      'cache-control': indexable ? 'public, max-age=3600' : 'no-store',
      ...(indexable ? {} : { 'x-robots-tag': 'noindex, nofollow' })
    }
  });
};
