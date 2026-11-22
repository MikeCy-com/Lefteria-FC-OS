import { loadPublicSite } from '../../lib/server/public-content';
import { readWebsitePages } from '../../lib/server/website-pages';
import { canonicalAboutIntro } from '../../lib/public/canonical-about';
import { canonicalAboutHistory } from '../../lib/public/canonical-about-history';

// The canonical layout stays native; only the explicitly approved introduction
// uses the published, organization-scoped CMS projection.
export async function load({url}: {url:URL}) {
  const site=await loadPublicSite(url);
  const pages=await readWebsitePages(site.organization);
  return {...site,aboutIntro:canonicalAboutIntro(pages,site.organization),
    aboutHistory:canonicalAboutHistory(pages,site.organization)};
}
