import { error } from '@sveltejs/kit';
import { loadSectionPage } from '../../../lib/server/public-sections';
import { readWebsitePages } from '../../../lib/server/website-pages';
import { canonicalAcademyLead } from '../../../lib/public/canonical-academy';
import { CANONICAL_ACADEMY_GROUPS_LEAD_SLUG } from '../../../lib/public/website-pages';

// The public site owns publication visibility; the Academy projection is only
// used to enrich cards whose exact group IDs are already published.
export async function load({ url }: { url: URL }) {
  const data = await loadSectionPage(url, 'lefteria_fc_academy');
  if (!data.available || !data.site)
    error(503, 'Τα τμήματα της ακαδημίας δεν είναι προσωρινά διαθέσιμα.');
  const pages = await readWebsitePages('lefteria_fc_academy');
  return {...data, editorialLead:canonicalAcademyLead(pages,CANONICAL_ACADEMY_GROUPS_LEAD_SLUG)};
}
