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

export function publicApi() {
  const origin = new URL(INTERNAL_API_ORIGIN);
  if (!['http:', 'https:'].includes(origin.protocol) || origin.username || origin.password || origin.pathname !== '/' || origin.search || origin.hash) throw new Error('Invalid internal API origin.');
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

// Bare Node preview has no edge proxy. This single public GET uses the same
// fixed, credential-free server client as SSR; no caller chooses its upstream.
export async function requestPublicSearchNews(url: URL) {
  const allowed = new Set(['organization_id', 'offset', 'limit', 'category']);
  const seen = new Set<string>();
  for (const key of url.searchParams.keys()) {
    if (!allowed.has(key) || seen.has(key)) error(400, 'Μη έγκυρα φίλτρα.');
    seen.add(key);
  }
  const organization = publicNewsScope(url);
  const offsetText = url.searchParams.get('offset') ?? '0';
  const limitText = url.searchParams.get('limit') ?? '100';
  if (!/^(0|[1-9][0-9]{0,4})$/.test(offsetText) || !/^[1-9][0-9]{0,2}$/.test(limitText)) error(400, 'Μη έγκυρα φίλτρα.');
  const offset = Number(offsetText); const limit = Number(limitText); const category = url.searchParams.get('category') ?? '';
  if (offset > 20000 || limit > 100 || category.length > 200) error(400, 'Μη έγκυρα φίλτρα.');
  return requestNews(organization, offset, category, limit);
}

export async function loadPublicSite(url: URL, fixed?: PublicOrganization): Promise<PublicPageData> {
  const organization = publicOrganization(url, fixed);
  const resultTask = requestSite(organization);
  // Shared public navigation needs Academy groups even on an FC page. They
  // remain a separate explicit public request, never injected into FC site data.
  const navigationTask = organization === 'lefteria_fc_academy' ? resultTask : requestSite('lefteria_fc_academy');
  // The source public shell always uses the FC /club branding. Page content
  // retains its explicit organization; this never widens a private grant.
  const brandingTask = organization === 'lefteria_fc' ? resultTask : requestSite('lefteria_fc');
  const [result, navigation, branding] = await Promise.all([resultTask, navigationTask, brandingTask]);
  return { site: result.value, available: result.status === 200 && result.value !== null, organization,
    branding: branding.value?.club ?? null, navigationGroups: navigation.value?.academy_groups ?? [] };
}

export async function loadPublicHome(url: URL): Promise<PublicHomeData> {
  // Filter the actual published feed before selecting the three homepage cards.
  const [site, news] = await Promise.all([loadPublicSite(url), requestNews(null, 0, '', 100)]);
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
