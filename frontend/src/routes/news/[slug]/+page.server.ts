import { loadPublicArticle } from '../../../lib/server/public-content';
export const load = ({ url, params }: { url: URL; params: { slug: string } }) => loadPublicArticle(url, params.slug);
