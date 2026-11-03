import { loadPublicCatalogDetail } from '../../../lib/server/public-catalog';
export const load = ({ url, params }: { url: URL; params: { id: string } }) => loadPublicCatalogDetail(url, 'partners', params.id);
