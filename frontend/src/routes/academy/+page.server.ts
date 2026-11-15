import { loadSectionPage } from '../../lib/server/public-sections';
export const load = ({ url }: { url: URL }) => loadSectionPage(url, 'lefteria_fc_academy');
