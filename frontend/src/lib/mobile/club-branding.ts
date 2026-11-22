/** Public-only branding projection for the private mobile shell.
 * No session token, private player data or admin endpoints are involved. */
export type ClubBrand = {
  name:string;logoUrl:string|null;organizationId:'lefteria_fc'|'lefteria_fc_academy';
  email:string|null;phone:string|null;address:string|null;
  socialLinks:Partial<Record<'facebook'|'instagram'|'twitter'|'youtube'|'tiktok',string>>;
};
export type BrandOwner=ClubBrand['organizationId'];

export function brandOwnerForRole(role:string|null,selectedOrganization:string|null):BrandOwner|null{
  if(role==='management'){
    return selectedOrganization==='lefteria_fc'||selectedOrganization==='lefteria_fc_academy'
      ?selectedOrganization:null;
  }
  if(role==='fan')return 'lefteria_fc';
  if(role==='parent'||role==='player'||role==='coach')return 'lefteria_fc_academy';
  return null;
}

export function publicLogoUrl(value:unknown):string|null{
  if(typeof value!=='string'||value.length>2048||value!==value.trim())return null;
  if(value.startsWith('/')&&!value.startsWith('//')&&!value.includes('\\')&&
      !/[\u0000-\u001f]/.test(value))return value;
  try{
    const url=new URL(value);
    return url.protocol==='https:'&&!url.username&&!url.password?value:null;
  }catch{return null;}
}

export function readPublicBrand(raw:unknown,organizationId:BrandOwner):ClubBrand|null{
  if(!raw||typeof raw!=='object'||Array.isArray(raw))return null;
  const site=raw as Record<string,unknown>;
  const club=site.club;
  if(!club||typeof club!=='object'||Array.isArray(club))return null;
  const record=club as Record<string,unknown>;
  if(record.organization_id!==organizationId||typeof record.name!=='string'||
      !record.name.trim()||record.name.length>200)return null;
  // Only project explicitly public fields. Filter links independently rather
  // than trusting a raw API object to become a browser navigation target.
  const email=typeof record.email==='string'&&
    /^[^\\s@<>:]+@[^\\s@<>:]+\\.[^\\s@<>:]+$/.test(record.email)
      ?record.email:null;
  const phone=typeof record.phone==='string'&&
    /^[+0-9 ()-]{5,40}$/.test(record.phone)?record.phone:null;
  const address=typeof record.address==='string'&&record.address.length<=1000
      ?record.address:null;
  const socials:ClubBrand['socialLinks']={};
  if(record.social_links&&typeof record.social_links==='object'&&!Array.isArray(record.social_links)){
    const links=record.social_links as Record<string,unknown>;
    for(const key of ['facebook','instagram','twitter','youtube','tiktok'] as const){
      const value=links[key];
      if(typeof value==='string'&&value.length<=2048){
        try{
          const url=new URL(value);
          if(url.protocol==='https:'&&!url.username&&!url.password)socials[key]=value;
        }catch{/* Never render a malformed social destination. */}
      }
    }
  }
  return {organizationId,name:record.name,logoUrl:publicLogoUrl(record.logo_url),
    email,phone,address,socialLinks:socials};
}
