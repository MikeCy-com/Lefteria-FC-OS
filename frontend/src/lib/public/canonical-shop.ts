import {CANONICAL_SHOP_LEAD_SLUG, isPublishedWebsitePage,
  type PublishedWebsitePage} from './website-pages.ts';

/** First Team shop copy only; never trusts draft records or catalog state. */
export function canonicalShopLead(pages:readonly PublishedWebsitePage[]):string|null {
  const record=pages.find(row=>row.slug===CANONICAL_SHOP_LEAD_SLUG &&
    isPublishedWebsitePage(row,'lefteria_fc'));
  const page=record?.page;
  if(!page||page.menu_parent!=='hidden'||page.menu_order!==0||
    page.blocks.length!==1||page.blocks[0]?.type!=='paragraph'||
    page.blocks[0].url!==null||!page.blocks[0].text.trim()||
    page.blocks[0].text.length>500)return null;
  return page.blocks[0].text;
}
