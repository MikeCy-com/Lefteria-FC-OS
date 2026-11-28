import { INTERNAL_API_ORIGIN } from '$app/env/private';
import type { PublicOrganization } from '../public/types';
import { isPublishedWebsitePage, type PublishedWebsitePage } from '../public/website-pages';

// No incoming browser request, cookie or authorization header enters this public reader.
// Navigation may degrade gracefully; a crawler sitemap must distinguish unavailable
// or invalid projections from a genuine successful empty collection.
export async function readWebsitePages(
  org: PublicOrganization, { strict = false }: { strict?: boolean } = {}
): Promise<PublishedWebsitePage[]> {
  try {
    const origin = new URL(INTERNAL_API_ORIGIN);
    if (!['http:', 'https:'].includes(origin.protocol) || origin.username || origin.password ||
        origin.pathname !== '/' || origin.search || origin.hash)
      throw new Error('Invalid public CMS origin');
    const response = await fetch(origin.origin + '/api/public/website/pages?organization_id=' + org, {
      headers: {Accept: 'application/json'}, cache: 'no-store',
      redirect: 'error', signal: AbortSignal.timeout(3000)
    });
    if (!response.ok) throw new Error('Public CMS projection unavailable');
    const data: unknown = await response.json();
    if (!Array.isArray(data) || data.length > 100)
      throw new Error('Invalid public CMS collection');
    const pages = data.filter(item => isPublishedWebsitePage(item, org));
    if (strict && (pages.length !== data.length ||
        new Set(pages.map(page => page.slug)).size !== pages.length))
      throw new Error('Inconsistent public CMS projection');
    return pages;
  } catch {
    if (strict) throw new Error('Public CMS projection unavailable');
    return [];
  }
}
