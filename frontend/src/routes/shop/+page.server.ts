import { loadPublicShop } from '../../lib/server/public-shop';

export const load = ({ url }: { url: URL }) => loadPublicShop(url);
