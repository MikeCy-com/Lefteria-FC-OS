import { CANONICAL_ACADEMY_INTRO_SLUG, CANONICAL_ACADEMY_PHILOSOPHY_LEAD_SLUG,
  isPublishedWebsitePage, type PublishedWebsitePage } from './website-pages.ts';

type AcademyEditorialSlot =
  typeof CANONICAL_ACADEMY_INTRO_SLUG | typeof CANONICAL_ACADEMY_PHILOSOPHY_LEAD_SLUG;

/** Academy-only approved introductory copy, with no untrusted HTML, links,
 * native data replacement or cross-organization fallback. */
export function canonicalAcademyLead(
  pages: readonly PublishedWebsitePage[], slot: AcademyEditorialSlot
): string | null {
  const row = pages.find(item => item.slug === slot &&
    isPublishedWebsitePage(item, 'lefteria_fc_academy'));
  const page = row?.page;
  if (!page || page.menu_parent !== 'hidden' || page.menu_order !== 0 ||
      page.blocks.length !== 1 || page.blocks[0]?.type !== 'paragraph' ||
      page.blocks[0].url !== null || !page.blocks[0].text.trim() ||
      page.blocks[0].text.length > 500) return null;
  return page.blocks[0].text;
}
