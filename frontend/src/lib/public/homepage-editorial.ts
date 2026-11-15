import type {PublicOrganization} from './types';
import {HOMEPAGE_HERO_SLUGS, HOMEPAGE_ACADEMY_SPOTLIGHT_SLUG, isPublishedWebsitePage,
  type PublishedWebsitePage, type WebsitePageContent} from './website-pages.ts';

// A fixed, minimal projection is rendered in existing homepage markup; no HTML
// from CMS is executed and no First Team content is borrowed into Academy.
export function homepageHeroPage(
  pages: readonly PublishedWebsitePage[], owner: PublicOrganization
): WebsitePageContent | null {
  const record=pages.find(item=>item.slug===HOMEPAGE_HERO_SLUGS[owner] &&
    isPublishedWebsitePage(item,owner));
  const content=record?.page;
  if(!content || content.menu_parent!=='hidden' || content.menu_order!==0 ||
      content.blocks.length!==2 ||
      content.blocks[0]?.type!=='heading' || content.blocks[1]?.type!=='paragraph' ||
      content.blocks[0].text.length>180 || content.blocks[1].text.length>500 ||
      content.blocks[0].text.split('\n').length>3) return null;
  return content;
}

/** Only Academy-approved, two-block editorial text can replace the existing
 * spotlight copy. The background, destinations and verified group count remain
 * owned by the original dynamic component.
 */
export function homepageAcademySpotlightPage(
  pages: readonly PublishedWebsitePage[], owner: PublicOrganization
): WebsitePageContent | null {
  if (owner !== 'lefteria_fc_academy') return null;
  const record = pages.find(item => item.slug === HOMEPAGE_ACADEMY_SPOTLIGHT_SLUG &&
    isPublishedWebsitePage(item, 'lefteria_fc_academy'));
  const content = record?.page;
  if (!content || content.menu_parent !== 'hidden' || content.menu_order !== 0 ||
      content.blocks.length !== 2 || content.blocks[0]?.type !== 'heading' ||
      content.blocks[1]?.type !== 'paragraph' ||
      content.blocks[0].text.length > 180 || content.blocks[1].text.length > 500 ||
      /[\r\n]/.test(content.blocks[0].text) ||
      content.blocks[0].text.split('·').length !== 3 ||
      content.blocks[0].text.split('·').some(part => !part.trim() || part.length > 60) ||
      !content.blocks[1].text.trim()) return null;
  return content;
}
