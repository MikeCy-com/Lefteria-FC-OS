import { INTERNAL_API_ORIGIN } from '$app/env/private';
import { error } from '@sveltejs/kit';
import { createApiClient } from '../api/client';
import type { PublicArticle, PublicHomeData, PublicNews, PublicNewsScope, PublicOrganization, PublicPageData, PublicSite } from '../public/types';

export function publicOrganization(url: URL, fixed?: PublicOrganization): PublicOrganization {
  const value = fixed ?? url.searchParams.get('organization_id') ?? 'lefteria_fc';
  if (value !== 'lefteria_fc' && value !== 'lefteria_fc_academy') error(400, 'Μη έγκυρος οργανισμός.');
  return value;
}

export function publicNewsScope(url: URL): PublicNewsScope {
  if (!url.searchParams.has('organization_id')) return null;
  return publicOrganization(url);
}

function publicApi() {
  const origin = new URL(INTERNAL_API_ORIGIN);
  if (!['http:', 'https:'].includes(origin.protocol) || origin.username || origin.password || origin.pathname !== '/') throw new Error('Invalid internal API origin.');
  return createApiClient({ baseUrl: origin.origin, fetch: (input, init) =>
    // No browser RequestEvent/cookies/Authorization enters this server client.
    fetch(input, { ...init, headers: { accept: 'application/json' }, credentials: 'omit', cache: 'no-store', signal: AbortSignal.timeout(3000) }) });
}
async function requestSite(organization: PublicOrganization): Promise<{ value: PublicSite | null; status: number }> {
  try {
    const result = await publicApi().GET('/api/public/site', { params: { query: { organization_id: organization } } });
    return { value: result.data ?? null, status: result.response.status };
  } catch { return { value: null, status: 503 }; }
}
async function requestNews(organization: PublicNewsScope, offset: number, category: string, limit = 12): Promise<{ value: PublicNews | null; status: number }> {
  try {
    const result = await publicApi().GET('/api/public/news', { params: { query: { ...(organization ? { organization_id: organization } : {}), offset, limit, ...(category ? { category } : {}) } } });
    return { value: result.data ?? null, status: result.response.status };
  } catch { return { value: null, status: 503 }; }
}
async function requestArticle(organization: PublicNewsScope, id: string): Promise<{ value: PublicArticle | null; status: number }> {
  try {
    const result = await publicApi().GET('/api/public/news/{id}', { params: { path: { id }, ...(organization ? { query: { organization_id: organization } } : {}) } });
    return { value: result.data ?? null, status: result.response.status };
  } catch { return { value: null, status: 503 }; }
}

export async function loadPublicSite(url: URL, fixed?: PublicOrganization): Promise<PublicPageData> {
  const organization = publicOrganization(url, fixed);
  const resultTask = requestSite(organization);
  // Shared public navigation needs Academy groups even on an FC page. They
  // remain a separate explicit public request, never injected into FC site data.
  const navigationTask = organization === 'lefteria_fc_academy' ? resultTask : requestSite('lefteria_fc_academy');
  const [result, navigation] = await Promise.all([resultTask, navigationTask]);
  return { site: result.value, available: result.status === 200 && result.value !== null, organization,
    navigationGroups: navigation.value?.academy_groups ?? [] };
}

export async function loadPublicHome(url: URL): Promise<PublicHomeData> {
  const [site, news] = await Promise.all([loadPublicSite(url), requestNews(null, 0, '', 3)]);
  return { ...site, news: news.value, newsAvailable: news.status === 200 && news.value !== null };
}

export async function loadPublicNews(url: URL) {
  const newsScope = publicNewsScope(url);
  const raw = url.searchParams.get('page') ?? '1';
  if (!/^[1-9][0-9]{0,3}$/.test(raw)) error(400, 'Μη έγκυρη σελίδα.');
  const page = Number(raw);
  if (page > 1667) error(400, 'Μη έγκυρη σελίδα.');
  const category = url.searchParams.get('category') ?? '';
  if (category.length > 200) error(400, 'Μη έγκυρη κατηγορία.');
  const [site, news] = await Promise.all([
    loadPublicSite(url), requestNews(newsScope, (page - 1) * 12, category)
  ]);
  return { ...site, newsScope, news: news.value, newsAvailable: news.status === 200 && news.value !== null, page, category };
}

export async function loadPublicArticle(url: URL, slug: string) {
  const newsScope = publicNewsScope(url);
  if (!slug || slug.length > 210) error(404, 'Το άρθρο δεν βρέθηκε.');
  // Existing opaque IDs may contain the separator. Resolve the exact legacy
  // component first; generated title slugs contain only single hyphens.
  const separator = slug.indexOf('--');
  const fallback = separator > 0 ? slug.slice(separator + 2) : '';
  const candidates = [...new Set([slug, fallback].filter(id => id.length > 0 && id.length <= 128))];
  if (!candidates.length) error(404, 'Το άρθρο δεν βρέθηκε.');
  const articleTask = (async () => {
    let result = await requestArticle(newsScope, candidates[0]);
    if (result.status === 404 && candidates.length > 1) result = await requestArticle(newsScope, candidates[1]);
    return result;
  })();
  const [site, article] = await Promise.all([loadPublicSite(url), articleTask]);
  if (article.status === 404) error(404, 'Το άρθρο δεν βρέθηκε.');
  return { ...site, newsScope, article: article.value, articleAvailable: article.status === 200 && article.value !== null };
}
