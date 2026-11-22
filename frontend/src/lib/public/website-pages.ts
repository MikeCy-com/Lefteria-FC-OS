import type { PublicNavigationItem, PublicMenuLink } from './navigation';
import type { PublicOrganization } from './types';

// Only curated responsive layout tokens are persisted; arbitrary CSS is forbidden.
export type WebsiteBlockLayout = {
  width: 'full'|'half'|'third';
  min_height: 0|120|240|360|480;
  margin_top: 0|8|16|24|32|48|64;
  margin_bottom: 0|8|16|24|32|48|64;
};
export type WebsitePageLayout = {
  max_width: 960|1120|1280|1440;
  gap: 8|16|24|32|48;
  side_padding: 12|16|24|32|48;
};
export type WebsiteBlock = { type: 'heading'|'paragraph'|'image'|'button'; text: string; url: string|null; level?: 2|3|4|5|6|null; layout?: WebsiteBlockLayout|null };
export function validHeadingLevel(block:WebsiteBlock):boolean {
  return block.type==='heading' ? (block.level==null || [2,3,4,5,6].includes(block.level)) : block.level==null;
}
export type WebsitePageContent = {
  title: string; summary: string; seo_title: string; seo_description: string;
  menu_parent: 'hidden'|'root'|'club'|'team'|'academy'|'matches'|'news'|'events'|'shop'|'contact';
  menu_order: number; blocks: WebsiteBlock[]; layout?: WebsitePageLayout|null;
};
export const DEFAULT_BLOCK_LAYOUT:WebsiteBlockLayout={width:'full',min_height:0,margin_top:0,margin_bottom:0};
export const DEFAULT_PAGE_LAYOUT:WebsitePageLayout={max_width:1280,gap:24,side_padding:16};
const VALID_WIDTHS=['full','half','third'];
const VALID_HEIGHTS=[0,120,240,360,480];
const VALID_MARGINS=[0,8,16,24,32,48,64];
export function validBlockLayout(value:WebsiteBlockLayout|null|undefined):boolean {
  return value == null || (VALID_WIDTHS.includes(value.width) &&
    VALID_HEIGHTS.includes(value.min_height) && VALID_MARGINS.includes(value.margin_top) &&
    VALID_MARGINS.includes(value.margin_bottom));
}
export function validPageLayout(value:WebsitePageLayout|null|undefined):boolean {
  return value == null || ([960,1120,1280,1440].includes(value.max_width) &&
    [8,16,24,32,48].includes(value.gap) && [12,16,24,32,48].includes(value.side_padding));
}
export type WebsitePageRecord = {
  organization_id: PublicOrganization; slug: string; version: number;
  draft: WebsitePageContent; published: WebsitePageContent|null;
  published_at: string|null; updated_at: string;
};
export type PublishedWebsitePage = {
  organization_id: PublicOrganization; slug: string;
  page: WebsitePageContent; published_at: string;
};
export type WebsiteNavigationData = Record<PublicOrganization, PublishedWebsitePage[]>;

// Native homepage regions are published snapshots, not standalone public pages.
export const HOMEPAGE_HERO_SLUGS = {
  lefteria_fc: 'homepage-hero-first-team',
  lefteria_fc_academy: 'homepage-hero-academy'
} as const satisfies Record<PublicOrganization, string>;
// Shared CMS menu policy. Used by the admin selector and by the public
// menu projection, which must reject legacy or malformed owner placements.
export function websiteMenuParentAllowed(organization: PublicOrganization, parent: WebsitePageContent['menu_parent']): boolean {
  return organization === 'lefteria_fc_academy' ? parent !== 'team' && parent !== 'matches' : parent !== 'academy';
}
export const isHomeHeroSlug = (slug: string) =>
  slug === HOMEPAGE_HERO_SLUGS.lefteria_fc || slug === HOMEPAGE_HERO_SLUGS.lefteria_fc_academy;
export const HOMEPAGE_ACADEMY_SPOTLIGHT_SLUG = 'homepage-academy-spotlight' as const;
export const CANONICAL_ABOUT_INTRO_SLUG = 'canonical-about-intro' as const;
export const CANONICAL_CONTACT_LEAD_SLUG = 'canonical-contact-lead' as const;
export const CANONICAL_ABOUT_HISTORY_SLUG = 'canonical-about-history' as const;
export const CANONICAL_ACADEMY_INTRO_SLUG = 'canonical-academy-intro' as const;
export const CANONICAL_ACADEMY_PHILOSOPHY_LEAD_SLUG = 'canonical-academy-philosophy-lead' as const;
export const CANONICAL_ACADEMY_GROUPS_LEAD_SLUG = 'canonical-academy-groups-lead' as const;
export const CANONICAL_ACADEMY_REGISTRATION_LEAD_SLUG = 'canonical-academy-registration-lead' as const;
export const CANONICAL_SHOP_LEAD_SLUG = 'canonical-shop-lead' as const;
export const CANONICAL_GLOBAL_DESIGN_SLUG = 'canonical-global-design' as const;
export const CANONICAL_NAVIGATION_DESIGN_SLUG = 'canonical-navigation-design' as const;
export const NATIVE_EDITORIAL_REGIONS = {
  'canonical-team-roster-sections':'lefteria_fc',
  'canonical-team-schedule-sections':'lefteria_fc',
  'canonical-team-results-sections':'lefteria_fc',
  'canonical-team-statistics-sections':'lefteria_fc',
  'canonical-news-sections':'both',
  'canonical-sponsors-sections':'both'
} as const;
export type NativeEditorialRegion = keyof typeof NATIVE_EDITORIAL_REGIONS;
export function nativeEditorialOwnerAllowed(slug:string, org:PublicOrganization):slug is NativeEditorialRegion {
  return Object.prototype.hasOwnProperty.call(NATIVE_EDITORIAL_REGIONS,slug) &&
    (NATIVE_EDITORIAL_REGIONS[slug as NativeEditorialRegion]==='both' ||
     NATIVE_EDITORIAL_REGIONS[slug as NativeEditorialRegion]===org);
}

export const isAcademySpotlightSlug = (slug: string) => slug === HOMEPAGE_ACADEMY_SPOTLIGHT_SLUG;
// Native slots are not routable standalone CMS pages, even if a legacy
// record has an incompatible menu assignment.
export const isHomepageEditorialSlug = (slug: string) =>
  isHomeHeroSlug(slug) || isAcademySpotlightSlug(slug) || slug === CANONICAL_ABOUT_INTRO_SLUG || slug === CANONICAL_CONTACT_LEAD_SLUG ||
  slug === CANONICAL_ABOUT_HISTORY_SLUG ||
  slug === CANONICAL_ACADEMY_INTRO_SLUG || slug === CANONICAL_ACADEMY_PHILOSOPHY_LEAD_SLUG ||
  slug === CANONICAL_ACADEMY_GROUPS_LEAD_SLUG ||
  slug === CANONICAL_ACADEMY_REGISTRATION_LEAD_SLUG || slug === CANONICAL_SHOP_LEAD_SLUG || slug === CANONICAL_GLOBAL_DESIGN_SLUG || slug === CANONICAL_NAVIGATION_DESIGN_SLUG ||
  Object.prototype.hasOwnProperty.call(NATIVE_EDITORIAL_REGIONS,slug);

export function websitePageParentLink(
  organization: PublicOrganization, parent: WebsitePageContent['menu_parent']
): {href: string; label: string} {
  const isAcademy = organization === 'lefteria_fc_academy';
  // About and Contact accept organization_id; Events accepts scope instead.
  // The current Shop is a shared first-team surface and must not pretend
  // that an ignored organization_id parameter filters its catalog.
  // Shared public routes support an explicit organization_id. Preserve the
  // Academy context rather than silently falling back to the First Team.
  const shared = (path: string) => isAcademy ? path + '?organization_id=lefteria_fc_academy' : path;
  // Only a published page may reach this component. Return to the native,
  // contextually correct public hub—not to an admin editor or a sibling org.
  switch (parent) {
    case 'club': return {href:shared('/about'),label:'Ο ΣΥΛΛΟΓΟΣ'};
    case 'team': return {href:'/team',label:'ΠΡΩΤΗ ΟΜΑΔΑ'};
    case 'academy': return {href:'/academy',label:'ΑΚΑΔΗΜΙΑ'};
    case 'matches': return {href:'/team?tab=results',label:'ΑΓΩΝΕΣ'};
    case 'news': return {href:isAcademy ? '/news?organization_id=lefteria_fc_academy' : '/news?organization_id=lefteria_fc',label:isAcademy ? 'ΝΕΑ ΑΚΑΔΗΜΙΑΣ' : 'ΝΕΑ ΠΡΩΤΗΣ ΟΜΑΔΑΣ'};
    case 'events': return {href:isAcademy ? '/events?scope=academy' : '/events?scope=first_team',label:'ΕΚΔΗΛΩΣΕΙΣ'};
    case 'shop': return {href:'/shop',label:'ΚΑΤΑΣΤΗΜΑ'};
    case 'contact': return {href:shared('/contact'),label:'ΕΠΙΚΟΙΝΩΝΙΑ'};
    case 'root':
    case 'hidden':
    default: return isAcademy
      ? {href:'/academy',label:'ΑΚΑΔΗΜΙΑ'}
      : {href:'/team',label:'ΠΡΩΤΗ ΟΜΑΔΑ'};
  }
}

export function websitePagePath(slug: string, org: PublicOrganization): string {
  return '/pages/' + encodeURIComponent(slug) +
    (org === 'lefteria_fc_academy' ? '?organization_id=lefteria_fc_academy' : '');
}
export function isPublishedWebsitePage(value: unknown, owner: PublicOrganization): value is PublishedWebsitePage {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const row = value as Partial<PublishedWebsitePage>;
  if (row.organization_id !== owner || typeof row.published_at !== 'string' ||
      !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/.test(row.published_at) ||
      !Number.isFinite(Date.parse(row.published_at)) || typeof row.slug !== 'string' ||
      !/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(row.slug) ||
      !row.page || typeof row.page !== 'object' ||
      typeof row.page.title !== 'string' || !Array.isArray(row.page.blocks) ||
      !['hidden','root','club','team','academy','matches','news','events','shop','contact'].includes(row.page.menu_parent) ||
      !Number.isSafeInteger(row.page.menu_order) || row.page.menu_order < 0 || row.page.menu_order > 1000 ||
      row.page.title.length === 0 || row.page.title.length > 180 ||
      typeof row.page.summary !== 'string' || row.page.summary.length > 500 ||
      typeof row.page.seo_title !== 'string' || row.page.seo_title.length > 180 ||
      typeof row.page.seo_description !== 'string' || row.page.seo_description.length > 300 ||
      row.page.blocks.length < 1 || row.page.blocks.length > 40) return false;
  return row.page.blocks.every(block => block &&
    ['heading','paragraph','image','button'].includes(block.type) &&
    typeof block.text === 'string' &&
    (block.url === null || typeof block.url === 'string') && validHeadingLevel(block));
}

export function withWebsitePages(
  navigation: readonly PublicNavigationItem[],
  pages: readonly PublishedWebsitePage[], organization: PublicOrganization
): PublicNavigationItem[] {
  const result = navigation.map(item => item.items ?
    {...item, items: [...item.items]} as PublicNavigationItem : {...item} as PublicNavigationItem);
  for (const row of [...pages].filter(p => p.organization_id === organization &&
      isPublishedWebsitePage(p, organization) && !isHomepageEditorialSlug(p.slug) &&
      p.page.menu_parent !== 'hidden' && websiteMenuParentAllowed(organization, p.page.menu_parent))
      .sort((a,b) => a.page.menu_order-b.page.menu_order || a.slug.localeCompare(b.slug))) {
    const path = websitePagePath(row.slug, organization);
    const label = row.page.title.slice(0, 80);
    const parent = row.page.menu_parent;
    if (parent === 'root') {
      result.splice(Math.min(row.page.menu_order, result.length), 0,
        {id: 'page-' + organization + '-' + row.slug, label, path, icon: 'info'});
      continue;
    }
    const index = result.findIndex(item => item.id === parent);
    if (index < 0) continue;
    const current = result[index];
    const existing: PublicMenuLink[] = current.items ? [...current.items] :
      current.path ? [[current.id + '-home', current.label, current.path]] : [];
    existing.splice(Math.min(row.page.menu_order, existing.length), 0,
      ['page-' + organization + '-' + row.slug, label, path]);
    result[index] = {id: current.id, label: current.label, icon: current.icon, items: existing};
  }
  return result;
}
