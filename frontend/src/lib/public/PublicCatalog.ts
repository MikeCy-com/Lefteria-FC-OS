export type CatalogKind = 'events' | 'partners' | 'sponsors' | 'seasons';
export type CatalogScope = 'first_team' | 'academy' | null;
export const catalogTitles: Record<CatalogKind, string> = { events: 'ΕΚΔΗΛΩΣΕΙΣ', partners: 'ΟΙ ΣΥΝΕΡΓΑΤΕΣ ΜΑΣ', sponsors: 'ΟΙ ΧΟΡΗΓΟΙ ΜΑΣ', seasons: 'ΠΑΛΑΙΟΤΕΡΕΣ ΣΕΖΟΝ' };
export function catalogScopeLabel(value: string) { return value === 'lefteria_fc_academy' ? 'ΑΚΑΔΗΜΙΑ' : 'ΠΡΩΤΗ ΟΜΑΔΑ'; }
export function catalogHref(kind: CatalogKind, row: { id: string; organization_id: string }, title: string) {
  const slug = title.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLocaleLowerCase('el-GR').replace(/[^a-z0-9α-ω]+/g, '-').slice(0, 70).replace(/^-+|-+$/g, '');
  return `/${kind}/${slug ? slug + '--' : ''}${encodeURIComponent(row.id)}?scope=${row.organization_id === 'lefteria_fc_academy' ? 'academy' : 'first_team'}`;
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
