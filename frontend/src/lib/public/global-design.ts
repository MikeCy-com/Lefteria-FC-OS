import {isPublishedWebsitePage,type PublishedWebsitePage,type WebsitePageLayout} from './website-pages.ts';
import type {PublicOrganization} from './types.ts';

/** Published theme tokens are curated values, never arbitrary CSS or font URLs. */
export const GLOBAL_DESIGN_SLUG='canonical-global-design' as const;
export const TEXT_ROLES=['h1','h2','h3','h4','h5','h6','p','button','nav','caption','label','card'] as const;
export type TextRole=typeof TEXT_ROLES[number];
export type TextStyle={family:'condensed'|'sans'|'serif';desktop:number;mobile:number;weight:400|500|600|700|800;line_height:number};
export type GlobalDesignLayout={gutter:number;section_gap:number;card_gap:number;card_radius:number;button_radius:number;motto_height:number;page_max_width:number;page_gap:number;page_side_padding:number;page_margin_top:number;page_margin_bottom:number};
export const DEFAULT_GLOBAL_LAYOUT:GlobalDesignLayout={gutter:28,section_gap:32,card_gap:16,card_radius:9,button_radius:5,motto_height:120,page_max_width:1280,page_gap:24,page_side_padding:16,page_margin_top:0,page_margin_bottom:0};
const layoutValues:Record<keyof GlobalDesignLayout,readonly number[]>={
  gutter:[12,16,20,24,28,32,40,48,56,64],
  section_gap:[12,16,20,24,32,40,48,64,80],
  card_gap:[8,12,16,20,24,32,40],
  card_radius:[0,4,6,9,12,16,20,24],
  button_radius:[0,4,5,8,12,16,24],
  motto_height:[80,100,120,160,240,320,360],
  page_max_width:[960,1120,1280,1440],
  page_gap:[8,16,24,32,48],
  page_side_padding:[12,16,24,32,48],
  page_margin_top:[0,8,12,16,24,32,48,64,80],
  page_margin_bottom:[0,8,12,16,24,32,48,64,80]
};
export const GLOBAL_LAYOUT_CHOICES=layoutValues;
export type GlobalDesign={text:Record<TextRole,TextStyle>;colors:{background:string;foreground:string;accent:string;surface:string;muted:string;border:string;link:string};layout?:GlobalDesignLayout};
const initial:Record<TextRole,[number,number]>={h1:[48,32],h2:[36,28],h3:[28,23],h4:[22,20],h5:[20,18],h6:[18,16],p:[16,15],button:[14,14],nav:[14,14],caption:[12,12],label:[13,13],card:[16,15]};
export const DEFAULT_GLOBAL_DESIGN:GlobalDesign={
  text:Object.fromEntries(TEXT_ROLES.map(role=>[role,{family:role==='p'?'sans':'condensed',desktop:initial[role][0],mobile:initial[role][1],weight:role==='p'?400:700,line_height:role==='p'?1.6:1.2}])) as GlobalDesign['text'],
  colors:{background:'#090909',foreground:'#ffffff',accent:'#f6bb38',surface:'#171717',muted:'#bdbdbd',border:'#494949',link:'#f6bb38'},
  layout:{...DEFAULT_GLOBAL_LAYOUT}
};
const exact=(value:unknown,names:readonly string[])=>!!value&&typeof value==='object'&&!Array.isArray(value)&&
  Object.keys(value).length===names.length&&Object.keys(value).every(key=>names.includes(key));
const size=(x:unknown)=>typeof x==='number'&&Number.isInteger(x)&&x>=12&&x<=80;
const hex=(x:unknown)=>typeof x==='string'&&/^#[0-9a-fA-F]{6}$/.test(x);
// Enforce readable color pairings before a theme can be published.
export function colorContrast(a:string,b:string):number {
  const luminance=(hexColor:string)=>{
    const linear=[1,3,5].map(pos=>{
      const c=parseInt(hexColor.slice(pos,pos+2),16)/255;
      return c<=0.04045?c/12.92:Math.pow((c+0.055)/1.055,2.4);
    });
    return linear[0]*0.2126+linear[1]*0.7152+linear[2]*0.0722;
  };
  const x=luminance(a),y=luminance(b);
  return (Math.max(x,y)+0.05)/(Math.min(x,y)+0.05);
}

export function parseGlobalDesign(text:string):GlobalDesign|null {
  if(typeof text!=='string'||text.length>5000)return null;
  let value:unknown;try{value=JSON.parse(text)}catch{return null}
  if(!exact(value,['text','colors'])&&!exact(value,['text','colors','layout']))return null;
  const theme=value as GlobalDesign;
  if(!exact(theme.text,TEXT_ROLES)||!exact(theme.colors,['background','foreground','accent','surface','muted','border','link'])||
    !hex(theme.colors.background)||!hex(theme.colors.foreground)||!hex(theme.colors.accent)||!hex(theme.colors.surface)||!hex(theme.colors.muted)||!hex(theme.colors.border)||!hex(theme.colors.link))return null;
  if(colorContrast(theme.colors.surface,theme.colors.foreground)<4.5 ||
     colorContrast(theme.colors.surface,theme.colors.muted)<4.5 ||
     colorContrast(theme.colors.surface,theme.colors.link)<4.5 ||
     colorContrast(theme.colors.background,theme.colors.foreground)<4.5 ||
     colorContrast(theme.colors.background,theme.colors.muted)<4.5 ||
     colorContrast(theme.colors.background,theme.colors.link)<4.5 ||
     colorContrast(theme.colors.background,theme.colors.accent)<3)return null;
  if(theme.layout!==undefined){
    if(!exact(theme.layout,Object.keys(layoutValues))&&!exact(theme.layout,Object.keys(layoutValues).slice(0,6)))return null;
    for(const key of Object.keys(theme.layout) as (keyof GlobalDesignLayout)[])
      if(!layoutValues[key].includes(theme.layout[key]))return null;
  }
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
  const layout={...DEFAULT_GLOBAL_LAYOUT,...theme.layout};
  return [...parts,...(Object.keys(layoutValues) as (keyof GlobalDesignLayout)[]).map(key=>'--lf-layout-'+key.replaceAll('_','-')+':'+layout[key]+'px'),'--lf-color-bg:'+theme.colors.background,
    '--lf-color-text:'+theme.colors.foreground,'--lf-color-accent:'+theme.colors.accent,
    '--lf-color-surface:'+theme.colors.surface,'--lf-color-muted:'+theme.colors.muted,
    '--lf-color-border:'+theme.colors.border,'--lf-color-link:'+theme.colors.link].join(';');
}

/** Page defaults are inherited from the published organization theme; explicit
 * WYSIWYG page settings override each property independently. */
export function globalPageDefaults(theme:GlobalDesign|null):WebsitePageLayout {
  const values={...DEFAULT_GLOBAL_LAYOUT,...theme?.layout};
  return {max_width:values.page_max_width as WebsitePageLayout['max_width'],
    gap:values.page_gap as WebsitePageLayout['gap'],
    side_padding:values.page_side_padding as WebsitePageLayout['side_padding'],
    margin_top:values.page_margin_top as WebsitePageLayout['margin_top'],
    margin_bottom:values.page_margin_bottom as WebsitePageLayout['margin_bottom']};
}
