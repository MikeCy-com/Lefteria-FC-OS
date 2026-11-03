import { error } from '@sveltejs/kit';
import { loadPublicSite } from '../../../lib/server/public-content';

export async function load({ url }: { url: URL }) {
  const data = await loadPublicSite(url, 'lefteria_fc_academy');
  if (!data.available || !data.site) error(503, 'Τα τμήματα της ακαδημίας δεν είναι προσωρινά διαθέσιμα.');
  return data;
}
