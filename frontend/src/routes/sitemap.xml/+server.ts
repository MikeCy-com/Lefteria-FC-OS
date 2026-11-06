import type { RequestHandler } from './$types';
import { publicApi } from '../../lib/server/public-content';
import { buildPublicSitemap, isIndexablePublicOrigin } from '../../lib/public/seo';
import type { PublicArticle } from '../../lib/public/types';

const MAX_NEWS = 500;
const PAGE_SIZE = 100;

async function readPublicNews(api: ReturnType<typeof publicApi>): Promise<PublicArticle[]> {
  const all: PublicArticle[] = [];
  let total: number | null = null;
  for (let offset = 0; offset < MAX_NEWS; offset += PAGE_SIZE) {
    const response = await api.GET('/api/public/news', {
      params: { query: { offset, limit: PAGE_SIZE } }
    });
    if (response.response.status !== 200 || !response.data)
      throw new Error('Public news projection unavailable');
    const claimed = Number(response.data.total);
    if (!Number.isSafeInteger(claimed) || claimed < 0)
      throw new Error('Invalid public news count');
    if (total === null) total = claimed;
    if (claimed !== total || response.data.items.length !== Math.min(PAGE_SIZE, total - offset))
      throw new Error('Inconsistent public news projection');
    all.push(...response.data.items);
    if (offset + PAGE_SIZE >= total) break;
  }
  return all;
}

export const GET: RequestHandler = async ({ url }) => {
  // An isolated preview cannot accidentally emit production URLs, even when
  // it has a full copy of the safe published content.
  if (!isIndexablePublicOrigin(url)) {
    return new Response('Not Found', {
      status: 404,
      headers: { 'cache-control': 'no-store', 'x-robots-tag': 'noindex, nofollow' }
    });
  }

  try {
    const api = publicApi();
    const [fcSite, academySite, fcSections, academySections, articles] = await Promise.all([
      api.GET('/api/public/site', { params: { query: { organization_id: 'lefteria_fc' } } }),
      api.GET('/api/public/site', { params: { query: { organization_id: 'lefteria_fc_academy' } } }),
      api.GET('/api/public/sections', { params: { query: { organization_id: 'lefteria_fc' } } }),
      api.GET('/api/public/sections', { params: { query: { organization_id: 'lefteria_fc_academy' } } }),
      readPublicNews(api)
    ]);
    if (fcSite.response.status !== 200 || fcSite.data?.organization_id !== 'lefteria_fc'
      || academySite.response.status !== 200 || academySite.data?.organization_id !== 'lefteria_fc_academy'
      || fcSections.response.status !== 200 || fcSections.data?.organization_id !== 'lefteria_fc'
      || academySections.response.status !== 200 || academySections.data?.organization_id !== 'lefteria_fc_academy') {
      throw new Error('Public projection unavailable');
    }

    return new Response(buildPublicSitemap({
      articles,
      groups: academySite.data.academy_groups,
      sites: [fcSite.data, academySite.data],
      sections: [fcSections.data, academySections.data]
    }), {
      status: 200,
      headers: {
        'content-type': 'application/xml; charset=utf-8',
        'cache-control': 'public, max-age=300'
      }
    });
  } catch {
    // Do not advertise a misleading empty sitemap during an outage; never
    // leak upstream/DB exceptions or any underlying source document content.
    return new Response('Public sitemap temporarily unavailable', {
      status: 503,
      headers: { 'cache-control': 'no-store', 'x-robots-tag': 'noindex' }
    });
  }
};
