import { error, redirect } from '@sveltejs/kit';
import { articleLink } from '../../../lib/public/content';
import { loadPublicArticle } from '../../../lib/server/public-content';

export const load = async ({ url, params }: { url: URL; params: { slug: string } }) => {
  // Obtain the published article from the bounded public endpoint first.
  // A 404, unavailable API or wrong organization never creates a redirect.
  const data = await loadPublicArticle(url, params.slug);
  if (data.articleAvailable && data.article && data.newsScope &&
      data.article.organization_id !== data.newsScope) {
    error(404, 'Το άρθρο δεν βρέθηκε.');
  }
  if (data.articleAvailable && data.article &&
      (!data.newsScope || data.article.organization_id === data.newsScope)) {
    const canonical = articleLink(data.article, data.newsScope);
    const canonicalUrl = new URL(canonical, url.origin);
    if (url.pathname !== canonicalUrl.pathname) {
      redirect(301, canonicalUrl.pathname + canonicalUrl.search);
    }
  }
  return data;
};
