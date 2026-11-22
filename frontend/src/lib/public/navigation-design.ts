import {isPublishedWebsitePage,type PublishedWebsitePage} from './website-pages.ts';
import type {PublicOrganization} from './types.ts';
import type {PublicNavigationItem,PublicMenuLink} from './navigation.ts';

export const NAVIGATION_DESIGN_SLUG='canonical-navigation-design' as const;
export const NAVIGATION_PARENTS=['root','club','team','academy','matches','news','events','shop','contact'] as const;
export type NavigationParent=typeof NAVIGATION_PARENTS[number];
export type NavigationEntry={id:string;label:string;path:string;parent:NavigationParent;order:number};
export type NavigationDesign={links:NavigationEntry[]};

const validLink=(link:NavigationEntry,owner:PublicOrganization):boolean=>{
  if(!link||Object.keys(link).length!==5 ||
     typeof link.id!=='string'||!/^custom-[a-z0-9-]{3,45}$/.test(link.id)||
     typeof link.label!=='string'||!link.label.trim()||link.label.length>80||
     typeof link.path!=='string'||link.path.length>250||
     !/^\/[a-z0-9-]+(?:\/[a-z0-9-]+)*(?:\?[a-z0-9_=&-]+)?$/.test(link.path)||
     /^\/(admin|api|app|login)(?:\/|\?|$)/.test(link.path)||
     !NAVIGATION_PARENTS.includes(link.parent)||!Number.isInteger(link.order)||
     link.order<0||link.order>100)return false;
  const parsed=new URL(link.path,'https://lefteria.invalid');
  const scope=parsed.searchParams.get('scope');
  const selectedOwner=parsed.searchParams.get('organization_id');
  if(owner==='lefteria_fc_academy'){
    if(link.parent==='team'||link.parent==='matches'||link.parent==='shop'||
       /^\/(team|trials|shop)(?:\/|\?|$)/.test(link.path)||
       scope==='first_team'||parsed.pathname==='/sponsors/first-team')return false;
    if(['/news','/about','/contact','/pages'].some(route=>parsed.pathname===route||
       route==='/pages'&&parsed.pathname.startsWith('/pages/')) &&
       selectedOwner!=='lefteria_fc_academy')return false;
    if(['/events','/partners','/sponsors'].includes(parsed.pathname)&&scope!=='academy')return false;
  }else if(link.parent==='academy'||/^\/academy(?:\/|\?|$)/.test(link.path)||
    scope==='academy'||parsed.pathname==='/sponsors/academy'||
    selectedOwner==='lefteria_fc_academy')return false;
  return true;
};
export function parseNavigationDesign(raw:string,owner:PublicOrganization):NavigationDesign|null{
  if(typeof raw!=='string'||raw.length>5000)return null;
  let value:unknown;try{value=JSON.parse(raw)}catch{return null}
  if(!value||typeof value!=='object'||Array.isArray(value)||
     Object.keys(value).length!==1||!Object.hasOwn(value,'links'))return null;
  const links=(value as NavigationDesign).links;
  if(!Array.isArray(links)||links.length>24||!links.every(x=>validLink(x,owner))||
    new Set(links.map(x=>x.id)).size!==links.length)return null;
  return {links};
}
export function publishedNavigationDesign(pages:readonly PublishedWebsitePage[],owner:PublicOrganization):NavigationDesign|null{
  const row=pages.find(item=>item.slug===NAVIGATION_DESIGN_SLUG&&isPublishedWebsitePage(item,owner));
  const page=row?.page;
  return page?.menu_parent==='hidden'&&page.menu_order===0&&page.blocks.length===1&&
    page.blocks[0].type==='paragraph'&&page.blocks[0].url===null
    ?parseNavigationDesign(page.blocks[0].text,owner):null;
}
/** Custom links only supplement existing verified public navigation and CMS
 * pages. A submenu can be attached to existing categories without replacing
 * operational destinations or their permission checks. */
export function withNavigationDesign(base:readonly PublicNavigationItem[],design:NavigationDesign|null):PublicNavigationItem[]{
  const result=base.map(item=>item.items?{...item,items:[...item.items]}:{...item}) as PublicNavigationItem[];
  if(!design)return result;
  for(const link of [...design.links].sort((a,b)=>a.order-b.order||a.id.localeCompare(b.id))){
    if(link.parent==='root'){
      result.splice(Math.min(link.order,result.length),0,{id:link.id,label:link.label,path:link.path,icon:'info'});
      continue;
    }
    const i=result.findIndex(item=>item.id===link.parent);
    if(i<0)continue;
    const item=result[i];
    const links:PublicMenuLink[]=item.items?[...item.items]:item.path?[[item.id+'-home',item.label,item.path]]:[];
    links.splice(Math.min(link.order,links.length),0,[link.id,link.label,link.path]);
    result[i]={id:item.id,label:item.label,icon:item.icon,items:links};
  }
  return result;
}
