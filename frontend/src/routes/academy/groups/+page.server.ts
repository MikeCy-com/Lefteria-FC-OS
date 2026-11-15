import { error } from '@sveltejs/kit';
import { loadSectionPage } from '../../../lib/server/public-sections';

// The public site owns publication visibility; the Academy projection is only
// used to enrich cards whose exact group IDs are already published.
export async function load({ url }: { url: URL }) {
  const data = await loadSectionPage(url, 'lefteria_fc_academy');
  if (!data.available || !data.site)
    error(503, 'Τα τμήματα της ακαδημίας δεν είναι προσωρινά διαθέσιμα.');
  return data;
}
