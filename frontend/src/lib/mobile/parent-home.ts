import type { components } from '../../api/schema';
import { clubToday } from '../calendar.ts';

export type Family = components['schemas']['ParentFamilyResult'];
export type ParentCalendar = components['schemas']['MobileCalendarResult'];
export type ParentCalendarItem = components['schemas']['MobileCalendarItem'];
export type LinkedChild = Family['children'][number];

// The calendar response is child-scoped, not a family-wide feed. A cached
// calendar for a sibling, another organization, identity, or month must never
// appear as if it belongs to the first child on the Parent Home dashboard.
export function parentHomeSchedule(
  family: Family | null,
  calendar: ParentCalendar | null,
  identityId: string,
  today = clubToday()
): ParentCalendarItem[] {
  const child = family?.children[0];
  if (!child || !calendar || calendar.role !== 'parent' ||
      calendar.identity_id !== identityId ||
      calendar.selected_player_id !== child.id ||
      calendar.organization_id !== child.organization_id ||
      calendar.month !== today.slice(0, 7)) return [];

  return calendar.items
    .filter((item) => item.organization_id === child.organization_id &&
      item.end_date >= today && item.date.slice(0, 7) === calendar.month &&
      !item.is_cancelled)
    .toSorted((a, b) => a.date.localeCompare(b.date) ||
      (a.start_time ?? '').localeCompare(b.start_time ?? '') ||
      a.title.localeCompare(b.title, 'el-GR'))
    .slice(0, 4);
}

export function matchingParentHomeCalendar(
  family: Family | null, calendar: ParentCalendar | null,
  identityId: string, today = clubToday()
): boolean {
  const child = family?.children[0];
  return !!child && !!calendar &&
    calendar.role === 'parent' && calendar.identity_id === identityId &&
    calendar.selected_player_id === child.id &&
    calendar.organization_id === child.organization_id &&
    calendar.month === today.slice(0, 7);
}

export function parentHomeCalendarMonth(today = clubToday()): string {
  return today.slice(0, 7);
}
