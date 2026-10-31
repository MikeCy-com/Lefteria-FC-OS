import type { PublicArticle, PublicFixture, PublicNewsScope, PublicOrganization } from './types';

export function articleLink(article: PublicArticle, newsScope: PublicNewsScope = null): string {
  const slug = article.title.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLocaleLowerCase('el-GR')
    .replace(/[^a-z0-9α-ω]+/g, '-').slice(0, 80).replace(/^-+|-+$/g, '');
  return `/news/${slug ? slug + '--' : ''}${encodeURIComponent(article.id)}${newsScope ? '?organization_id=' + newsScope : ''}`;
}
export function newsLink(newsScope: PublicNewsScope, category = '', page = 1): string {
  const query = new URLSearchParams();
  if (newsScope) query.set('organization_id', newsScope);
  if (category) query.set('category', category);
  if (page > 1) query.set('page', String(page));
  return '/news' + (query.size ? '?' + query.toString() : '');
}
export function organizationHref(path: string, organization: PublicOrganization): string {
  return `${path}${path.includes('?') ? '&' : '?'}organization_id=${organization}`;
}
export function greekDate(value: string | null): string {
  if (!value) return '';
  const date = new Date(value.length === 10 ? value + 'T12:00:00Z' : value);
  return Number.isFinite(date.getTime()) ? new Intl.DateTimeFormat('el-GR', { day: 'numeric', month: 'long', year: 'numeric', timeZone: 'Asia/Nicosia' }).format(date) : '';
}
export function fixtureStatus(fixture: PublicFixture): string {
  return ({ Scheduled: 'Προσεχής αγώνας', Live: 'Ζωντανά', 'Half Time': 'Ημίχρονο', Completed: 'Τελικό', Postponed: 'Αναβλήθηκε', Cancelled: 'Ακυρώθηκε' })[fixture.status] ?? fixture.status;
}
export function safePublicHref(value: string): string | null {
  if (/[\u0000-\u001f\u007f\\]/.test(value) || value.startsWith('//')) return null;
  if (value.startsWith('/')) return /^\/(?:news|team|academy|about|contact|app)(?:[/?#]|$)/.test(value) ? value : null;
  try { const url = new URL(value); return url.protocol === 'https:' && !url.username && !url.password ? value : null; } catch { return null; }
}

export type MarkdownBlock = { kind: 'heading' | 'paragraph' | 'quote' | 'list'; text: string; level?: number; items?: string[] };
export function markdownBlocks(value: string): MarkdownBlock[] {
  return value.replace(/\r\n/g, '\n').split(/\n\s*\n/).filter(block => block.trim()).map(block => {
    const heading = /^(#{1,3})\s+(.+)$/.exec(block);
    if (heading) return { kind: 'heading', text: heading[2], level: heading[1].length };
    if (block.split('\n').every(line => /^[-*]\s+/.test(line))) return { kind: 'list', text: '', items: block.split('\n').map(line => line.replace(/^[-*]\s+/, '')) };
    if (block.startsWith('> ')) return { kind: 'quote', text: block.split('\n').map(line => line.replace(/^>\s?/, '')).join('\n') };
    return { kind: 'paragraph', text: block };
  });
}
export type InlineToken = { kind: 'text' | 'strong' | 'link'; text: string; href?: string };
export function inlineTokens(value: string): InlineToken[] {
  const tokens: InlineToken[] = []; let cursor = 0; let index = 0; let links = true; let strong = true;
  // Scan forward once. A regex that retries an unclosed '[' at every character
  // becomes quadratic for a large malformed article.
  while (index < value.length) {
    if (strong && value.startsWith('**', index)) {
      const end = value.indexOf('**', index + 2);
      if (end < 0) strong = false;
      else if (end > index + 2) {
        if (index > cursor) tokens.push({ kind: 'text', text: value.slice(cursor, index) });
        tokens.push({ kind: 'strong', text: value.slice(index + 2, end) });
        index = end + 2; cursor = index; continue;
      }
    }
    if (links && value[index] === '[') {
      const labelEnd = value.indexOf('](', index + 1);
      const end = labelEnd < 0 ? -1 : value.indexOf(')', labelEnd + 2);
      if (end < 0) links = false;
      else {
        if (index > cursor) tokens.push({ kind: 'text', text: value.slice(cursor, index) });
        const text = value.slice(index + 1, labelEnd); const href = safePublicHref(value.slice(labelEnd + 2, end));
        tokens.push(href ? { kind: 'link', text, href } : { kind: 'text', text });
        index = end + 1; cursor = index; continue;
      }
    }
    index++;
  }
  if (cursor < value.length) tokens.push({ kind: 'text', text: value.slice(cursor) });
  return tokens;
}
