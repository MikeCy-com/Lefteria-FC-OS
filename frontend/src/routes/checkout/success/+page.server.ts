import { loadPublicSite } from '../../../lib/server/public-content';
export const load = async ({ url }: { url: URL }) => ({ ...(await loadPublicSite(url, 'lefteria_fc')), hasSession: url.searchParams.has('session_id') });
