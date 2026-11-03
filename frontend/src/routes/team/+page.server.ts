import { loadTeamPage } from '../../lib/server/public-sections';
export const load = ({ url }: { url: URL }) => loadTeamPage(url);
