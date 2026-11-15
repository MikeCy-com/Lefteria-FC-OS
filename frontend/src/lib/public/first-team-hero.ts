import type { PublicTeam } from './types';

/** The First Team slide must never inherit an unrelated Club or Academy banner.
 * Unknown or ambiguous team levels deliberately fall back to the published
 * Club stadium artwork in HomeHero, rather than guessing an identity.
 */
export function firstTeamHeroBanner(teams: readonly PublicTeam[] | null | undefined): string | null {
  const verified = teams?.find(team =>
    team.organization_id === 'lefteria_fc' &&
    /^(first team|first_team|a.? team|α.? ομάδα|πρώτη ομάδα)$/i.test(team.level?.trim() ?? '')
  );
  return verified?.banner_url || null;
}
