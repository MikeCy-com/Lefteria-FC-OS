export type TrainingPlanStatus='draft'|'archived';
export interface TrainingPlanDraft {
  id:string;organization_id:'lefteria_fc_academy';group_id:string;
  day:string;start_time:string;duration_minutes:number;
  title:string;location:string;notes:string;status:TrainingPlanStatus;version:number;
}
export interface TrainingPlanPage {
  organization_id:'lefteria_fc_academy';group_id:string;
  from:string;to:string;total:number;plans:TrainingPlanDraft[];
}
export interface TrainingPlanForm {
  day:string;start_time:string;duration_minutes:number;
  title:string;location:string;notes:string;
}
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const date=/^\d{4}-\d{2}-\d{2}$/;
const clock=/^([01][0-9]|2[0-3]):[0-5][0-9]$/;
const isDate=(d:unknown):d is string=>typeof d==='string'&&date.test(d)&&
  !Number.isNaN(Date.parse(d))&&new Date(d).toISOString().slice(0,10)===d;
const rec=(x:unknown):x is Record<string,unknown>=>
  x!==null&&typeof x==='object'&&!Array.isArray(x);
const safe=(x:unknown,max:number,empty=false)=>typeof x==='string'&&
  x.length<=max&&(empty||x.length>0)&&!/[\x00-\x1f\x7f]/.test(x);
export function validTrainingPlanDraft(
  value:unknown,group:string
):value is TrainingPlanDraft {
  if(!rec(value))return false;
  return typeof value.id==='string'&&uuid.test(value.id)&&
    value.organization_id==='lefteria_fc_academy'&&value.group_id===group&&
    isDate(value.day)&&typeof value.start_time==='string'&&clock.test(value.start_time)&&
    Number.isSafeInteger(value.duration_minutes)&&
    (value.duration_minutes as number)>=15&&(value.duration_minutes as number)<=240&&
    safe(value.title,180)&&value.title===(value.title as string).trim()&&
    safe(value.location,200,true)&&safe(value.notes,2000,true)&&
    (value.status==='draft'||value.status==='archived')&&
    Number.isSafeInteger(value.version)&&(value.version as number)>=1&&
    (value.version as number)<=10000;
}
export function validTrainingPlanPage(
  value:unknown,group:string,from:string,to:string
):value is TrainingPlanPage {
  if(!rec(value)||value.organization_id!=='lefteria_fc_academy'||
    value.group_id!==group||value.from!==from||value.to!==to||
    !Number.isSafeInteger(value.total)||(value.total as number)<0||
    (value.total as number)>500||!Array.isArray(value.plans)||
    value.plans.length!==(value.total as number))return false;
  const ids=new Set<string>();
  return value.plans.every((row:unknown)=>{
    if(!validTrainingPlanDraft(row,group)||row.day<from||row.day>to||
      ids.has(row.id))return false;
    ids.add(row.id);return true;
  });
}
export function validTrainingPlanForm(value:TrainingPlanForm):boolean{
  return isDate(value.day)&&clock.test(value.start_time)&&
    Number.isSafeInteger(value.duration_minutes)&&
    value.duration_minutes>=15&&value.duration_minutes<=240&&
    safe(value.title,180)&&value.title===value.title.trim()&&
    safe(value.location,200,true)&&safe(value.notes,2000,true);
}
export function matchingTrainingPlanWrite(
  value:unknown,group:string,id:string|undefined,
  version:number,status:TrainingPlanStatus
):value is TrainingPlanDraft {
  return validTrainingPlanDraft(value,group)&&
    (id===undefined||value.id===id)&&value.version===version&&
    value.status===status;
}

export interface TrainingPlanApprovals{
  organization_id:'lefteria_fc_academy';group_id:string;from:string;to:string;
  published:false;approvals:{id:string;approved_version:number}[];
}
export function validTrainingPlanApprovals(
  value:unknown,group:string,from:string,to:string,
  plans:readonly TrainingPlanDraft[]
):value is TrainingPlanApprovals{
  if(!rec(value)||value.organization_id!=='lefteria_fc_academy'||
    value.group_id!==group||value.from!==from||value.to!==to||
    value.published!==false||!Array.isArray(value.approvals)||
    value.approvals.length>plans.length)return false;
  const known=new Map(plans.map(p=>[p.id,p]));
  const seen=new Set<string>();
  return value.approvals.every((entry:unknown)=>{
    if(!rec(entry)||typeof entry.id!=='string'||seen.has(entry.id))return false;
    const plan=known.get(entry.id);
    if(!plan||plan.status!=='draft'||entry.approved_version!==plan.version)return false;
    seen.add(entry.id);
    return true;
  });
}
export function validTrainingPlanApprovalResult(
  value:unknown,plan:TrainingPlanDraft
):boolean{
  return rec(value)&&value.id===plan.id&&
    value.organization_id==='lefteria_fc_academy'&&
    value.approved_version===plan.version&&
    value.approval_status==='approved'&&value.published===false;
}

export interface TrainingDistributionPreview {
  id:string;organization_id:'lefteria_fc_academy';group_id:string;
  version:number;day:string;start_time:string;duration_minutes:number;
  title:string;location:string;eligible_player_count:number;
  human_approved:true;published:false;
}
export function validTrainingDistributionPreview(
  value:unknown,plan:TrainingPlanDraft
):value is TrainingDistributionPreview {
  if(!rec(value)||value.id!==plan.id||
    value.organization_id!=='lefteria_fc_academy'||value.group_id!==plan.group_id||
    value.version!==plan.version||value.day!==plan.day||
    value.start_time!==plan.start_time||value.duration_minutes!==plan.duration_minutes||
    value.title!==plan.title||value.location!==plan.location||
    value.human_approved!==true||value.published!==false||
    !Number.isSafeInteger(value.eligible_player_count)||
    (value.eligible_player_count as number)<0||
    (value.eligible_player_count as number)>100000||
    'notes' in value||'players' in value||'player_ids' in value||
    'phone' in value||'emails' in value)return false;
  return true;
}

export interface TrainingPublicationRecord {
  id:string;event_id:string;published_version:number;
  is_cancelled:boolean;cancel_reason:string|null;
}
export interface TrainingPublicationsPage {
  organization_id:'lefteria_fc_academy';group_id:string;from:string;to:string;
  publications:TrainingPublicationRecord[];
}
export function validTrainingPublicationsPage(
  value:unknown,group:string,from:string,to:string,
  plans:readonly TrainingPlanDraft[]
):value is TrainingPublicationsPage {
  if(!rec(value)||value.organization_id!=='lefteria_fc_academy'||
    value.group_id!==group||value.from!==from||value.to!==to||
    !Array.isArray(value.publications)||value.publications.length>plans.length)return false;
  const known=new Map(plans.map(p=>[p.id,p]));
  const seen=new Set<string>();
  return value.publications.every((row:unknown)=>{
    if(!rec(row)||typeof row.id!=='string'||seen.has(row.id))return false;
    const plan=known.get(row.id);
    if(!plan||row.published_version!==plan.version||
      row.event_id!=='os-training-'+row.id||
      typeof row.is_cancelled!=='boolean'||
      (row.is_cancelled ? (
        typeof row.cancel_reason!=='string'||
        row.cancel_reason.length<8||row.cancel_reason.length>500||
        row.cancel_reason!==row.cancel_reason.trim()||
        /[\x00-\x1f\x7f]/.test(row.cancel_reason)
      ) : row.cancel_reason!==null))return false;
    seen.add(row.id);return true;
  });
}
export function validPublishedTrainingResult(
  value:unknown,plan:TrainingPlanDraft
):value is {id:string;event_id:string;published_version:number;
  organization_id:'lefteria_fc_academy';event_type:'training';
  published:true;notifications_sent:false} {
  return rec(value)&&value.id===plan.id&&
    value.organization_id==='lefteria_fc_academy'&&
    value.event_id==='os-training-'+plan.id&&
    value.event_type==='training'&&value.published_version===plan.version&&
    value.published===true&&value.notifications_sent===false;
}

export function validTrainingCancellationResult(
  value:unknown,plan:TrainingPlanDraft,reason:string
):boolean{
  return rec(value)&&value.id===plan.id&&
    value.organization_id==='lefteria_fc_academy'&&
    value.event_id==='os-training-'+plan.id&&
    value.published_version===plan.version&&
    value.cancelled===true&&value.cancel_reason===reason&&
    value.notifications_sent===false;
}
