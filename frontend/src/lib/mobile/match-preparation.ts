import type { MobileCalendarResponses } from './calendar.ts';

export type MatchAvailability = 'going' | 'maybe' | 'not_going' | 'unanswered';
export type MatchRosterFilter = 'all' | MatchAvailability;
type Player = MobileCalendarResponses['players'][number];

export function matchAvailability(status: Player['status']): MatchAvailability {
  if (status === 'going' || status === 'maybe' || status === 'not_going') return status;
  return 'unanswered';
}

export function matchRosterSummary(players: readonly Player[]) {
  const counts: Record<MatchAvailability, number> = { going: 0, maybe: 0, not_going: 0, unanswered: 0 };
  const unique = new Map(players.map(player => [player.id, player]));
  for (const player of unique.values()) {
    counts[matchAvailability(player.status)]++;
  }
  return { counts, total: unique.size };
}

export function filterMatchRoster(players: readonly Player[], filter: MatchRosterFilter, search: string): Player[] {
  const normalized = search.trim().toLocaleLowerCase('el-CY');
  const unique = [...new Map(players.map(player => [player.id, player])).values()];
  return unique.filter(player =>
    (filter === 'all' || matchAvailability(player.status) === filter) &&
    (!normalized || player.name.toLocaleLowerCase('el-CY').includes(normalized) ||
      player.position.toLocaleLowerCase('el-CY').includes(normalized) || String(player.number) === normalized)
  );
}
