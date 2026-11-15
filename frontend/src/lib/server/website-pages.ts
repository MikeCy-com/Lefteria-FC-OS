import { INTERNAL_API_ORIGIN } from '$app/env/private';
import type { PublicOrganization } from '../public/types';
import { isPublishedWebsitePage, type PublishedWebsitePage } from '../public/website-pages';

// No incoming browser request, cookie or authorization header enters this public reader.
export async function readWebsitePages(org: PublicOrganization): Promise<PublishedWebsitePage[]> {
  const origin = new URL(INTERNAL_API_ORIGIN);
  if (!['http:', 'https:'].includes(origin.protocol) || origin.username || origin.password ||
      origin.pathname !== '/' || origin.search || origin.hash) return [];
  try {
    const response = await fetch(origin.origin + '/api/public/website/pages?organization_id=' + org, {
      headers: {Accept: 'application/json'}, cache: 'no-store',
      signal: AbortSignal.timeout(3000)
    });
    if (!response.ok) return [];
    const data: unknown = await response.json();
    return Array.isArray(data) && data.length <= 100 ?
      data.filter(item => isPublishedWebsitePage(item, org)) : [];
  } catch { return []; }
}
