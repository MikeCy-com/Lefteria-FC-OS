import { error } from '@sveltejs/kit';
import { loadPublicSite } from '../../../lib/server/public-content';

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
  const groupFixtures = data.site.fixtures
    .filter(fixture => fixture.organization_id === 'lefteria_fc_academy' && fixture.academy_group_id === group.id)
    .sort((left, right) => left.match_date.localeCompare(right.match_date) || (left.match_time ?? '').localeCompare(right.match_time ?? '') || left.id.localeCompare(right.id));
  return { ...data, group, groupFixtures };
}
