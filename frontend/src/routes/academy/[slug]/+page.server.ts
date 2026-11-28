import { error, redirect } from '@sveltejs/kit';
import { loadPublicSite } from '../../../lib/server/public-content';
import { requestSections } from '../../../lib/server/public-sections';
import { academyGroupLink } from '../../../lib/public/navigation';

export async function load({ url, params }: { url: URL; params: { slug: string } }) {
  const data = await loadPublicSite(url, 'lefteria_fc_academy');
  if (!data.available || !data.site) error(503, 'Το τμήμα της ακαδημίας δεν είναι προσωρινά διαθέσιμο.');
  const slug = params.slug;
  if (!slug || slug.length > 400) error(404, 'Το τμήμα δεν βρέθηκε.');
  const groups = data.site.academy_groups.filter(group => group.organization_id === 'lefteria_fc_academy');
  const exact = groups.find(group => group.id === slug);
  const separator = slug.indexOf('--');
  const group = exact ?? (separator > 0 ? groups.find(group => group.id === slug.slice(separator + 2)) : undefined);
  if (!group) error(404, 'Το τμήμα δεν βρέθηκε.');
  const canonical = academyGroupLink(group);
  if (decodeURIComponent(url.pathname) !== decodeURIComponent(canonical)) redirect(301,canonical);
  // Never serialize unrelated public sections to a group page. The API
  // projection already abbreviates youth identities; filter membership again
  // by this published Academy group before rendering them.
  const sections = await requestSections('lefteria_fc_academy');
  const groupPlayers = sections.value?.players.filter(player =>
    player.organization_id === 'lefteria_fc_academy' && player.team_type === 'Academy' &&
    player.academy_group_ids.includes(group.id)) ?? [];
  const groupGallery = sections.value?.gallery.filter(photo =>
    photo.organization_id === 'lefteria_fc_academy' && photo.academy_group_id === group.id) ?? [];
  const groupCoaches = sections.value?.staff.filter(coach =>
    coach.organization_id === 'lefteria_fc_academy' && coach.academy_group_ids.includes(group.id)) ?? [];
  const groupFixtures = data.site.fixtures
    .filter(fixture => fixture.organization_id === 'lefteria_fc_academy' && fixture.academy_group_id === group.id)
    .sort((left, right) => left.match_date.localeCompare(right.match_date) || (left.match_time ?? '').localeCompare(right.match_time ?? '') || left.id.localeCompare(right.id));
  return { ...data, group, groupFixtures, groupPlayers, groupGallery, groupCoaches, groupSectionsAvailable: sections.status === 200 };
}
