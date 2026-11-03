import { error } from '@sveltejs/kit';
import { loadPublicSite } from './public-content';
import type { PublicFixture, PublicPageData } from '../public/types';

export type PublicMatchData = PublicPageData & { fixture: PublicFixture; kickoffAt: string | null };

export async function loadPublicMatch(url: URL, id: string): Promise<PublicMatchData> {
  if (!id || id.length > 400 || /[\u0000-\u001f\u007f]/.test(id)) error(404, 'Ο αγώνας δεν βρέθηκε.');
  // The original URL has no organization segment. Resolve only the two explicit
  // public projections; caller cookies and organization selectors never choose it.
  const scopes = await Promise.all([
    loadPublicSite(url, 'lefteria_fc'), loadPublicSite(url, 'lefteria_fc_academy')
  ]);
  let matches = scopes.flatMap(scope => scope.available ? (scope.site?.fixtures ?? [])
    .filter(fixture => fixture.id === id).map(fixture => ({ scope, fixture })) : []);
  if (!matches.length) {
    // Exact legacy IDs win, including IDs containing the slug separator.
    const separator = id.indexOf('--');
    if (separator > 0) {
      const suffix = id.slice(separator + 2);
      matches = scopes.flatMap(scope => scope.available ? (scope.site?.fixtures ?? [])
        .filter(fixture => fixture.id === suffix).map(fixture => ({ scope, fixture })) : []);
    }
  }
  if (matches.length > 1) error(503, 'Ο αγώνας δεν είναι προσωρινά διαθέσιμος.');
  const match = matches[0];
  if (!match) {
    if (scopes.some(scope => !scope.available)) error(503, 'Ο αγώνας δεν είναι προσωρινά διαθέσιμος.');
    error(404, 'Ο αγώνας δεν βρέθηκε.');
  }
  return { ...match.scope, fixture: match.fixture,
    kickoffAt: match.scope.site?.fixture_clock?.find(row => row.id === match.fixture.id)?.kickoff_at ?? null };
}
