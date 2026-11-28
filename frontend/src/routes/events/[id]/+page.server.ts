import { error, redirect } from '@sveltejs/kit';
import { catalogCanonicalDetailLink, catalogCanonicalRedirect } from '../../../lib/public/PublicCatalog';
import { loadPublicCatalogDetail } from '../../../lib/server/public-catalog';

export const load = async ({ url, params }: { url: URL; params: { id: string } }) => {
  // The actual published DTO resolves first. Guessing IDs, wrong organizations
  // and unverified previews still return the existing 404/503 responses.
  const data = await loadPublicCatalogDetail(url,'events',params.id);
  const row = data.event;
  if (!row) error(404,'Η σελίδα δεν βρέθηκε.');
  const canonical = catalogCanonicalDetailLink('events',row,row.title);
  const redirectTo = catalogCanonicalRedirect(url,canonical);
  if(redirectTo) redirect(301,redirectTo);
  return data;
};
