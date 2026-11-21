/** Strict player-bound, read-only mobile coach goal summary. */
export type CoachGoalOwner='lefteria_fc'|'lefteria_fc_academy';
export interface CoachPlayerGoal {
  id:string;title:string;
  category:'general'|'physical'|'technical'|'tactical'|'mental'|'social';
  progress:number;status:'active'|'completed';target_date:string|null;
}
export interface CoachPlayerGoalPage {
  organization_id:CoachGoalOwner;player_id:string;total:number;goals:CoachPlayerGoal[];
}
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const playerId=/^[A-Za-z0-9_-]{1,128}$/;
const isoDay=/^\d{4}-\d{2}-\d{2}$/;
const record=(x:unknown):x is Record<string,unknown>=>
  x!==null&&typeof x==='object'&&!Array.isArray(x);
export function validCoachGoalPage(raw:unknown,owner:CoachGoalOwner,player:string):
  raw is CoachPlayerGoalPage {
  if(!record(raw)||raw.organization_id!==owner||raw.player_id!==player||
    !playerId.test(player)||!Array.isArray(raw.goals)||raw.goals.length>50||
    !Number.isSafeInteger(raw.total)||raw.total!==raw.goals.length)return false;
  const seen=new Set<string>();
  return raw.goals.every((item:unknown)=>{
    if(!record(item)||typeof item.id!=='string'||!uuid.test(item.id)||
      seen.has(item.id)||typeof item.title!=='string'||
      item.title.length<1||item.title.length>200||
      item.title.trim()!==item.title||/[\x00-\x1f\x7f]/.test(item.title)||
      !['general','physical','technical','tactical','mental','social'].includes(String(item.category))||
      !['active','completed'].includes(String(item.status))||
      typeof item.progress!=='number'||!Number.isInteger(item.progress)||
      item.progress<0||item.progress>100||item.progress%10!==0||
      !(item.target_date===null||
        typeof item.target_date==='string'&&isoDay.test(item.target_date)&&
        Number.isFinite(Date.parse(item.target_date+'T00:00:00Z'))))return false;
    seen.add(item.id);return true;
  });
}
