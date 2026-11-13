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
