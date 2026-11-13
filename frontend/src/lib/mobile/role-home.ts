import type {MobileCalendar,MobileCalendarItem} from './calendar.ts';
import type {MobileRole} from './navigation.ts';

/**
 * Source of truth: only the authorized calendar response. No demo fixtures,
 * cached cross-role content, inferred membership or fabricated counts.
 */
export function trustedRoleHomeEvents(
  calendar:MobileCalendar|null,
  role:Extract<MobileRole,'player'|'coach'|'management'>,
  identityId:string,
  organizationId:string|null,
  today:string,
  limit=4
):MobileCalendarItem[] {
  if (!calendar || !identityId || calendar.identity_id!==identityId ||
      calendar.role!==role || calendar.month!==today.slice(0,7) ||
      calendar.selected_player_id!==null ||
      (role==='management' && (!organizationId || calendar.organization_id!==organizationId))) return [];
  return calendar.items.filter(item=>
    item.organization_id===calendar.organization_id &&
    item.end_date>=today && !item.is_cancelled &&
    item.date.slice(0,7)===calendar.month)
    .toSorted((a,b)=>a.date.localeCompare(b.date)||
      (a.start_time??'').localeCompare(b.start_time??'')||
      a.id.localeCompare(b.id))
    .slice(0,Math.max(0,Math.min(limit,10)));
}

export const ROLE_HOME_SHORTCUTS={
  player:['calendar','hub','chat','announcements','profile'],
  coach:['calendar','squad','chat','announcements','profile'],
  management:['calendar','teams','financials','chat','announcements'],
  fan:['matches','news','team','shop','hub']
} as const;
