// Kickoff instants come from the server's reviewed TimeZoneInfo conversion.
// Never reconstruct them from the viewer's local time zone or a guessed offset.
export const CLUB_TIME_ZONE = 'Asia/Nicosia';
export interface FixtureClockFields {
  id: string; status: string; match_date: string; match_time: string | null; kickoff_at: string | null;
}
const dayFormat = new Intl.DateTimeFormat('en-GB', { timeZone: CLUB_TIME_ZONE, year:'numeric',month:'2-digit',day:'2-digit' });
export function clubDay(now: number): string | null {
  if (!Number.isFinite(now)) return null;
  const parts = dayFormat.formatToParts(now);
  return ['year','month','day'].map(type => parts.find(part => part.type === type)?.value).join('-');
}
export function snapshotInstant(value: { generated_at: string; time_zone: string } | null | undefined): number | null {
  if (!value || value.time_zone !== CLUB_TIME_ZONE || !/(?:Z|[+-][0-9]{2}:[0-9]{2})$/.test(value.generated_at)) return null;
  const instant = Date.parse(value.generated_at);
  return Number.isFinite(instant) ? instant : null;
}
export function kickoffInstant(fixture: FixtureClockFields): number | null {
  if (!fixture.kickoff_at || !/(?:Z|[+-][0-9]{2}:[0-9]{2})$/.test(fixture.kickoff_at)) return null;
  const value = Date.parse(fixture.kickoff_at);
  return Number.isFinite(value) && clubDay(value) === fixture.match_date ? value : null;
}
export function nextPublicFixture<T extends FixtureClockFields>(fixtures: readonly T[], now: number | null): T | undefined {
  const today = now === null ? null : clubDay(now);
  if (!today || now === null) return undefined;
  const candidates = fixtures.filter(fixture => {
    if (fixture.status !== 'Scheduled' || !/^\d{4}-\d{2}-\d{2}$/.test(fixture.match_date)) return false;
    const kickoff = kickoffInstant(fixture);
    if (kickoff !== null) return kickoff > now;
    // A non-null unresolvable wall time is not a date-only fixture.
    return fixture.match_time === null && fixture.kickoff_at === null && fixture.match_date >= today;
  });
  const ordinal = (a: string,b: string) => a < b ? -1 : a > b ? 1 : 0;
  return candidates.sort((a,b) => ordinal(a.match_date,b.match_date) ||
    (kickoffInstant(a) ?? Infinity) - (kickoffInstant(b) ?? Infinity) || ordinal(a.id,b.id))[0];
}
export function fixtureCountdown(fixture: FixtureClockFields, now: number) {
  const kickoff = kickoffInstant(fixture);
  if (kickoff === null || !Number.isFinite(now) || kickoff <= now) return null;
  const seconds = Math.floor((kickoff - now) / 1000);
  return [Math.floor(seconds/86400),Math.floor(seconds/3600)%24,Math.floor(seconds/60)%60,seconds%60] as const;
}
