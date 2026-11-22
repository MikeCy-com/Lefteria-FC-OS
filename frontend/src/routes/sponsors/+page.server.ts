import { loadPublicCatalog } from '../../lib/server/public-catalog';
import { readWebsitePages } from '../../lib/server/website-pages';
import { nativeEditorialBlocks, nativeEditorialPageLayout } from '../../lib/public/native-editorial-regions';

export async function load({url}: {url:URL}) {
  const catalog=await loadPublicCatalog(url,'sponsors');
  const pages=await readWebsitePages(catalog.organization);
  return {...catalog,editorialBlocks:nativeEditorialBlocks(pages,catalog.organization,'canonical-sponsors-sections'),editorialLayout:nativeEditorialPageLayout(pages,catalog.organization,'canonical-sponsors-sections')};
}
