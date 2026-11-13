export type HubOrganization='lefteria_fc'|'lefteria_fc_academy';
export type HubState='ai_draft'|'review_required'|'approved'|'published'|'rejected';
export type ReviewState='pending'|'passed'|'rejected';
export interface HubEditorialItem {
  id:string;organization_id:HubOrganization;title:string;
  status:HubState;version:number;kind:string;provenance:'ai'|'human';
  audience_parent:boolean;audience_player:boolean;
  age_min:number|null;age_max:number|null;
}
export interface HubMediaDescriptor {
  id:string;kind:string;mime_type:string;alt_text:string|null;
  moderation:'quarantined'|'reviewed'|'rejected';generated_by_ai:boolean;
}
export interface HubEditorialDetail extends HubEditorialItem {
  body:string;revision_id:string;revision:number;
  text_review:ReviewState;visual_review:ReviewState;
  visuals:HubMediaDescriptor[];
}
const uuid=(s:unknown)=>typeof s==='string'&&
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(s);
const role=(s:unknown):s is HubState=>['ai_draft','review_required','approved','published','rejected'].includes(String(s));
export const isHubOrganization=(x:unknown):x is HubOrganization=>
  x==='lefteria_fc'||x==='lefteria_fc_academy';
export function validHubItem(raw:unknown,owner:HubOrganization):raw is HubEditorialItem {
  if(!raw||typeof raw!=='object'||Array.isArray(raw))return false;
  const d=raw as Record<string,unknown>;
  return uuid(d.id)&&d.organization_id===owner&&role(d.status)&&
    typeof d.title==='string'&&d.title.length>=1&&d.title.length<=180&&
    typeof d.kind==='string'&&['article','drill','video','visual','course'].includes(d.kind)&&
    (d.provenance==='ai'||d.provenance==='human')&&
    Number.isSafeInteger(d.version)&&(d.version as number)>=1&&
    typeof d.audience_parent==='boolean'&&typeof d.audience_player==='boolean'&&
    (d.audience_parent||d.audience_player)&&
    (d.age_min===null&&d.age_max===null||
      typeof d.age_min==='number'&&typeof d.age_max==='number'&&
      Number.isSafeInteger(d.age_min)&&Number.isSafeInteger(d.age_max)&&
      d.age_min>=4&&d.age_max<=99&&d.age_max>=d.age_min);
}
export function validHubList(raw:unknown,owner:HubOrganization):raw is {organization_id:HubOrganization;items:HubEditorialItem[]} {
  if(!raw||typeof raw!=='object'||Array.isArray(raw))return false;
  const d=raw as Record<string,unknown>;
  return d.organization_id===owner&&Array.isArray(d.items)&&d.items.length<=100&&
    d.items.every((item:unknown)=>validHubItem(item,owner));
}
export function validHubDetail(raw:unknown,owner:HubOrganization):raw is HubEditorialDetail {
  if(!validHubItem(raw,owner))return false;
  const d=raw as unknown as Record<string,unknown>;
  return uuid(d.revision_id)&&Number.isSafeInteger(d.revision)&&
    (d.revision as number)>=1&&typeof d.body==='string'&&
    d.body.length>=1&&d.body.length<=20000&&
    ['pending','passed','rejected'].includes(String(d.text_review))&&
    ['pending','passed','rejected'].includes(String(d.visual_review))&&
    Array.isArray(d.visuals)&&d.visuals.length<=20&&
    d.visuals.every((item:unknown)=>{
      if(!item||typeof item!=='object'||Array.isArray(item))return false;
      const v=item as Record<string,unknown>;
      return uuid(v.id)&&v.kind==='image'&&v.mime_type==='image/jpeg'&&
        (v.alt_text===null||typeof v.alt_text==='string'&&v.alt_text.length<=500)&&
        ['quarantined','reviewed','rejected'].includes(String(v.moderation))&&
        typeof v.generated_by_ai==='boolean';
    });
}
export function validHubTransition(raw:unknown,owner:HubOrganization,id:string,
  version:number,status:HubState):boolean {
  if(!raw||typeof raw!=='object'||Array.isArray(raw))return false;
  const d=raw as Record<string,unknown>;
  return d.id===id&&d.organization_id===owner&&d.status===status&&
    d.version===version+1;
}
