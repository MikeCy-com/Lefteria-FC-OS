import { error } from '@sveltejs/kit';
import { loadPublicSite, publicOrganization } from '../../../lib/server/public-content';
import { readWebsitePages } from '../../../lib/server/website-pages';
import { isHomeHeroSlug } from '../../../lib/public/website-pages';

export async function load({url,params}: {url: URL;params:{slug:string}}) {
  if (isHomeHeroSlug(params.slug) || !/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(params.slug) || params.slug.length > 80)
    error(404, 'Η σελίδα δεν βρέθηκε.');
  const org = publicOrganization(url);
  const [site, pages] = await Promise.all([loadPublicSite(url), readWebsitePages(org)]);
  const published = pages.find(p => p.slug === params.slug);
  if (!published) error(404, 'Η σελίδα δεν βρέθηκε.');
  return {...site, published};
}
