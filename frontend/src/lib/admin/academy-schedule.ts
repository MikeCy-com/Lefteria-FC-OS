export type AdminRosterGroup={id:string;name:string;kind:'academy_group';active_players:number};
export type AcademyTrainingSession={id:string;day:string;title:string;is_cancelled:boolean;is_archived:boolean};
export type AcademyTrainingList={
  organization_id:'lefteria_fc_academy';group_id:string;from:string;to:string;
  total:number;sessions:AcademyTrainingSession[]
};
const idOk=(x:unknown):x is string=>typeof x==='string'&&x.length>0&&x.length<=128&&!/[\x00-\x1f\x7f]/.test(x);
const dateOk=(x:unknown):x is string=>typeof x==='string'&&/^\d{4}-\d{2}-\d{2}$/.test(x)&&
  !Number.isNaN(Date.parse(x))&&new Date(x).toISOString().slice(0,10)===x;
export function isAcademyScheduleGroupList(x:unknown):x is {organization_id:'lefteria_fc_academy';ready:true;groups:AdminRosterGroup[]} {
  if(!x||typeof x!=='object'||Array.isArray(x))return false;
  const v=x as Record<string,unknown>;
  if(v.organization_id!=='lefteria_fc_academy'||v.ready!==true||
    !Array.isArray(v.groups)||v.groups.length>150)return false;
  const seen=new Set<string>();
  return v.groups.every((entry:unknown)=>{
    if(!entry||typeof entry!=='object'||Array.isArray(entry))return false;
    const g=entry as Record<string,unknown>;
    if(!idOk(g.id)||seen.has(g.id)||typeof g.name!=='string'||g.name.length<1||
      g.name.length>200||g.kind!=='academy_group'||!Number.isSafeInteger(g.active_players)||
      (g.active_players as number)<0)return false;
    seen.add(g.id);return true;
  });
}
export function isAcademyTrainingList(value:unknown,group:string,from:string,to:string):value is AcademyTrainingList{
  if(!value||typeof value!=='object'||Array.isArray(value))return false;
  const x=value as Record<string,unknown>;
  if(x.organization_id!=='lefteria_fc_academy'||x.group_id!==group||
    x.from!==from||x.to!==to||!Number.isSafeInteger(x.total)||
    (x.total as number)<0||(x.total as number)>500||!Array.isArray(x.sessions)||
    x.sessions.length>500||x.sessions.length>(x.total as number))return false;
  const seen=new Set<string>();
  return x.sessions.every((entry:unknown)=>{
    if(!entry||typeof entry!=='object'||Array.isArray(entry))return false;
    const row=entry as Record<string,unknown>;
    if(!idOk(row.id)||seen.has(row.id)||!dateOk(row.day)||row.day<from||row.day>to||
      typeof row.title!=='string'||row.title.length<1||row.title.length>250||
      typeof row.is_cancelled!=='boolean'||typeof row.is_archived!=='boolean')return false;
    seen.add(row.id);return true;
  });
}
export function upcomingAcademySessions(list:AcademyTrainingList,today:string):AcademyTrainingSession[]{
  return list.sessions.filter(s=>s.day>=today&&!s.is_archived&&!s.is_cancelled)
    .toSorted((a,b)=>a.day.localeCompare(b.day)||a.id.localeCompare(b.id));
}
export function academyMonthWindow(month:string):{start:string;end:string}|null{
  if(!/^\d{4}-(0[1-9]|1[0-2])$/.test(month))return null;
  const first=new Date(month+'-01T12:00:00Z');
  if(Number.isNaN(first.valueOf()))return null;
  const last=new Date(first);
  last.setUTCMonth(last.getUTCMonth()+1,0);
  return {start:month+'-01',end:last.toISOString().slice(0,10)};
}
