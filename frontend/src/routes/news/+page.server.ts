import { loadPublicNews } from '../../lib/server/public-content';
export const load = ({ url }: { url: URL }) => loadPublicNews(url);
