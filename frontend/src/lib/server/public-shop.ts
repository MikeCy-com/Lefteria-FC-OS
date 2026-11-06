import { loadPublicSite, publicApi } from './public-content';
import type { PublicPageData } from '../public/types';
import type { PublicSections } from '../public/sections-types';
import {
  shopTab, uniquePublishedItems,
  type ShopProduct, type ShopTicket, type ShopTab, type ShopOrganization
} from '../public/shop';

export type PublicShopData = PublicPageData & {
  tab: ShopTab;
  catalogAvailable: boolean;
  products: ShopProduct[];
  tickets: ShopTicket[];
};

async function readPublishedShopSection(organization: ShopOrganization): Promise<PublicSections | null> {
  try {
    const result = await publicApi().GET('/api/public/sections', {
      params: { query: { organization_id: organization } }
    });
    if (result.response.status !== 200 || result.data?.organization_id !== organization) return null;
    return result.data;
  } catch {
    return null;
  }
}

export async function loadPublicShop(url: URL): Promise<PublicShopData> {
  const [site, fc, academy] = await Promise.all([
    loadPublicSite(url, 'lefteria_fc'),
    readPublishedShopSection('lefteria_fc'),
    readPublishedShopSection('lefteria_fc_academy')
  ]);
  const catalogAvailable = site.available && fc !== null && academy !== null;

  // On a public projection outage do not invent stock, prices or claim that
  // the catalog is empty. The UI renders the site's explicit unavailable state.
  return {
    ...site, tab: shopTab(url), catalogAvailable,
    products: catalogAvailable
      ? uniquePublishedItems([
        { organization: 'lefteria_fc', items: fc.products },
        { organization: 'lefteria_fc_academy', items: academy.products }
      ]) : [],
    tickets: catalogAvailable
      ? uniquePublishedItems([
        { organization: 'lefteria_fc', items: fc.tickets },
        { organization: 'lefteria_fc_academy', items: academy.tickets }
      ]) : []
  };
}
