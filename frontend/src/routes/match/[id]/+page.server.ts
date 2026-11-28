import { redirect } from '@sveltejs/kit';
import { fixturePublicLink } from '../../../lib/public/public-urls';
import { loadPublicMatch } from '../../../lib/server/public-match';

export const load = async ({ url, params }: { url: URL; params: { id: string } }) => {
  const data = await loadPublicMatch(url, params.id);
  const canonical = fixturePublicLink(data.fixture);
  if (decodeURIComponent(url.pathname) !== decodeURIComponent(canonical)) redirect(301, canonical);
  return data;
};
