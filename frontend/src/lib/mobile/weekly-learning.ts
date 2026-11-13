export type WeeklyLearningOrganization='lefteria_fc'|'lefteria_fc_academy';
export type WeeklyLearningAudience='parent'|'player'|'fan_parent'|'fan_player';
export interface WeeklyLearningDelivery {
  id:string;week:number;content_id:string;title:string;body:string;
  delivered_at:string;
}
export interface WeeklyLearningEnrollment {
  organization_id:WeeklyLearningOrganization;
  audience:WeeklyLearningAudience;
  started_at:string;next_due_at:string;
  delivery_week:number;status:'active'|'paused'|'cancelled';
}
export interface WeeklyLearningInbox {
  organization_id:WeeklyLearningOrganization;
  audience:WeeklyLearningAudience;
  enrolled:boolean;
  started_at?:string|null;
  next_due_at?:string|null;
  delivery_week?:number|null;
  status?:'active'|'paused'|'cancelled'|null;
  deliveries:WeeklyLearningDelivery[];
}
const validDate=(v:unknown):v is string=>typeof v==='string'&&
  Number.isFinite(Date.parse(v))&&Date.parse(v)<=Date.now()+60000;
const validFutureDate=(v:unknown):v is string=>typeof v==='string'&&
  Number.isFinite(Date.parse(v));
const uuid=(v:unknown):v is string=>typeof v==='string'&&
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v);
const plain=(v:unknown,max:number):v is string=>
  typeof v==='string'&&v.trim().length>0&&v.length<=max;
const object=(v:unknown):v is Record<string,unknown>=>
  !!v&&typeof v==='object'&&!Array.isArray(v);
const validEnrollment=(v:Record<string,unknown>,owner:WeeklyLearningOrganization,
  audience:WeeklyLearningAudience)=>{
  if(v.organization_id!==owner||v.audience!==audience||
    !validDate(v.started_at)||!validFutureDate(v.next_due_at)||
    Date.parse(v.next_due_at)<Date.parse(v.started_at)||
    !Number.isInteger(v.delivery_week)||Number(v.delivery_week)<1||
    Number(v.delivery_week)>5200||
    !['active','paused','cancelled'].includes(String(v.status)))return false;
  return true;
};
export function validWeeklyLearningEnrollment(v:unknown,
  owner:WeeklyLearningOrganization,audience:WeeklyLearningAudience):
  v is WeeklyLearningEnrollment {
  return object(v)&&validEnrollment(v,owner,audience);
}
export function validWeeklyLearningInbox(v:unknown,
  owner:WeeklyLearningOrganization,audience:WeeklyLearningAudience):
  v is WeeklyLearningInbox {
  if(!object(v)||v.organization_id!==owner||v.audience!==audience||
    typeof v.enrolled!=='boolean'||!Array.isArray(v.deliveries)||
    v.deliveries.length>20)return false;
  if(v.enrolled&&!validEnrollment(v,owner,audience))return false;
  if(!v.enrolled&&v.deliveries.length!==0)return false;
  const seen=new Set<string>();
  return v.deliveries.every((item:unknown)=>{
    if(!object(item)||!uuid(item.id)||seen.has(item.id)||
      !uuid(item.content_id)||!Number.isInteger(item.week)||
      Number(item.week)<1||Number(item.week)>5200||
      !plain(item.title,180)||!plain(item.body,20000)||
      !validDate(item.delivered_at))return false;
    seen.add(item.id);return true;
  });
}
