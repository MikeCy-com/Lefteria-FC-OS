import type {MobileCalendar,MobileCalendarItem} from './calendar.ts';
import type {MobileRole} from './navigation.ts';

/**
 * Source of truth: only the authorized calendar response. No demo fixtures,
 * cached cross-role content, inferred membership or fabricated counts.
 */
/**
 * Matches the authenticated calendar service contract:
 * player => exactly one self-linked player and that player's organization;
 * coach => no selected player, organization null, scoped event organizations;
 * management => explicit selected organization and no player selection.
 * An unlinked player may receive a genuinely empty calendar.
 */
export function isTrustedRoleHomeContext(
  calendar:MobileCalendar|null,
  role:Extract<MobileRole,'player'|'coach'|'management'>,
  identityId:string,organizationId:string|null,today:string
):calendar is MobileCalendar {
  if(!calendar||!identityId||calendar.identity_id!==identityId||
    calendar.role!==role||calendar.month!==today.slice(0,7)||
    !Array.isArray(calendar.items)||!Array.isArray(calendar.players))return false;
  if(role==='player'){
    if(calendar.players.length===0)
      return calendar.selected_player_id===null&&calendar.organization_id===null&&calendar.items.length===0;
    const selected=calendar.players[0];
    return calendar.players.length===1&&!!selected&&
      calendar.selected_player_id===selected.id&&
      calendar.organization_id===selected.organization_id;
  }
  return calendar.selected_player_id===null&&calendar.players.length===0&&
    (role==='coach'?calendar.organization_id===null:
      !!organizationId&&calendar.organization_id===organizationId);
}

function trustedRoleHomeItems(
  calendar:MobileCalendar|null,
  role:Extract<MobileRole,'player'|'coach'|'management'>,
  identityId:string,
  organizationId:string|null,
  today:string
):MobileCalendarItem[] {
  if(!isTrustedRoleHomeContext(calendar,role,identityId,organizationId,today))return [];
  return calendar.items.filter(item=>
    (role==='coach'
      ? (item.organization_id==='lefteria_fc'||item.organization_id==='lefteria_fc_academy')
      :item.organization_id===calendar.organization_id)&&
    item.end_date>=today&&!item.is_cancelled&&
    item.date.slice(0,7)===calendar.month)
    .toSorted((a,b)=>a.date.localeCompare(b.date)||
      (a.start_time??'').localeCompare(b.start_time??'')||
      a.id.localeCompare(b.id));
}

export function trustedRoleHomeEvents(
  calendar:MobileCalendar|null,
  role:Extract<MobileRole,'player'|'coach'|'management'>,
  identityId:string,organizationId:string|null,today:string,limit=4
):MobileCalendarItem[] {
  return trustedRoleHomeItems(calendar,role,identityId,organizationId,today)
    .slice(0,Math.max(0,Math.min(limit,10)));
}

export function trustedNextRoleMatch(
  calendar:MobileCalendar|null,
  role:Extract<MobileRole,'player'|'coach'|'management'>,
  identityId:string,organizationId:string|null,today:string
):MobileCalendarItem|null {
  return trustedRoleHomeItems(calendar,role,identityId,organizationId,today)
    .find(item=>item.kind==='match')??null;
}

/** Cancelled upcoming events remain visible as alerts without re-enabling
 * attendance/RSVP. Never infer coach, management or player access locally:
 * the exact current role + identity + owner calendar must match. */
export function trustedRoleHomeCancelledEvents(
  calendar:MobileCalendar|null,
  role:Extract<MobileRole,'player'|'coach'|'management'>,
  identityId:string,organizationId:string|null,today:string,limit=3
):MobileCalendarItem[] {
  if(!isTrustedRoleHomeContext(calendar,role,identityId,organizationId,today))return [];
  return calendar.items.filter(item=>
    (role==='coach'
      ? (item.organization_id==='lefteria_fc'||item.organization_id==='lefteria_fc_academy')
      :item.organization_id===calendar.organization_id)&&
    item.end_date>=today&&item.is_cancelled===true&&
    item.date.slice(0,7)===calendar.month)
    .toSorted((a,b)=>a.date.localeCompare(b.date)||
      (a.start_time??'').localeCompare(b.start_time??'')||
      a.id.localeCompare(b.id))
    .slice(0,Math.max(0,Math.min(limit,5)));
}

export const ROLE_HOME_SHORTCUTS={
  player:['calendar','hub','chat','announcements','profile'],
  coach:['calendar','squad','chat','announcements','profile'],
  management:['calendar','teams','financials','chat','announcements'],
  fan:['matches','news','team','shop','hub']
} as const;
