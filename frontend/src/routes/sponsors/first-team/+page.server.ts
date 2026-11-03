import { loadPublicCatalog } from '../../../lib/server/public-catalog';
export const load = ({ url }: { url: URL }) => loadPublicCatalog(url, 'sponsors', 'first_team');
