import type { PublicSections, SectionPlayer } from './sections-types';
import type { PublicFixture } from './types';

type Gallery = PublicSections['gallery'][number];
type Venue = PublicSections['venues'][number];

// No guessed cross-team ownership: a Club photo must name the selected team,
// one of its actually published player IDs, or explicitly say "First Team"
// while the verified First Team is selected. A generic/unassigned photo is
// never silently included in every Club team's gallery.
export function selectTeamGallery(
  source: readonly Gallery[],
  players: readonly SectionPlayer[],
  selectedTeamId: string | null,
  isSelectedFirstTeam: boolean
): Gallery[] {
  if (!selectedTeamId) return [];
  const playerIds = new Set(players.filter(player =>
    player.organization_id === 'lefteria_fc' && player.team_id === selectedTeamId
  ).map(player => player.id));
  return source.filter(photo => {
    if (photo.organization_id !== 'lefteria_fc' || photo.academy_group_id) return false;
    if (photo.team_id) return photo.team_id === selectedTeamId;
    if (photo.player_id) return playerIds.has(photo.player_id);
    // Source-labelled FC First Team photos had no team_id in old imports.
    // Preserve those only for the verified First Team (never Women's teams).
    return isSelectedFirstTeam && /^(first[ _-]?team|πρωτη[ _-]?ομαδα)$/i
      .test((photo.category ?? '').trim().normalize('NFD').replace(/[\u0300-\u036f]/g,''));
  });
}

const normalizeVenue = (value: string) => value.normalize('NFD')
  .replace(/[\u0300-\u036f]/g,'').toLocaleLowerCase('el-GR').trim();

// Venues have no team_id in the imported public DTO. Bind them only to
// recorded fixture venue names for the selected Club team. The verified
// First Team may also use the explicitly marked Club home ground.
export function selectTeamVenues(
  source: readonly Venue[],
  selectedFixtures: readonly PublicFixture[],
  selectedTeamId: string | null,
  isSelectedFirstTeam: boolean
): Venue[] {
  if (!selectedTeamId) return [];
  const names = new Set(selectedFixtures.filter(fixture =>
    fixture.organization_id === 'lefteria_fc' &&
    fixture.team_id === selectedTeamId
  ).map(fixture => normalizeVenue(fixture.venue ?? ''))
    .filter(Boolean));
  return source.filter(venue =>
    venue.organization_id === 'lefteria_fc' &&
    (names.has(normalizeVenue(venue.name)) ||
      (isSelectedFirstTeam && venue.is_home_ground))
  );
}
