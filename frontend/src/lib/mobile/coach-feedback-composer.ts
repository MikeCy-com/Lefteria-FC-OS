/** Strict response validators for coach-authored, explicitly released private feedback. */
export type FeedbackOrganization='lefteria_fc'|'lefteria_fc_academy';
export interface CoachFeedbackTarget {
  id:string;
  organization_id:FeedbackOrganization;
  name:string;
}
export interface CoachFeedbackTargets {items:CoachFeedbackTarget[];}
export interface CoachFeedbackDraft {
  id:string;organization_id:FeedbackOrganization;player_id:string;status:'draft';
  version:1;language:'el'|'en';requires_coach_approval:true;
}
export interface CoachFeedbackApproved {
  id:string;organization_id:FeedbackOrganization;player_id:string;
  status:'approved';version:2;authorized_by_coach:true;delivered:false;
}
export interface CoachFeedbackSent {
  id:string;organization_id:FeedbackOrganization;status:'sent';version:3;
  inbox_available:true;read:false;
}
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const playerId=/^[A-Za-z0-9_-]{1,128}$/;
const invalidControl=/[\x00-\x1f\x7f]/;
const record=(value:unknown):value is Record<string,unknown>=>
  typeof value==='object'&&value!==null&&!Array.isArray(value);
const owner=(value:unknown):value is FeedbackOrganization=>
  value==='lefteria_fc'||value==='lefteria_fc_academy';
export function validCoachFeedbackTargets(value:unknown):value is CoachFeedbackTargets {
  if(!record(value)||!Array.isArray(value.items)||value.items.length>150)return false;
  const ids=new Set<string>();
  return value.items.every((item:unknown)=>{
    if(!record(item)||typeof item.id!=='string'||!playerId.test(item.id)||
      !owner(item.organization_id)||typeof item.name!=='string'||
      item.name.length<1||item.name.length>200||item.name!==item.name.trim()||
      invalidControl.test(item.name))return false;
    const key=item.organization_id+'/'+item.id;
    if(ids.has(key))return false;
    ids.add(key);return true;
  });
}
export function validCoachDraft(value:unknown,target:CoachFeedbackTarget,language:'el'|'en'):
  value is CoachFeedbackDraft {
  return record(value)&&typeof value.id==='string'&&uuid.test(value.id)&&
    value.organization_id===target.organization_id&&value.player_id===target.id&&
    value.status==='draft'&&value.version===1&&
    value.language===language&&value.requires_coach_approval===true;
}
export function validCoachApproval(value:unknown,draft:CoachFeedbackDraft):
  value is CoachFeedbackApproved {
  return record(value)&&value.id===draft.id&&
    value.organization_id===draft.organization_id&&
    value.player_id===draft.player_id&&value.status==='approved'&&
    value.version===2&&value.authorized_by_coach===true&&value.delivered===false;
}
export function validCoachSend(value:unknown,draft:CoachFeedbackApproved):
  value is CoachFeedbackSent {
  return record(value)&&value.id===draft.id&&
    value.organization_id===draft.organization_id&&
    value.status==='sent'&&value.version===3&&
    value.inbox_available===true&&value.read===false;
}
export function validCoachText(value:string,min:number,max:number):boolean {
  return value.length>=min&&value.length<=max&&value===value.trim()&&
    !invalidControl.test(value);
}
