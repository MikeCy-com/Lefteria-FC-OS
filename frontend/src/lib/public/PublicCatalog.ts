import { publicSlug } from './public-urls.ts';
export type CatalogKind = 'events' | 'partners' | 'sponsors' | 'seasons';
export type CatalogScope = 'first_team' | 'academy' | null;
export const catalogTitles: Record<CatalogKind, string> = { events: 'ΕΚΔΗΛΩΣΕΙΣ', partners: 'ΟΙ ΣΥΝΕΡΓΑΤΕΣ ΜΑΣ', sponsors: 'ΟΙ ΧΟΡΗΓΟΙ ΜΑΣ', seasons: 'ΠΑΛΑΙΟΤΕΡΕΣ ΣΕΖΟΝ' };
export function catalogScopeLabel(value: string) { return value === 'lefteria_fc_academy' ? 'ΑΚΑΔΗΜΙΑ' : 'ΠΡΩΤΗ ΟΜΑΔΑ'; }
export function catalogHref(kind: CatalogKind, row: { id: string; organization_id: string }, title: string) {
  const slug = publicSlug(title).slice(0,70).replace(/-+$/g, '');
  return `/${kind}/${slug ? slug + '--' : ''}${encodeURIComponent(row.id)}?scope=${row.organization_id === 'lefteria_fc_academy' ? 'academy' : 'first_team'}`;
}


/** The canonical detail identity always comes from a published API DTO. */
export function catalogCanonicalDetailLink(kind: CatalogKind, row: {id:string; organization_id:string}, title: string, partnerSlug?: string|null): string {
  if(kind === 'partners' && partnerSlug && /^[a-z0-9][a-z0-9-]{0,95}$/.test(partnerSlug))
    return '/partners/' + encodeURIComponent(partnerSlug)
      + '?scope=' + (row.organization_id === 'lefteria_fc_academy' ? 'academy' : 'first_team');
  return catalogHref(kind,row,title);
}

/** Only forward validated list-navigation context, never secrets or arbitrary queries. */
export function catalogCanonicalRedirect(source: URL, canonical: string): string | null {
  const target = new URL(canonical,source.origin);
  if(source.pathname === target.pathname) return null;
  for(const key of ['list_scope','when','type'] as const) {
    const value = source.searchParams.get(key);
    if(value === null) continue;
    if(key === 'list_scope' && !['academy','first_team','all'].includes(value)) continue;
    if(key === 'when' && !['upcoming','past','all'].includes(value)) continue;
    if(key === 'type' && (value.length > 64 || [...value].some(char => char.charCodeAt(0) < 32 || char.charCodeAt(0) === 127))) continue;
    target.searchParams.set(key,value);
  }
  return target.pathname + target.search;
}

export function catalogListHref(kind: CatalogKind, scope: CatalogScope, when: 'upcoming' | 'past' | 'all' = 'all', eventType = 'all') {
  const query = new URLSearchParams();
  if (scope) query.set('scope', scope);
  if (kind === 'events') {
    query.set('when', when);
    if (eventType !== 'all') query.set('type', eventType);
  }
  return '/' + kind + (query.size ? '?' + query.toString() : '');
}
export function eventTypeLabel(value: string): string {
  const labels: Record<string, string> = { open_day: 'Open Day', camp: 'Camp', tournament: 'Τουρνουά', webinar: 'Webinar', social: 'Κοινωνική', trial: 'Δοκιμαστικά', other: 'Εκδήλωση' };
  return Object.hasOwn(labels, value) ? labels[value] : value;
}
export function eventDateTime(value: string): string {
  const date = new Date(value);
  return Number.isFinite(date.getTime()) ? new Intl.DateTimeFormat('el-GR', { timeZone: 'Asia/Nicosia', day: 'numeric', month: 'long', year: 'numeric', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).format(date) : '—';
}
export function eventPrice(value: number | string): string {
  return Number(value) === 0 ? 'Δωρεάν' : new Intl.NumberFormat('el-GR', { style: 'currency', currency: 'EUR' }).format(Number(value));
}
