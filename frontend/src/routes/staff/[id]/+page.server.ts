import { loadStaffPage } from '../../../lib/server/public-sections';
export const load = ({ url, params }: { url: URL; params: { id: string } }) => loadStaffPage(url, params.id);
