import {CANONICAL_ABOUT_INTRO_SLUG, isPublishedWebsitePage,
  type PublishedWebsitePage} from './website-pages.ts';
import type {PublicOrganization} from './types';

/** Approved canonical introduction only; never executes CMS markup or touches
 * the crest, history, values, stadium or other existing About sections. */
export function canonicalAboutIntro(
  pages: readonly PublishedWebsitePage[], owner: PublicOrganization
): {heading: string; description: string} | null {
  const record=pages.find(row=>row.slug===CANONICAL_ABOUT_INTRO_SLUG &&
    isPublishedWebsitePage(row,owner));
  const page=record?.page;
  if(!page || page.menu_parent!=='hidden' || page.menu_order!==0 ||
    page.blocks.length!==2 || page.blocks[0]?.type!=='heading' ||
    page.blocks[1]?.type!=='paragraph') return null;
  const heading=page.blocks[0].text;
  const description=page.blocks[1].text;
  if(!heading.trim() || !description.trim() || heading.length>180 ||
    description.length>500 || /[\\r\\n]/.test(heading) ||
    page.blocks[0].url!==null || page.blocks[1].url!==null) return null;
  return {heading,description};
}
