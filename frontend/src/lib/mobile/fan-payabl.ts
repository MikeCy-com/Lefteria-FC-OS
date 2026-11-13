export type FanHubOrganization='lefteria_fc'|'lefteria_fc_academy';
export type FanHubTrack='parent'|'player'|'both';
export type FanHubAudience='parent'|'player';
export type FanHubStatusKind='none'|'pending'|'active'|'past_due';
export interface FanHubStatus{
 organization_id:FanHubOrganization;status:FanHubStatusKind;
 audience_parent:boolean;audience_player:boolean;
 expires_at:string|null;checkout_url:string|null;
}
export interface FanHubCheckout{
 id:string;status:'pending'|'active'|'past_due';created:boolean;
 payment_id:string|null;amount_cents:number|null;checkout_url:string|null;
}
export interface FanHubLesson{
 id:string;revision_id:string;organization_id:FanHubOrganization;
 audience:FanHubAudience;title:string;body:string;published_at:string;
}
export interface FanHubLessonPage{
 organization_id:FanHubOrganization;audience:FanHubAudience;next:FanHubLesson|null;
}
export interface FanHubLessonCompleted{
 organization_id:FanHubOrganization;audience:FanHubAudience;
 completed_id:string;completed_revision_id:string;
}
const validUuid=(x:unknown):x is string=>typeof x==='string'&&
 /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(x);
const record=(x:unknown):x is Record<string,unknown>=>!!x&&
 typeof x==='object'&&!Array.isArray(x);
const iso=(x:unknown):x is string=>typeof x==='string'&&
 Number.isFinite(Date.parse(x));
export const validHostedPayablUrl=(x:unknown):x is string=>{
 if(typeof x!=='string'||x.length>2048)return false;
 try{
  const u=new URL(x);
  return u.protocol==='https:'&&!u.username&&!u.password&&!u.hash&&
    (u.hostname==='sandbox.payabl.com'||u.hostname==='pay4.sandbox.payabl.com');
 }catch{return false;}
};
export function validFanHubStatus(v:unknown,org:FanHubOrganization):v is FanHubStatus{
 if(!record(v)||v.organization_id!==org||
  !['none','pending','active','past_due'].includes(v.status as string)||
  typeof v.audience_parent!=='boolean'||typeof v.audience_player!=='boolean'||
  (v.checkout_url!==null&&!validHostedPayablUrl(v.checkout_url))||
  (v.expires_at!==null&&!iso(v.expires_at)))return false;
 if(v.status==='active')
  return (v.audience_parent||v.audience_player)&&
    iso(v.expires_at)&&Date.parse(v.expires_at)>Date.now();
 return true;
}
export function validFanHubCheckout(v:unknown):v is FanHubCheckout{
 return record(v)&&validUuid(v.id)&&
   ['pending','active','past_due'].includes(v.status as string)&&
   typeof v.created==='boolean'&&
   (v.checkout_url==null||validHostedPayablUrl(v.checkout_url))&&
   (v.payment_id==null||validUuid(v.payment_id))&&
   (v.amount_cents==null||Number.isSafeInteger(v.amount_cents)&&
     Number(v.amount_cents)>=100&&Number(v.amount_cents)<=1000000);
}
export function validFanHubLesson(v:unknown,org:FanHubOrganization,
 audience:FanHubAudience):v is FanHubLessonPage{
 if(!record(v)||v.organization_id!==org||v.audience!==audience)return false;
 if(v.next===null)return true;
 if(!record(v.next))return false;
 const a=v.next;
 return validUuid(a.id)&&validUuid(a.revision_id)&&
   a.organization_id===org&&a.audience===audience&&
   typeof a.title==='string'&&a.title.trim().length>0&&a.title.length<=180&&
   typeof a.body==='string'&&a.body.trim().length>0&&a.body.length<=20000&&
   iso(a.published_at)&&Date.parse(a.published_at)<=Date.now();
}
export function validFanHubCompleted(v:unknown,page:FanHubLessonPage):v is FanHubLessonCompleted{
 return record(v)&&page.next!==null&&v.organization_id===page.organization_id&&
  v.audience===page.audience&&v.completed_id===page.next.id&&
  v.completed_revision_id===page.next.revision_id;
}
