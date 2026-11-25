export const drillCategories=['technical','tactical','physical','goalkeeping','warmup','game','recovery','other'] as const;
export const drillAges=['all','u6-u8','u9-u11','u12-u14','u15-u18','adult'] as const;
export const drillStatuses=['draft','approved','archived'] as const;
export type DrillCategory=typeof drillCategories[number];
export type DrillAge=typeof drillAges[number];
export type DrillStatus=typeof drillStatuses[number];
export interface DrillForm {
  title:string;category:DrillCategory;age_band:DrillAge;duration_minutes:number;objective:string;instructions:string;
}
export interface Drill extends DrillForm {
  id:string;organization_id:'lefteria_fc_academy';status:DrillStatus;version:number;
}
export type DrillPage={organization_id:'lefteria_fc_academy';drills:Drill[];next_offset:number|null};
const rec=(x:unknown):x is Record<string,unknown>=>x!==null&&typeof x==='object'&&!Array.isArray(x);
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const text=(x:unknown,max:number)=>typeof x==='string'&&x.length>=1&&x.length<=max&&x===x.trim()&&!/[\x00-\x1f\x7f-\x9f]/.test(x);
export function validDrillForm(x:unknown):x is DrillForm {
  return rec(x)&&text(x.title,180)&&drillCategories.some(v=>v===x.category)&&drillAges.some(v=>v===x.age_band)&&
    Number.isSafeInteger(x.duration_minutes)&&(x.duration_minutes as number)>=1&&(x.duration_minutes as number)<=120&&
    text(x.objective,1000)&&text(x.instructions,4000);
}
export function validDrill(x:unknown):x is Drill {
  if(!rec(x)||!validDrillForm(x))return false;
  const allowed=['id','organization_id','title','category','age_band','duration_minutes','objective','instructions','status','version'];
  return Object.keys(x).length===allowed.length&&Object.keys(x).every(k=>allowed.includes(k))&&
    typeof x.id==='string'&&uuid.test(x.id)&&x.organization_id==='lefteria_fc_academy'&&
    drillStatuses.some(v=>v===x.status)&&Number.isSafeInteger(x.version)&&(x.version as number)>=1&&(x.version as number)<=10000;
}
export function validDrillPage(x:unknown,status:string,offset:number):x is DrillPage {
  if(!rec(x)||Object.keys(x).length!==3||x.organization_id!=='lefteria_fc_academy'||
    !Array.isArray(x.drills)||x.drills.length>30||!Number.isSafeInteger(offset)||offset<0||offset>100000||
    !(x.next_offset===null||(x.drills.length===30&&x.next_offset===offset+30&&offset<=99970)))return false;
  const ids=new Set<string>();
  for(const row of x.drills){
    if(!validDrill(row)||(status!=='all'&&row.status!==status)||ids.has(row.id))return false;
    ids.add(row.id);
  }
  return true;
}
export function canReadDrillLibrary(role:string,organizations:readonly {id:string}[]):boolean {
  return ['super_admin','manager','coach'].includes(role)&&organizations.some(x=>x.id==='lefteria_fc_academy');
}
