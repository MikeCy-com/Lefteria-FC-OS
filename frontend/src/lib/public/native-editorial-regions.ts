import {
  nativeEditorialOwnerAllowed, isPublishedWebsitePage, type NativeEditorialRegion,
  validPageLayout,validBlockLayout,type WebsitePageLayout,
  type PublishedWebsitePage, type WebsiteBlock
} from './website-pages.ts';
import type {PublicOrganization} from './types';

/** Public-only structural projection for non-destructive additional page regions.
 * Dynamic roster, fixtures, news and sponsors remain owned by their APIs.
 */
export function nativeEditorialBlocks(
  pages: readonly PublishedWebsitePage[], owner:PublicOrganization, region:NativeEditorialRegion
): WebsiteBlock[] {
  if(!nativeEditorialOwnerAllowed(region,owner))return [];
  const row=pages.find(item=>item.slug===region && isPublishedWebsitePage(item,owner));
  const page=row?.page;
  if(!page||!validPageLayout(page.layout)||page.menu_parent!=='hidden'||page.menu_order!==0||
     page.blocks.length<1||page.blocks.length>12)return [];
  if(!page.blocks.every(block=>{
    if(!block||!validBlockLayout(block.layout)||!['heading','paragraph','image','button'].includes(block.type)||
       typeof block.text!=='string'||!block.text.trim()||block.text.length>5000)return false;
    if(block.type==='heading'||block.type==='paragraph')return block.url===null;
    const url=block.url;
    return typeof url==='string'&&url.length>0&&url.length<=2048&&
      !url.includes('\\')&&!url.includes('"')&&
      (url.startsWith('/')&&!url.startsWith('//')||/^https:\/\//i.test(url));
  }))return [];
  return page.blocks;
}

/** Matches the same verified published native record as the blocks; never
 * applies unapproved layout to another tenant's content. */
export function nativeEditorialPageLayout(
  pages:readonly PublishedWebsitePage[],owner:PublicOrganization,region:NativeEditorialRegion
):WebsitePageLayout|null {
  if(nativeEditorialBlocks(pages,owner,region).length===0)return null;
  const row=pages.find(item=>item.slug===region && isPublishedWebsitePage(item,owner));
  return row?.page.layout ?? null;
}
