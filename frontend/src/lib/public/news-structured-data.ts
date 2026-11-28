import type { PublicArticle, PublicNewsScope } from './types';
import { articleLink } from './content.ts';
import { publicCanonicalHref, publicSocialImage } from './public-urls.ts';

// Explicit public DTO allowlist. Do not copy article bodies, unknown fields,
// private account data or invented author/publisher/publication information.
export function newsStructuredData(article: PublicArticle | null, scope: PublicNewsScope, indexable: boolean): string | null {
  if (!indexable || !article ||
      (article.organization_id !== 'lefteria_fc' && article.organization_id !== 'lefteria_fc_academy') ||
      (scope !== null && scope !== article.organization_id) ||
      typeof article.id !== 'string' || !article.id || article.id.length > 128 ||
      article.id === '.' || article.id === '..' || /[\u0000-\u001f\u007f\\/?#]/.test(article.id) ||
      typeof article.title !== 'string' || !article.title.trim() || article.title.length > 500) return null;
  const url = publicCanonicalHref(articleLink(article, scope));
  const result: Record<string, unknown> = {
    '@context': 'https://schema.org',
    '@type': 'NewsArticle',
    headline: article.title,
    url,
    mainEntityOfPage: { '@type': 'WebPage', '@id': url }
  };
  if (typeof article.excerpt === 'string' && article.excerpt.trim() && article.excerpt.length <= 2000)
    result.description = article.excerpt;
  const image = publicSocialImage(article.image_url);
  if (image) result.image = [image];
  // The public DTO exposes creation, not verified publication/modification time.
  if (typeof article.created_at === 'string' &&
      /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/i.test(article.created_at)) {
    const created = new Date(article.created_at);
    if (Number.isFinite(created.getTime())) result.dateCreated = created.toISOString();
  }
  // JSON-LD script elements use HTML raw-text parsing. Escape every angle
  // bracket, ampersand and JS line separator, preserving the JSON values.
  const json = JSON.stringify(result).replace(/[<>&\u2028\u2029]/g, char =>
    '\\u' + char.charCodeAt(0).toString(16).padStart(4, '0'));
  return '<script type="application/ld+json">' + json + '</script>';
}
