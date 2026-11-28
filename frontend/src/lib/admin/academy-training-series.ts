import {validTrainingPlanForm,type TrainingPlanForm,type TrainingPlanDraft} from './academy-training-plans.ts';

export type WeeklySlot={weekday:number;start_time:string;duration_minutes:number};
export type SeriesInput={start:string;end:string;title:string;location:string;notes:string;slots:WeeklySlot[];excludedDays?:string[]};
const dayPattern=/^\d{4}-\d{2}-\d{2}$/;
const dayMs=86400000;
function timestamp(day:string):number{
  if(!dayPattern.test(day))return NaN;
  const n=Date.parse(day+'T00:00:00Z');
  return Number.isFinite(n)&&new Date(n).toISOString().slice(0,10)===day?n:NaN;
}
export function trainingSeries(input:SeriesInput):TrainingPlanForm[]|null{
  const start=timestamp(input.start),end=timestamp(input.end);
  if(!Number.isFinite(start)||!Number.isFinite(end)||end<start||end-start>365*dayMs||
    input.slots.length<1||input.slots.length>7||
    new Set(input.slots.map(slot=>slot.weekday)).size!==input.slots.length||
    input.slots.some(slot=>!Number.isInteger(slot.weekday)||slot.weekday<0||slot.weekday>6))return null;
  const excluded=input.excludedDays??[];
  if(excluded.length>366||new Set(excluded).size!==excluded.length||
    excluded.some(day=>!Number.isFinite(timestamp(day))||timestamp(day)<start||timestamp(day)>end||
      !input.slots.some(slot=>slot.weekday===new Date(timestamp(day)).getUTCDay())))return null;
  const skip=new Set(excluded);
  const rows:TrainingPlanForm[]=[];
  for(let n=start;n<=end;n+=dayMs){
    const date=new Date(n),slot=input.slots.find(s=>s.weekday===date.getUTCDay());
    if(!slot||skip.has(date.toISOString().slice(0,10)))continue;
    const plan={day:date.toISOString().slice(0,10),start_time:slot.start_time,
      duration_minutes:slot.duration_minutes,title:input.title,location:input.location,notes:input.notes};
    if(!validTrainingPlanForm(plan)||minute(plan.start_time)+plan.duration_minutes>1440)return null;
    rows.push(plan);
    if(rows.length>180)return null;
  }
  return rows.length?rows:null;
}
export function trainingSeriesWindows(start:string,end:string):{start:string;end:string}[]{
  const a=timestamp(start),b=timestamp(end);
  if(!Number.isFinite(a)||!Number.isFinite(b)||b<a||b-a>365*dayMs)return [];
  const windows:{start:string;end:string}[]=[];
  for(let n=a;n<=b;n+=94*dayMs){
    windows.push({start:new Date(n).toISOString().slice(0,10),
      end:new Date(Math.min(n+93*dayMs,b)).toISOString().slice(0,10)});
  }
  return windows;
}
function minute(time:string){const [h,m]=time.split(':').map(Number);return h*60+m;}
export function seriesPlanStatus(plan:TrainingPlanForm,existing:readonly TrainingPlanDraft[]):'new'|'existing'|'conflict'{
  const overlapping=existing.filter(p=>p.status==='draft'&&p.day===plan.day&&
    minute(p.start_time)<minute(plan.start_time)+plan.duration_minutes&&
    minute(plan.start_time)<minute(p.start_time)+p.duration_minutes);
  if(!overlapping.length)return 'new';
  if(overlapping.length===1&&overlapping.every(p=>p.start_time===plan.start_time&&p.duration_minutes===plan.duration_minutes&&
    p.title===plan.title&&p.location===plan.location&&p.notes===plan.notes))return 'existing';
  return 'conflict';
}
