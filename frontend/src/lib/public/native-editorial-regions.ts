import {
  nativeEditorialOwnerAllowed, isPublishedWebsitePage, type NativeEditorialRegion,
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
  if(!page||page.menu_parent!=='hidden'||page.menu_order!==0||
     page.blocks.length<1||page.blocks.length>12)return [];
  if(!page.blocks.every(block=>{
    if(!block||!['heading','paragraph','image','button'].includes(block.type)||
       typeof block.text!=='string'||!block.text.trim()||block.text.length>5000)return false;
    if(block.type==='heading'||block.type==='paragraph')return block.url===null;
    const url=block.url;
    return typeof url==='string'&&url.length>0&&url.length<=2048&&
      !url.includes('\\')&&!url.includes('"')&&
      (url.startsWith('/')&&!url.startsWith('//')||/^https:\/\//i.test(url));
  }))return [];
  return page.blocks;
}
