import type {PublishedHubArticle} from './published-education.ts';
type Org=PublishedHubArticle['organization_id'];
type Role='parent'|'player';
export interface PersonalLessonSchedule {
 organization_id:Org;audience:Role;
 started_at:string;next_due_at:string;cadence_days:number;
}
export interface PersonalLessonDelivery{
 id:string;queued_at:string;type:'lesson_ready';audience:Role;content_id:string;
}
export interface PersonalLessonInbox{
 organization_id:Org;audience:Role;items:PersonalLessonDelivery[];
}
const uuid=(value:unknown):value is string=>typeof value==='string'&&
 /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(value);
const iso=(value:unknown):value is string=>typeof value==='string'&&Number.isFinite(Date.parse(value));
const object=(value:unknown):value is Record<string,unknown>=>!!value&&
 typeof value==='object'&&!Array.isArray(value);
export function validPersonalLessonSchedule(value:unknown,owner:Org,role:Role):
 value is PersonalLessonSchedule{
 if(!object(value)||value.organization_id!==owner||value.audience!==role||
   !iso(value.started_at)||!iso(value.next_due_at)||value.cadence_days!==7)
   return false;
 return Date.parse(value.started_at)<=Date.now()&&
   Date.parse(value.next_due_at)>=Date.parse(value.started_at);
}
export function validPersonalLessonInbox(value:unknown,owner:Org,role:Role):
 value is PersonalLessonInbox{
 if(!object(value)||value.organization_id!==owner||value.audience!==role||
    !Array.isArray(value.items)||value.items.length>10)return false;
 const seen=new Set<string>();
 return value.items.every((raw:unknown)=>{
   if(!object(raw)||!uuid(raw.id)||seen.has(raw.id)||
     raw.type!=='lesson_ready'||raw.audience!==role||
     !uuid(raw.content_id)||!iso(raw.queued_at)||
     Date.parse(raw.queued_at)>Date.now())return false;
   seen.add(raw.id);return true;
 });
}
