// A public presentation-only partner slug from the original JoinPage.
// This never authorizes an organization, writes a lead or proves a partner.
export function partnerReferralSlug(url: Pick<URL,'searchParams'>): string | null {
  const values=url.searchParams.getAll('partner');
  return values.length===1 && /^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$/.test(values[0])
    ? values[0] : null;
}
