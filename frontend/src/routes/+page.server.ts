import { loadPublicHome } from '../lib/server/public-content';
export const load = ({ url }: { url: URL }) => loadPublicHome(url);
