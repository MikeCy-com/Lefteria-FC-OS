import { redirect } from '@sveltejs/kit';
import { personPublicLink } from '../../../lib/public/public-urls';
import { loadPlayerPage } from '../../../lib/server/public-sections';

export const load = async ({ url, params }: { url: URL; params: { id: string } }) => {
  const data = await loadPlayerPage(url, params.id);
  const canonical = personPublicLink('player', data.player);
  if (decodeURIComponent(url.pathname) !== decodeURIComponent(canonical)) {
    const team = data.returnTeamId ? '?team_id=' + encodeURIComponent(data.returnTeamId) : '';
    redirect(301, canonical + team);
  }
  return data;
};
