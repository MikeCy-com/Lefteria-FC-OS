import {validDrillForm,type Drill,type DrillForm} from './central-academy-drills.ts';
import type {TrainingPlanDraft} from './academy-training-plans.ts';

export interface TrainingDrillSnapshot extends DrillForm {
  drill_id:string;drill_version:number;
}
export interface TrainingDrillPlan {
  id:string;organization_id:'lefteria_fc_academy';group_id:string;
  version:number;status:'draft'|'archived';duration_minutes:number;drills:TrainingDrillSnapshot[];
}
export interface TrainingDrillSelection {drill_id:string;drill_version:number;duration_minutes:number}
const rec=(x:unknown):x is Record<string,unknown>=>x!==null&&typeof x==='object'&&!Array.isArray(x);
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const keys=['drill_id','drill_version','title','category','age_band','duration_minutes','objective','instructions'];
export function validTrainingDrillSnapshot(x:unknown):x is TrainingDrillSnapshot {
  if(!rec(x)||Object.keys(x).length!==keys.length||!Object.keys(x).every(k=>keys.includes(k))||
    typeof x.drill_id!=='string'||!uuid.test(x.drill_id)||!Number.isSafeInteger(x.drill_version)||
    (x.drill_version as number)<1||(x.drill_version as number)>10000)return false;
  return validDrillForm(x);
}
export function plannedMinutes(drills:readonly TrainingDrillSnapshot[]):number {
  return drills.reduce((sum,row)=>sum+row.duration_minutes,0);
}
export function validTrainingDrillSequence(drills:readonly TrainingDrillSnapshot[],duration:number):boolean {
  if(!Number.isSafeInteger(duration)||duration<15||duration>240||drills.length>24)return false;
  const seen=new Set<string>();
  for(const row of drills){
    if(!validTrainingDrillSnapshot(row)||seen.has(row.drill_id))return false;
    seen.add(row.drill_id);
  }
  return plannedMinutes(drills)<=duration;
}
export function validTrainingDrillPlan(x:unknown,plan:TrainingPlanDraft,version:number):x is TrainingDrillPlan {
  return rec(x)&&Object.keys(x).length===7&&x.id===plan.id&&x.organization_id==='lefteria_fc_academy'&&
    x.group_id===plan.group_id&&x.version===version&&x.status===plan.status&&
    x.duration_minutes===plan.duration_minutes&&Array.isArray(x.drills)&&validTrainingDrillSequence(x.drills,plan.duration_minutes);
}
export function snapshotDrill(drill:Drill):TrainingDrillSnapshot {
  return {drill_id:drill.id,drill_version:drill.version,title:drill.title,category:drill.category,
    age_band:drill.age_band,duration_minutes:drill.duration_minutes,objective:drill.objective,instructions:drill.instructions};
}
export function drillSelections(drills:readonly TrainingDrillSnapshot[]):TrainingDrillSelection[] {
  return drills.map(({drill_id,drill_version,duration_minutes})=>({drill_id,drill_version,duration_minutes}));
}
export function matchingTrainingDrillSave(x:unknown,plan:TrainingPlanDraft,drills:readonly TrainingDrillSnapshot[]):x is TrainingDrillPlan {
  return validTrainingDrillPlan(x,plan,plan.version+1)&&x.drills.length===drills.length&&
    x.drills.every((row,i)=>{const expected=drills[i];return !!expected&&keys.every(k=>
      row[k as keyof TrainingDrillSnapshot]===expected[k as keyof TrainingDrillSnapshot]);});
}
