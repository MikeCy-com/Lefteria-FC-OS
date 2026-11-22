import { loadPublicSite } from '../../../lib/server/public-content';
import { readWebsitePages } from '../../../lib/server/website-pages';
import { canonicalAcademyLead } from '../../../lib/public/canonical-academy';
import { CANONICAL_ACADEMY_PHILOSOPHY_LEAD_SLUG } from '../../../lib/public/website-pages';

export async function load({url}: {url: URL}) {
  const site = await loadPublicSite(url, 'lefteria_fc_academy');
  const pages = await readWebsitePages('lefteria_fc_academy');
  return {...site, editorialLead: canonicalAcademyLead(pages, CANONICAL_ACADEMY_PHILOSOPHY_LEAD_SLUG)};
}
