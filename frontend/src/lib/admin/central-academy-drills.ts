export const drillCategories=['technical','tactical','physical','goalkeeping','warmup','game','recovery','other'] as const;
export const drillAges=['all','u6-u8','u9-u11','u12-u14','u15-u18','adult'] as const;
export const drillDifficulties=['beginner','intermediate','advanced'] as const;
export type DrillDifficulty=typeof drillDifficulties[number];
export const drillStatuses=['draft','approved','archived'] as const;
export type DrillCategory=typeof drillCategories[number];
export type DrillAge=typeof drillAges[number];
export type DrillStatus=typeof drillStatuses[number];
export interface DrillForm {
  title:string;category:DrillCategory;age_band:DrillAge;duration_minutes:number;objective:string;instructions:string;
}
export interface Drill extends DrillForm {
  id:string;organization_id:'lefteria_fc_academy';status:DrillStatus;version:number;difficulty?:DrillDifficulty;variant_of?:string;variant_base_version?:number;
  proposal_kind?:'revision'|'variant';base_drill_id?:string;base_version?:number;applied_version?:number|null;
}
export type DrillPage={organization_id:'lefteria_fc_academy';drills:Drill[];next_offset:number|null};
const rec=(x:unknown):x is Record<string,unknown>=>x!==null&&typeof x==='object'&&!Array.isArray(x);
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const text=(x:unknown,max:number,multiline=false)=>typeof x==='string'&&x.length>=1&&x.length<=max&&x===x.trim()&&!/[\x00-\x1f\x7f-\x9f]/.test(multiline?x.replaceAll('\n',''):x);
export function validDrillForm(x:unknown):x is DrillForm {
  return rec(x)&&text(x.title,180)&&drillCategories.some(v=>v===x.category)&&drillAges.some(v=>v===x.age_band)&&
    Number.isSafeInteger(x.duration_minutes)&&(x.duration_minutes as number)>=1&&(x.duration_minutes as number)<=120&&
    text(x.objective,1000)&&text(x.instructions,4000,true);
}
export function validDrill(x:unknown,allowProposal=false):x is Drill {
  if(!rec(x)||!validDrillForm(x))return false;
  const value=x as Record<string,unknown>;
  const allowed=['id','organization_id','title','category','age_band','duration_minutes','objective','instructions','status','version'];
  const version=(v:unknown)=>Number.isSafeInteger(v)&&(v as number)>=1&&(v as number)<=10000;
  const id=(v:unknown)=>typeof v==='string'&&uuid.test(v);
  if(Object.hasOwn(value,'difficulty')){
    if(!drillDifficulties.some(v=>v===value.difficulty))return false;
    allowed.push('difficulty');
  }
  if(Object.hasOwn(value,'variant_of')||Object.hasOwn(value,'variant_base_version')){
    if(!id(value.variant_of)||value.variant_of===value.id||!version(value.variant_base_version)||!value.difficulty)return false;
    allowed.push('variant_of','variant_base_version');
  }
  if(Object.hasOwn(value,'proposal_kind')){
    if(!allowProposal||!['revision','variant'].includes(String(value.proposal_kind))||!id(value.base_drill_id)||
      value.base_drill_id===value.id||!version(value.base_version)||!value.difficulty||
      !(value.applied_version===null||version(value.applied_version))||
      (value.proposal_kind==='variant'&&(value.variant_of!==value.base_drill_id||value.variant_base_version!==value.base_version||value.applied_version!==null))||
      (value.proposal_kind==='revision'&&(value.variant_of!==undefined||
        (value.applied_version!==null&&(value.status!=='archived'||value.applied_version!==(value.base_version as number)+1)))))return false;
    allowed.push('proposal_kind','base_drill_id','base_version','applied_version');
  }
  return Object.keys(value).length===allowed.length&&Object.keys(value).every(k=>allowed.includes(k))&&
    id(value.id)&&value.organization_id==='lefteria_fc_academy'&&drillStatuses.some(v=>v===value.status)&&version(value.version);
}
export function validDrillPage(x:unknown,status:string,offset:number,allowProposal=false):x is DrillPage {
  if(!rec(x)||Object.keys(x).length!==3||x.organization_id!=='lefteria_fc_academy'||
    !Array.isArray(x.drills)||x.drills.length>30||!Number.isSafeInteger(offset)||offset<0||offset>100000||
    !(x.next_offset===null||(x.drills.length===30&&x.next_offset===offset+30&&offset<=99970)))return false;
  const ids=new Set<string>();
  for(const row of x.drills){
    if(!validDrill(row,allowProposal)||(status!=='all'&&row.status!==status)||ids.has(row.id))return false;
    ids.add(row.id);
  }
  return true;
}
export function canReadDrillLibrary(role:string,organizations:readonly {id:string}[]):boolean {
  return ['super_admin','manager','coach'].includes(role)&&organizations.some(x=>x.id==='lefteria_fc_academy');
}
