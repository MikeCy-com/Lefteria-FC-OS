import type { PublicOrganization } from '../public/types.ts';

// Only already public images. Never inspect private player, guardian or CRM media.
export type MediaSource = 'gallery'|'sponsor'|'partner'|'event';
export type PublishedMediaAsset = {
  id:string; organization_id:PublicOrganization; source:MediaSource;
  label:string; alt:string; url:string;
};
export function publishedMediaUrl(value:unknown):string|null {
  if(typeof value!=='string'||!value||value.length>2048||
     /[\u0000-\u001f\u007f\\<>"']/.test(value)||value.trim()!==value)return null;
  try {
    if(value.startsWith('/')&&!value.startsWith('//')){
      const path=new URL(value,'https://lefteriafc.cy').pathname.toLowerCase();
      if(/^\/(?:admin|app)(?:\/|$)/.test(path)||
         (path.startsWith('/api/')&&!path.startsWith('/api/uploads/')))return null;
      return value;
    }
    if(!/^https:\/\//i.test(value))return null;
    const uri=new URL(value);
    return uri.protocol==='https:'&&uri.hostname&&!uri.username&&!uri.password?value:null;
  }catch{return null;}
}
const named=(x:unknown):string|null=>typeof x==='string'&&x.trim().length>0&&x.length<=180&&!/[\u0000-\u001f\u007f]/.test(x)?x.trim():null;
const validId=(x:unknown):x is string=>typeof x==='string'&&x.length>0&&x.length<=128&&!/[\u0000-\u001f\u007f]/.test(x);
/** null means that the public projection is unavailable/malformed; [] means valid empty. */
export function collectPublishedMedia(input:unknown,owner:PublicOrganization):PublishedMediaAsset[]|null {
  if((owner!=='lefteria_fc'&&owner!=='lefteria_fc_academy')||
     !input||typeof input!=='object'||Array.isArray(input))return null;
  const data=input as Record<string,unknown>;
  if(data.organization_id!==owner||
     !['gallery','sponsors','partners','events'].every(key=>Array.isArray(data[key])))return null;
  const result:PublishedMediaAsset[]=[];
  const used=new Set<string>();
  function add(source:MediaSource,item:unknown,field:string,suffix=''){
    if(!item||typeof item!=='object'||Array.isArray(item))return;
    const row=item as Record<string,unknown>;
    if(row.organization_id!==owner||!validId(row.id))return;
    const url=publishedMediaUrl(row[field]);
    const label=named(row.title)??named(row.name);
    if(!url||!label||used.has(url)||result.length>=400)return;
    used.add(url);
    result.push({id:owner+':'+source+':'+row.id+':'+field,organization_id:owner,
      source,label:label+suffix,alt:label,url});
  }
  for(const row of (data.gallery as unknown[]).slice(0,500))add('gallery',row,'image_url');
  for(const row of (data.sponsors as unknown[]).slice(0,500)){
    add('sponsor',row,'logo_url',' · λογότυπο');
    add('sponsor',row,'banner_url',' · banner');
  }
  for(const row of (data.partners as unknown[]).slice(0,500)){
    add('partner',row,'hero_image_url');
    add('partner',row,'headshot_url');
  }
  for(const row of (data.events as unknown[]).slice(0,500))add('event',row,'cover_image_url');
  return result;
}
