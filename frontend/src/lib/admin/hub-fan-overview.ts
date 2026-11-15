export type FanHubOwner='lefteria_fc'|'lefteria_fc_academy';
export type FanTrack='parent'|'player'|'both';
export interface FanHubPlan {
  track:FanTrack;
  monthly_price_cents:number;
  is_enabled:boolean;
  currency:'EUR';
  duration_days:30;
}
export interface FanHubOverview {
  organization_id:FanHubOwner;
  as_of:string;
  counts:{
    total_records:number;
    pending:number;
    recorded_active:number;
    verified_active:number;
    past_due:number;
    cancelled:number;
    expired:number;
  };
  plans:FanHubPlan[];
}
const whole=(value:unknown):value is number=>
  Number.isSafeInteger(value)&&(value as number)>=0;
const keys=(value:Record<string,unknown>,names:string[]) =>
  Object.keys(value).sort().join('|')===names.slice().sort().join('|');

/**
 * This contract intentionally contains NO subscriber IDs, names, phones,
 * provider references, checkout links, payment requests or individual rows.
 * A recorded active status is not an independently verified entitlement.
 */
export function validFanHubOverview(raw:unknown,owner:FanHubOwner):raw is FanHubOverview {
  if(!raw||typeof raw!=='object'||Array.isArray(raw))return false;
  const d=raw as Record<string,unknown>;
  if(!keys(d,['organization_id','as_of','counts','plans'])||
     d.organization_id!==owner||typeof d.as_of!=='string'||
     !Number.isFinite(Date.parse(d.as_of))||
     !d.counts||typeof d.counts!=='object'||Array.isArray(d.counts)||
     !Array.isArray(d.plans)||d.plans.length>3)return false;
  const counts=d.counts as Record<string,unknown>;
  const fields=['total_records','pending','recorded_active','verified_active','past_due','cancelled','expired'];
  if(!keys(counts,fields)||!fields.every(field=>whole(counts[field]))||
     (counts.verified_active as number)>(counts.recorded_active as number)||
     (counts.pending as number)+(counts.recorded_active as number)+
     (counts.past_due as number)+(counts.cancelled as number)+
     (counts.expired as number)!==(counts.total_records as number))return false;
  const tracks=new Set<string>();
  for(const item of d.plans){
    if(!item||typeof item!=='object'||Array.isArray(item))return false;
    const plan=item as Record<string,unknown>;
    if(!keys(plan,['track','monthly_price_cents','is_enabled','currency','duration_days'])||
       !['parent','player','both'].includes(String(plan.track))||
       tracks.has(String(plan.track))||
       !whole(plan.monthly_price_cents)||(plan.monthly_price_cents as number)<100||
       (plan.monthly_price_cents as number)>1000000||
       typeof plan.is_enabled!=='boolean'||plan.currency!=='EUR'||plan.duration_days!==30)return false;
    tracks.add(String(plan.track));
  }
  return true;
}
