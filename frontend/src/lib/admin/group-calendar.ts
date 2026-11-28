import type {AcademyTrainingSession} from './academy-schedule.ts';
import type {TrainingPlanDraft,TrainingPublicationRecord} from './academy-training-plans.ts';

export type CalendarOwner='lefteria_fc'|'lefteria_fc_academy';
export type CalendarRow={id:string;day:string;time:string|null;title:string;location:string;
  type:'training'|'match';state:'draft'|'published'|'cancelled'|'completed'|'scheduled'|'recorded';};
const rec=(v:unknown):v is Record<string,unknown>=>!!v&&typeof v==='object'&&!Array.isArray(v);
const safe=(v:unknown,max:number):v is string=>typeof v==='string'&&v.length<=max&&!/[\x00-\x1f\x7f]/.test(v);
const day=(v:unknown):v is string=>typeof v==='string'&&/^\d{4}-\d{2}-\d{2}$/.test(v)&&Number.isFinite(Date.parse(v))&&new Date(v).toISOString().slice(0,10)===v;
const time=(v:unknown):string|null=>typeof v==='string'&&/^([01]\d|2[0-3]):[0-5]\d$/.test(v)?v:null;
export function calendarTraining(sessions:AcademyTrainingSession[],plans:TrainingPlanDraft[],publications:TrainingPublicationRecord[]):CalendarRow[]{
  const known=new Map(publications.map(p=>[p.id,p]));
  const delivered=new Set(publications.map(p=>p.event_id));
  return [
    ...sessions.filter(s=>!s.is_archived&&!delivered.has(s.id)).map((s):CalendarRow=>({
      id:'session:'+s.id,day:s.day,time:null,title:s.title,location:'',type:'training',state:s.is_cancelled?'cancelled':'recorded'
    })),
    ...plans.filter(p=>p.status==='draft'||known.has(p.id)).map((p):CalendarRow=>{
      const publication=known.get(p.id);
      return {id:'plan:'+p.id,day:p.day,time:p.start_time,title:p.title,location:p.location,
        type:'training',state:publication?(publication.is_cancelled?'cancelled':'published'):'draft'};
    })
  ];
}
export function calendarFixtures(value:unknown,owner:CalendarOwner,group:string,from:string,to:string):CalendarRow[]|null{
  if(!rec(value)||value.organization_id!==owner||!Array.isArray(value.fixtures)||value.fixtures.length>50)return null;
  const rows:CalendarRow[]=[];const seen=new Set<string>();
  for(const f of value.fixtures){
    if(!rec(f)||f.organization_id!==owner||!safe(f.id,128)||!f.id||seen.has(f.id)||
      !day(f.match_date)||!safe(f.home_team,250)||!safe(f.away_team,250)||!safe(f.status,40))return null;
    seen.add(f.id);
    if((owner==='lefteria_fc'?f.team_id:f.academy_group_id)!==group||f.match_date<from||f.match_date>to)continue;
    rows.push({id:'fixture:'+f.id,day:f.match_date,time:time(f.match_time),title:f.home_team+' – '+f.away_team,
      location:safe(f.venue,250)?f.venue:'',type:'match',
      state:f.status==='Completed'?'completed':['Cancelled','Canceled'].includes(f.status)?'cancelled':'published'});
  }
  return rows;
}
export function calendarMatchDrafts(value:unknown,owner:CalendarOwner,group:string,from:string,to:string):CalendarRow[]|null{
  if(!rec(value)||value.organization_id!==owner||!Array.isArray(value.match_drafts)||!Array.isArray(value.scopes)||
     !Array.isArray(value.groups)||!Array.isArray(value.opponents)||!Array.isArray(value.playing_fields)||
     [value.match_drafts,value.scopes,value.groups,value.opponents,value.playing_fields].some(a=>a.length>3000))return null;
  const assignment=value.groups.find(g=>rec(g)&&g.id===group);
  const scope=value.scopes.find(s=>rec(s)&&s.organization_id===owner&&
    (owner==='lefteria_fc'?s.team_id===group:rec(assignment)&&s.id===assignment.scope_id));
  if(!rec(scope)||!safe(scope.id,128))return [];
  const name=(items:unknown[],id:unknown)=>{const row=items.find(r=>rec(r)&&r.id===id&&r.scope_id===scope.id&&r.organization_id===owner);
    return rec(row)&&safe(row.name,250)?row.name:'';};
  const rows:CalendarRow[]=[];const seen=new Set<string>();
  for(const d of value.match_drafts){
    if(!rec(d)||d.organization_id!==owner||!safe(d.id,128)||!d.id||seen.has(d.id)||d.status!=='draft'||!day(d.match_date))return null;
    seen.add(d.id);
    if(d.scope_id!==scope.id||(owner==='lefteria_fc'?d.club_team_id:d.academy_group_id)!==group||
      d.match_date<from||d.match_date>to)continue;
    rows.push({id:'draft:'+d.id,day:d.match_date,time:time(d.kickoff_local),
      title:name(value.opponents,d.opponent_id)||'Αγώνας · ιδιωτικό πρόχειρο',
      location:name(value.playing_fields,d.playing_field_id),type:'match',state:'draft'});
  }
  return rows;
}
export function sortCalendar(rows:CalendarRow[]):CalendarRow[]{
  return rows.slice().sort((a,b)=>a.day.localeCompare(b.day)||(a.time??'99:99').localeCompare(b.time??'99:99')||a.id.localeCompare(b.id));
}
