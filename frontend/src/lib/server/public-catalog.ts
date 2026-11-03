import { error } from '@sveltejs/kit';
import { loadPublicSite, publicApi } from './public-content';
import type { components } from '../api/schema';
import type { PublicOrganization, PublicPageData } from '../public/types';
import { catalogHref, type CatalogKind, type CatalogScope } from '../public/PublicCatalog';

type Sections = components['schemas']['PublicSectionsResult'];
type Event = components['schemas']['PublicSectionEvent'];
type Partner = components['schemas']['PublicSectionPartner'];
type Sponsor = components['schemas']['PublicSectionSponsor'];
type Archive = components['schemas']['PublicSectionArchive'];
export type CatalogCard = { id: string; organization: PublicOrganization; title: string; summary: string | null; image: string | null; label: string; meta: string | null; href: string; scopeLabel?: string };
export type CatalogData = PublicPageData & {
  kind: CatalogKind; scope: CatalogScope; cards: CatalogCard[]; when: 'upcoming' | 'past' | 'all'; eventType: string; eventTypes: string[];
};
export type CatalogDetailData = PublicPageData & {
  kind: CatalogKind; scope: CatalogScope; event: Event | null; partner: Partner | null; sponsor: Sponsor | null; archives: Archive[]; title: string;
};

const club: PublicOrganization = 'lefteria_fc';
const academy: PublicOrganization = 'lefteria_fc_academy';
function scopeFrom(url: URL, fixed?: CatalogScope): CatalogScope {
  if (fixed !== undefined) return fixed;
  const value = url.searchParams.get('scope');
  if (value === null || value === 'all') return null;
  if (value !== 'academy' && value !== 'first_team') error(400, 'Μη έγκυρη ενότητα.');
  return value;
}
async function context(url: URL, fixed?: CatalogScope) {
  const scope = scopeFrom(url, fixed);
  const organizations = scope === 'academy' ? [academy] : scope === 'first_team' ? [club] : [club, academy];
  const [site, sections] = await Promise.all([
    loadPublicSite(url, scope === 'academy' ? academy : club),
    Promise.all(organizations.map(async organization => {
      try {
        const result = await publicApi().GET('/api/public/sections', { params: { query: { organization_id: organization } } });
        return result.response.status === 200 && result.data?.organization_id === organization ? result.data : null;
      } catch { return null; }
    }))
  ]);
  if (!site.available || sections.some(section => !section)) error(503, 'Το περιεχόμενο δεν είναι προσωρινά διαθέσιμο.');
  return { site, scope, sections: sections as Sections[] };
}
function rows(context: Sections[], kind: CatalogKind) {
  if (kind === 'seasons') return context.flatMap(section => section.archived_seasons);
  // A shared event/partner is present in both public organizations. Its stable
  // identity appears once in a global list. Archives remain separate scoped views.
  const source = context.flatMap<Event | Partner | Sponsor>(section => kind === 'events' ? section.events : kind === 'partners' ? section.partners : section.sponsors);
  return [...new Map(source.map(row => [row.id, row])).values()];
}
function card(kind: CatalogKind, row: Event | Partner | Sponsor | Archive): CatalogCard {
  if (kind === 'events') {
    const value = row as Event;
    return { id: value.id, organization: value.organization_id as PublicOrganization, title: value.title, summary: value.description, image: value.cover_image_url, label: value.event_type, meta: value.start_at, href: catalogHref(kind, value, value.title) };
  }
  if (kind === 'partners') {
    const value = row as Partner;
    return { id: value.id, organization: value.organization_id as PublicOrganization, title: value.name, summary: value.tagline, image: value.hero_image_url ?? value.headshot_url, label: value.partner_type, meta: null, href: `/partners/${encodeURIComponent(value.slug)}?scope=${value.organization_id === academy ? 'academy' : 'first_team'}` };
  }
  if (kind === 'sponsors') {
    const value = row as Sponsor;
    return { id: value.id, organization: value.organization_id as PublicOrganization, title: value.name, summary: value.description, image: value.logo_url, label: value.level, meta: null, href: catalogHref(kind, value, value.name) };
  }
  const value = row as Archive;
  return { id: value.id, organization: value.organization_id as PublicOrganization, title: value.season_name, summary: null, image: null, label: value.team_type, meta: value.archived_at, href: catalogHref(kind, value, value.season_name) };
}
export async function loadPublicCatalog(url: URL, kind: CatalogKind, fixedScope?: CatalogScope): Promise<CatalogData> {
  const loaded = await context(url, fixedScope);
  const requestedWhen = url.searchParams.get('when') ?? 'upcoming';
  if (kind === 'events' && !['upcoming', 'past', 'all'].includes(requestedWhen)) error(400, 'Μη έγκυρο χρονικό φίλτρο.');
  const when = ['past', 'all'].includes(requestedWhen) ? requestedWhen as 'past' | 'all' : 'upcoming';
  const eventType = url.searchParams.get('type') ?? 'all';
  if (eventType.length > 64 || /[\u0000-\u001f]/.test(eventType)) error(400, 'Μη έγκυρο φίλτρο.');
  let records = rows(loaded.sections, kind);
  const eventTypes = kind === 'events' ? [...new Set((records as Event[]).map(row => row.event_type))].sort() : [];
  if (kind === 'events') {
    const now = Date.parse(loaded.sections[0].generated_at);
    if (!Number.isFinite(now)) error(503, 'Το περιεχόμενο δεν είναι προσωρινά διαθέσιμο.');
    records = (records as Event[]).filter(row => (eventType === 'all' || row.event_type === eventType) &&
      (when === 'all' || (when === 'past' ? Date.parse(row.start_at) < now : Date.parse(row.start_at) >= now)))
      .sort((a, b) => (when === 'past' ? -1 : 1) * (Date.parse(a.start_at) - Date.parse(b.start_at)) || a.id.localeCompare(b.id));
  } else if (kind === 'seasons') records = (records as Archive[]).sort((a, b) => (b.archived_at ?? '').localeCompare(a.archived_at ?? '') || a.season_name.localeCompare(b.season_name));
  else records = (records as (Partner | Sponsor)[]).sort((a, b) => Number(a.display_order) - Number(b.display_order) || a.name.localeCompare(b.name));
  const shared = new Set<string>();
  if (kind !== 'seasons' && loaded.sections.length > 1) {
    const first = new Set(rows([loaded.sections[0]], kind).map(row => row.id));
    for (const row of rows([loaded.sections[1]], kind)) if (first.has(row.id)) shared.add(row.id);
  }
  return { ...loaded.site, kind, scope: loaded.scope, cards: records.map(row => ({ ...card(kind, row),
    ...(shared.has(row.id) ? { scopeLabel: 'ΣΥΛΛΟΓΟΣ & ΑΚΑΔΗΜΙΑ' } : {}) })), when, eventType, eventTypes };
}
export async function loadPublicCatalogDetail(url: URL, kind: CatalogKind, id: string): Promise<CatalogDetailData> {
  if (!id || id.length > 400 || /[\u0000-\u001f\u007f]/.test(id)) error(404, 'Η σελίδα δεν βρέθηκε.');
  const loaded = await context(url);
  const source = rows(loaded.sections, kind);
  let matches = source.filter(row => row.id === id);
  if (!matches.length && kind === 'partners') matches = (source as Partner[]).filter(row => row.slug === id);
  if (!matches.length && id.indexOf('--') > 0) matches = source.filter(row => row.id === id.slice(id.indexOf('--') + 2));
  if (!matches.length) error(404, 'Η σελίδα δεν βρέθηκε.');
  if (new Set(matches.map(row => row.id)).size > 1 || (kind !== 'seasons' && matches.length > 1)) error(503, 'Το περιεχόμενο δεν είναι προσωρινά διαθέσιμο.');
  const selected = matches[0];
  return { ...loaded.site, kind, scope: loaded.scope, event: kind === 'events' ? selected as Event : null,
    partner: kind === 'partners' ? selected as Partner : null, sponsor: kind === 'sponsors' ? selected as Sponsor : null,
    archives: kind === 'seasons' ? matches as Archive[] : [],
    title: kind === 'events' ? (selected as Event).title : kind === 'seasons' ? (selected as Archive).season_name : (selected as Partner | Sponsor).name };
}
