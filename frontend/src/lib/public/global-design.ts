import {isPublishedWebsitePage,type PublishedWebsitePage} from './website-pages.ts';
import type {PublicOrganization} from './types.ts';

/** Published theme tokens are curated values, never arbitrary CSS or font URLs. */
export const GLOBAL_DESIGN_SLUG='canonical-global-design' as const;
export const TEXT_ROLES=['h1','h2','h3','h4','h5','h6','p','button','nav','caption','label','card'] as const;
export type TextRole=typeof TEXT_ROLES[number];
export type TextStyle={family:'condensed'|'sans'|'serif';desktop:number;mobile:number;weight:400|500|600|700|800;line_height:number};
export type GlobalDesign={text:Record<TextRole,TextStyle>;colors:{background:string;foreground:string;accent:string;surface:string;muted:string;border:string;link:string}};
const initial:Record<TextRole,[number,number]>={h1:[48,32],h2:[36,28],h3:[28,23],h4:[22,20],h5:[20,18],h6:[18,16],p:[16,15],button:[14,14],nav:[14,14],caption:[12,12],label:[13,13],card:[16,15]};
export const DEFAULT_GLOBAL_DESIGN:GlobalDesign={
  text:Object.fromEntries(TEXT_ROLES.map(role=>[role,{family:role==='p'?'sans':'condensed',desktop:initial[role][0],mobile:initial[role][1],weight:role==='p'?400:700,line_height:role==='p'?1.6:1.2}])) as GlobalDesign['text'],
  colors:{background:'#090909',foreground:'#ffffff',accent:'#f6bb38',surface:'#171717',muted:'#bdbdbd',border:'#494949',link:'#f6bb38'}
};
const exact=(value:unknown,names:readonly string[])=>!!value&&typeof value==='object'&&!Array.isArray(value)&&
  Object.keys(value).length===names.length&&Object.keys(value).every(key=>names.includes(key));
const size=(x:unknown)=>typeof x==='number'&&Number.isInteger(x)&&x>=12&&x<=80;
const hex=(x:unknown)=>typeof x==='string'&&/^#[0-9a-fA-F]{6}$/.test(x);
export function parseGlobalDesign(text:string):GlobalDesign|null {
  if(typeof text!=='string'||text.length>5000)return null;
  let value:unknown;try{value=JSON.parse(text)}catch{return null}
  if(!exact(value,['text','colors']))return null;
  const theme=value as GlobalDesign;
  if(!exact(theme.text,TEXT_ROLES)||!exact(theme.colors,['background','foreground','accent','surface','muted','border','link'])||
    !hex(theme.colors.background)||!hex(theme.colors.foreground)||!hex(theme.colors.accent)||!hex(theme.colors.surface)||!hex(theme.colors.muted)||!hex(theme.colors.border)||!hex(theme.colors.link))return null;
  for(const role of TEXT_ROLES) {
    const t=theme.text[role];
    if(!exact(t,['family','desktop','mobile','weight','line_height'])||
      !['condensed','sans','serif'].includes(t.family)||
      !size(t.desktop)||!size(t.mobile)||!([400,500,600,700,800].includes(t.weight))||
      ![1,1.15,1.2,1.35,1.5,1.6,1.8].includes(t.line_height))return null;
  }
  return theme;
}
export function publishedGlobalDesign(pages:readonly PublishedWebsitePage[],owner:PublicOrganization):GlobalDesign|null {
  const row=pages.find(page=>page.slug===GLOBAL_DESIGN_SLUG&&isPublishedWebsitePage(page,owner));
  const page=row?.page;
  return page?.menu_parent==='hidden'&&page.menu_order===0&&page.blocks.length===1&&
    page.blocks[0].type==='paragraph'&&page.blocks[0].url===null
    ?parseGlobalDesign(page.blocks[0].text):null;
}
const fonts={condensed:"'Fira Sans Extra Condensed',sans-serif",sans:"'Fira Sans',Arial,sans-serif",serif:"Georgia,serif"} as const;
export function globalDesignCssVars(theme:GlobalDesign|null):string {
  if(!theme)return '';
  const parts=TEXT_ROLES.flatMap(role=>{
    const t=theme.text[role];
    return ['--lf-'+role+'-family:'+fonts[t.family],
      '--lf-'+role+'-desktop:'+t.desktop+'px','--lf-'+role+'-mobile:'+t.mobile+'px',
      '--lf-'+role+'-weight:'+t.weight,'--lf-'+role+'-line:'+t.line_height];
  });
  return [...parts,'--lf-color-bg:'+theme.colors.background,
    '--lf-color-text:'+theme.colors.foreground,'--lf-color-accent:'+theme.colors.accent,
    '--lf-color-surface:'+theme.colors.surface,'--lf-color-muted:'+theme.colors.muted,
    '--lf-color-border:'+theme.colors.border,'--lf-color-link:'+theme.colors.link].join(';');
}
