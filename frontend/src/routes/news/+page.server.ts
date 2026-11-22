import { loadPublicNews } from '../../lib/server/public-content';
import { readWebsitePages } from '../../lib/server/website-pages';
import { nativeEditorialBlocks, nativeEditorialPageLayout } from '../../lib/public/native-editorial-regions';

export async function load({url}: {url:URL}) {
  const news=await loadPublicNews(url);
  const pages=await readWebsitePages(news.organization);
  return {...news,editorialBlocks:nativeEditorialBlocks(pages,news.organization,'canonical-news-sections'),editorialLayout:nativeEditorialPageLayout(pages,news.organization,'canonical-news-sections')};
}
