import {CANONICAL_CONTACT_LEAD_SLUG, isPublishedWebsitePage,
  type PublishedWebsitePage} from './website-pages.ts';
import type {PublicOrganization} from './types';

/** Exactly one published, owner-scoped paragraph. No HTML or navigation. */
export function canonicalContactLead(
  pages: readonly PublishedWebsitePage[], owner: PublicOrganization
): string | null {
  const record=pages.find(row=>row.slug===CANONICAL_CONTACT_LEAD_SLUG &&
    isPublishedWebsitePage(row,owner));
  const page=record?.page;
  if(!page || page.menu_parent!=='hidden' || page.menu_order!==0 ||
    page.blocks.length!==1 || page.blocks[0]?.type!=='paragraph' ||
    page.blocks[0].url!==null || !page.blocks[0].text.trim() ||
    page.blocks[0].text.length>500) return null;
  return page.blocks[0].text;
}
