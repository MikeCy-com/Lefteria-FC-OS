import { loadPublicSite } from '../../lib/server/public-content';
import { readWebsitePages } from '../../lib/server/website-pages';
import { canonicalContactLead } from '../../lib/public/canonical-contact';

// Private contact/registration workflows are unchanged. Only published text
// from the exact selected organization may replace the static introduction.
export async function load({url}: {url:URL}) {
  const site=await loadPublicSite(url);
  const pages=await readWebsitePages(site.organization);
  return {...site,contactLead:canonicalContactLead(pages,site.organization)};
}
