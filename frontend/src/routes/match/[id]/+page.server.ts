import { loadPublicMatch } from '../../../lib/server/public-match';

export const load = ({ url, params }: { url: URL; params: { id: string } }) => loadPublicMatch(url, params.id);
