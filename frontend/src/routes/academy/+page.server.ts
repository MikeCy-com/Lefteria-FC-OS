import { loadSectionPage } from '../../lib/server/public-sections';
import { readWebsitePages } from '../../lib/server/website-pages';
import { canonicalAcademyLead } from '../../lib/public/canonical-academy';
import { CANONICAL_ACADEMY_INTRO_SLUG } from '../../lib/public/website-pages';

export async function load({url}: {url: URL}) {
  const site = await loadSectionPage(url, 'lefteria_fc_academy');
  const pages = await readWebsitePages('lefteria_fc_academy');
  return {...site, editorialLead: canonicalAcademyLead(pages, CANONICAL_ACADEMY_INTRO_SLUG)};
}
