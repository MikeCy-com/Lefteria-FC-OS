import type { components } from '../api/schema';

export type ShopProduct = components['schemas']['PublicSectionProduct'];
export type ShopTicket = components['schemas']['PublicSectionTicket'];
export type ShopTab = 'tickets' | 'merchandise';
export type ShopOrganization = 'lefteria_fc' | 'lefteria_fc_academy';

export function shopTab(url: URL): ShopTab {
  return url.searchParams.get('tab') === 'merchandise' ? 'merchandise' : 'tickets';
}

// A shared logical catalog item can occur in both FC and Academy public
// projections; render it once, preferring the FC public source.
export function uniquePublishedItems<T extends { id: string; organization_id: string }>(
  sources: readonly { organization: ShopOrganization; items: readonly T[] }[]
): T[] {
  const byId = new Map<string, T>();
  for (const { organization, items } of sources) {
    for (const item of items) {
      if (item.organization_id !== organization || typeof item.id !== 'string'
          || !item.id || item.id.length > 128 || byId.has(item.id)) continue;
      byId.set(item.id, item);
    }
  }
  return [...byId.values()];
}

export function formatShopPrice(value: number | string): string {
  const numeric = typeof value === 'number' ? value : Number(value);
  if (!Number.isFinite(numeric) || numeric < 0 || numeric > 1_000_000) return '—';
  return new Intl.NumberFormat('el-CY', {
    style: 'currency', currency: 'EUR', minimumFractionDigits: 2, maximumFractionDigits: 2
  }).format(numeric);
}
