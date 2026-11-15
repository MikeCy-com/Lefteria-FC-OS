import type {PublicOrganization} from './types';
import {HOMEPAGE_HERO_SLUGS, isPublishedWebsitePage,
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
