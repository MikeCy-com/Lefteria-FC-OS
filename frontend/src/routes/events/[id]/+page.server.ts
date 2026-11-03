import { loadPublicCatalogDetail } from '../../../lib/server/public-catalog';
export const load = ({ url, params }: { url: URL; params: { id: string } }) => loadPublicCatalogDetail(url, 'events', params.id);
