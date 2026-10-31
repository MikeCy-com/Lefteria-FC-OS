import { loadPublicSite } from '../../lib/server/public-content';
export const load = ({ url }: { url: URL }) => loadPublicSite(url);
