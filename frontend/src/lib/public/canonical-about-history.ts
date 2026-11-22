import {CANONICAL_ABOUT_HISTORY_SLUG, isPublishedWebsitePage,
  type PublishedWebsitePage} from './website-pages.ts';
import type {PublicOrganization} from './types';

export type CanonicalAboutHistory = {heading:string; paragraphs:[string,string,string]};

/** Exact approved four-block About history. All native non-editorial
 * widgets and the original static fallback remain in place. */
export function canonicalAboutHistory(
  pages: readonly PublishedWebsitePage[], owner: PublicOrganization
): CanonicalAboutHistory|null {
  const record=pages.find(row=>row.slug===CANONICAL_ABOUT_HISTORY_SLUG &&
    isPublishedWebsitePage(row,owner));
  const content=record?.page;
  if(!content || content.menu_parent!=='hidden' || content.menu_order!==0 ||
    content.blocks.length!==4 || content.blocks[0]?.type!=='heading' ||
    content.blocks[0].url!==null || !content.blocks[0].text.trim() ||
    content.blocks[0].text.length>180 || /[\r\n]/.test(content.blocks[0].text) ||
    content.blocks.slice(1).some(block=>block.type!=='paragraph' ||
      block.url!==null || !block.text.trim() || block.text.length>800))return null;
  return {heading:content.blocks[0].text,
    paragraphs:[content.blocks[1].text,content.blocks[2].text,content.blocks[3].text]};
}
