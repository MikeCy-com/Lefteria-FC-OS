export type CatalogKind = 'events' | 'partners' | 'sponsors' | 'seasons';
export type CatalogScope = 'first_team' | 'academy' | null;
export const catalogTitles: Record<CatalogKind, string> = { events: 'ΕΚΔΗΛΩΣΕΙΣ', partners: 'ΟΙ ΣΥΝΕΡΓΑΤΕΣ ΜΑΣ', sponsors: 'ΟΙ ΧΟΡΗΓΟΙ ΜΑΣ', seasons: 'ΠΑΛΑΙΟΤΕΡΕΣ ΣΕΖΟΝ' };
export function catalogScopeLabel(value: string) { return value === 'lefteria_fc_academy' ? 'ΑΚΑΔΗΜΙΑ' : 'ΠΡΩΤΗ ΟΜΑΔΑ'; }
export function catalogHref(kind: CatalogKind, row: { id: string; organization_id: string }, title: string) {
  const slug = title.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLocaleLowerCase('el-GR').replace(/[^a-z0-9α-ω]+/g, '-').slice(0, 70).replace(/^-+|-+$/g, '');
  return `/${kind}/${slug ? slug + '--' : ''}${encodeURIComponent(row.id)}?scope=${row.organization_id === 'lefteria_fc_academy' ? 'academy' : 'first_team'}`;
}
