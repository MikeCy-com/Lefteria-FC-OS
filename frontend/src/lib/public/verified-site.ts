import type { PublicOrganization, PublicSite } from './types';

/** Never display public site data returned for a different organization. */
export function verifiedPublicSite(site: PublicSite | null, organization: PublicOrganization): PublicSite | null {
  return site?.organization_id === organization && (site.club == null || site.club.organization_id === organization)
    ? site : null;
}
