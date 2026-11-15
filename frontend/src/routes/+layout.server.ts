import { readWebsitePages } from '../lib/server/website-pages';

// Public navigation renders on the server, including SEO/crawlable new links.
// Admin and private app routes never request public CMS content.
export async function load({url}: {url: URL}) {
  if (/^\/(admin|app|api)(\/|$)/.test(url.pathname))
    return {websitePages: {lefteria_fc: [], lefteria_fc_academy: []}};
  const [firstTeam, academy] = await Promise.all([
    readWebsitePages('lefteria_fc'), readWebsitePages('lefteria_fc_academy')
  ]);
  return {websitePages: {lefteria_fc: firstTeam, lefteria_fc_academy: academy}};
}
