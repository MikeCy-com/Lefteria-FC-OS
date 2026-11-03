import { loadPlayerPage } from '../../../lib/server/public-sections';
export const load = ({ url, params }: { url: URL; params: { id: string } }) => loadPlayerPage(url, params.id);
