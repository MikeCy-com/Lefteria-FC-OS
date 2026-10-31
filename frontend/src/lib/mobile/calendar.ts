import type { components } from '../api/schema';

export type MobileCalendar = components['schemas']['MobileCalendarResult'];
export type MobileCalendarItem = components['schemas']['MobileCalendarItem'];
export type MobileCalendarResponses = components['schemas']['MobileCalendarResponseResult'];
export type AvailabilityStatus = 'going' | 'not_going' | 'maybe';
export type CalendarKind = 'event' | 'match' | 'training';
export type CalendarLoadStatus = 'idle' | 'loading' | 'ready' | 'error';

export const AVAILABILITY_LABELS = { going: 'Πάω', not_going: 'Δεν πάω', maybe: 'Ίσως' } as const;
export const CALENDAR_KIND_LABELS = { event: 'Εκδήλωση', match: 'Αγώνας', training: 'Προπόνηση' } as const;
export const WEEKDAY_LABELS = ['Δευ', 'Τρι', 'Τετ', 'Πέμ', 'Παρ', 'Σάβ', 'Κυρ'] as const;

export function calendarOrganizationLabel(id: string): string {
  return id === 'lefteria_fc' ? 'Λευτεριά FC' : id === 'lefteria_fc_academy' ? 'Ακαδημία' : id;
}

export function isCalendarKind(value: unknown): value is CalendarKind {
  return value === 'event' || value === 'match' || value === 'training';
}

export function isAvailabilityStatus(value: unknown): value is AvailabilityStatus {
  return value === 'going' || value === 'not_going' || value === 'maybe';
}

export function isCalendarMonth(value: unknown): value is string {
  return typeof value === 'string' && /^[0-9]{4}-(0[1-9]|1[0-2])$/.test(value) && Number(value.slice(0, 4)) > 0;
}

// Date-only values are calendar coordinates. Always format with UTC explicitly,
// so browser timezone never changes an imported club date.
export function calendarDate(value: string): Date {
  const [year, month, day] = value.split('-').map(Number);
  const date = new Date(0);
  date.setUTCFullYear(year, month - 1, day);
  date.setUTCHours(12, 0, 0, 0);
  return date;
}

export function isCalendarDate(value: unknown): value is string {
  if (typeof value !== 'string' || !/^[0-9]{4}-[0-9]{2}-[0-9]{2}$/.test(value)) return false;
  const date = calendarDate(value);
  return !Number.isNaN(date.valueOf()) && date.toISOString().slice(0, 10) === value;
}

export function clubToday(timeZone = 'Asia/Nicosia', now = Date.now()): string {
  const parts = new Intl.DateTimeFormat('en-GB', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(now);
  return ['year', 'month', 'day'].map((type) => parts.find((part) => part.type === type)?.value).join('-');
}

export function formatCalendarDate(value: string, options: Intl.DateTimeFormatOptions = { weekday: 'long', day: 'numeric', month: 'long' }): string {
  return new Intl.DateTimeFormat('el-GR', { ...options, timeZone: 'UTC' }).format(calendarDate(value));
}

export function shiftCalendarMonth(month: string, offset: number): string {
  const date = calendarDate(`${month}-01`);
  date.setUTCMonth(date.getUTCMonth() + offset);
  return date.toISOString().slice(0, 7);
}

export function calendarMonthDays(month: string): (string | null)[] {
  const first = calendarDate(`${month}-01`);
  const blanks = (first.getUTCDay() + 6) % 7;
  const last = new Date(first);
  last.setUTCMonth(first.getUTCMonth() + 1, 0);
  const days = last.getUTCDate();
  return [...Array.from({ length: blanks }, () => null), ...Array.from({ length: days }, (_, index) => `${month}-${String(index + 1).padStart(2, '0')}`)];
}

export function calendarWeekDays(selected: string): string[] {
  const first = calendarDate(selected);
  first.setUTCDate(first.getUTCDate() - ((first.getUTCDay() + 6) % 7));
  return Array.from({ length: 7 }, (_, index) => {
    const date = new Date(first);
    date.setUTCDate(first.getUTCDate() + index);
    return date.toISOString().slice(0, 10);
  });
}

export function calendarItemKey(item: Pick<MobileCalendarItem, 'id' | 'kind'>): string {
  return `${item.kind}:${item.id}`;
}

export function calendarItemOnDate(item: MobileCalendarItem, date: string): boolean {
  return item.date <= date && item.end_date >= date;
}

export function safeCalendarLink(value: string | null | undefined): string | null {
  if (!value) return null;
  try { const url = new URL(value); return url.protocol === 'https:' || url.protocol === 'http:' ? url.href : null; }
  catch { return null; }
}
