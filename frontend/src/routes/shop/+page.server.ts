import { loadPublicShop } from '../../lib/server/public-shop';
import { readWebsitePages } from '../../lib/server/website-pages';
import { canonicalShopLead } from '../../lib/public/canonical-shop';

export async function load({url}: {url:URL}) {
  const shop=await loadPublicShop(url);
  const pages=await readWebsitePages('lefteria_fc');
  return {...shop,editorialLead:canonicalShopLead(pages)};
}
