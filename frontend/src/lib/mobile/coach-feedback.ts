/** Strictly validated, role-scoped coach feedback inbox DTOs. */
export interface PlayerCoachFeedbackItem {
  id: string;
  organization_id: 'lefteria_fc'|'lefteria_fc_academy';
  coach_name: string;
  message: string;
  sent_at: string;
}
export interface PlayerCoachFeedbackPage {
  items: PlayerCoachFeedbackItem[];
}
const uuid=/^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i;
const timestamp=/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/;
function isRecord(value:unknown):value is Record<string,unknown>{
  return typeof value==='object'&&value!==null&&!Array.isArray(value);
}
export function validPlayerCoachFeedbackPage(value:unknown):value is PlayerCoachFeedbackPage {
  if(!isRecord(value)||!Array.isArray(value.items)||value.items.length>20)return false;
  const ids=new Set<string>();
  return value.items.every((item:unknown)=>{
    if(!isRecord(item)||typeof item.id!=='string'||!uuid.test(item.id)||
       ids.has(item.id))return false;
    ids.add(item.id);
    if(item.organization_id!=='lefteria_fc'&&item.organization_id!=='lefteria_fc_academy')
      return false;
    if(typeof item.coach_name!=='string'||item.coach_name.length<1||
       item.coach_name.length>200||item.coach_name.trim()!==item.coach_name||
       /[\x00-\x1f\x7f]/.test(item.coach_name))return false;
    if(typeof item.message!=='string'||item.message.length<30||
       item.message.length>1600||item.message.trim()!==item.message||
       /[\x00-\x1f\x7f]/.test(item.message))return false;
    if(typeof item.sent_at!=='string'||!timestamp.test(item.sent_at)||
       !Number.isFinite(Date.parse(item.sent_at)))return false;
    return true;
  });
}
