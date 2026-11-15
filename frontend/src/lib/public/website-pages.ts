import type { PublicNavigationItem, PublicMenuLink } from './navigation';
import type { PublicOrganization } from './types';

export type WebsiteBlock = { type: 'heading'|'paragraph'|'image'|'button'; text: string; url: string|null };
export type WebsitePageContent = {
  title: string; summary: string; seo_title: string; seo_description: string;
  menu_parent: 'hidden'|'root'|'club'|'team'|'academy'|'matches'|'news'|'events'|'shop'|'contact';
  menu_order: number; blocks: WebsiteBlock[];
};
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
export const isHomeHeroSlug = (slug: string) =>
  slug === HOMEPAGE_HERO_SLUGS.lefteria_fc || slug === HOMEPAGE_HERO_SLUGS.lefteria_fc_academy;

export function websitePagePath(slug: string, org: PublicOrganization): string {
  return '/pages/' + encodeURIComponent(slug) +
    (org === 'lefteria_fc_academy' ? '?organization_id=lefteria_fc_academy' : '');
}
export function isPublishedWebsitePage(value: unknown, owner: PublicOrganization): value is PublishedWebsitePage {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const row = value as Partial<PublishedWebsitePage>;
  if (row.organization_id !== owner || typeof row.slug !== 'string' ||
      !/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(row.slug) ||
      !row.page || typeof row.page !== 'object' ||
      typeof row.page.title !== 'string' || !Array.isArray(row.page.blocks) ||
      typeof row.page.menu_parent !== 'string' ||
      !Number.isSafeInteger(row.page.menu_order)) return false;
  return row.page.blocks.every(block => block &&
    ['heading','paragraph','image','button'].includes(block.type) &&
    typeof block.text === 'string' &&
    (block.url === null || typeof block.url === 'string'));
}

export function withWebsitePages(
  navigation: readonly PublicNavigationItem[],
  pages: readonly PublishedWebsitePage[], organization: PublicOrganization
): PublicNavigationItem[] {
  const result = navigation.map(item => item.items ?
    {...item, items: [...item.items]} as PublicNavigationItem : {...item} as PublicNavigationItem);
  for (const row of [...pages].filter(p => p.organization_id === organization &&
      isPublishedWebsitePage(p, organization) && !isHomeHeroSlug(p.slug) && p.page.menu_parent !== 'hidden')
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
