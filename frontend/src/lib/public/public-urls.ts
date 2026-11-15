/**
 * Human-readable URLs for public, already-published identities.
 * Stable opaque IDs are retained for uniqueness, renames and old bookmarks.
 * Never call this with private CRM names: use only public projection DTOs.
 */
const greek: Record<string,string> = {
  'α':'a','β':'v','γ':'g','δ':'d','ε':'e','ζ':'z','η':'i','θ':'th',
  'ι':'i','κ':'k','λ':'l','μ':'m','ν':'n','ξ':'x','ο':'o','π':'p',
  'ρ':'r','σ':'s','ς':'s','τ':'t','υ':'y','φ':'f','χ':'ch','ψ':'ps','ω':'o'
};

export function publicSlug(value: string): string {
  const folded = value.normalize('NFD').replace(/[\u0300-\u036f]/g,'').toLocaleLowerCase('el-GR');
  return [...folded].map(char => greek[char] ?? char).join('')
    .replace(/[^a-z0-9]+/g,'-').slice(0,80).replace(/^-+|-+$/g,'');
}

type PublicEntity = { id: string; name: string };
export function personPublicLink(kind: 'player'|'staff', person: PublicEntity): string {
  const slug = publicSlug(person.name ?? '');
  return '/' + kind + '/' + (slug ? slug + '--' : '') + encodeURIComponent(person.id);
}

export function fixturePublicLink(fixture: { id: string; home_team: string; away_team: string }): string {
  const slug = fixture.home_team || fixture.away_team
    ? publicSlug((fixture.home_team ?? '') + '-vs-' + (fixture.away_team ?? '')) : '';
  return '/match/' + (slug ? slug + '--' : '') + encodeURIComponent(fixture.id);
}

export function publicCanonicalHref(path: string): string {
  if (!path.startsWith('/') || path.startsWith('//')) return 'https://lefteriafc.cy/';
  const url = new URL(path, 'https://lefteriafc.cy');
  // Canonical only verified public owner scope, never return filters,
  // guardian/child context, arbitrary query, tokens or caller parameters.
  const routeParts = url.pathname.split('/');
  const isPublicCatalogDetail = routeParts.length === 3
    && ['events','partners','sponsors','seasons'].includes(routeParts[1])
    && routeParts[2].length > 0;
  const scope = url.searchParams.get('scope');
  const owner = isPublicCatalogDetail && (scope === 'academy' || scope === 'first_team') ? '?scope=' + scope : '';
  const newsOwner = url.searchParams.get('organization_id');
  const isNewsDetail = routeParts.length === 3 && routeParts[1] === 'news' && routeParts[2].length > 0;
  const newsOrganization = isNewsDetail &&
    (newsOwner === 'lefteria_fc' || newsOwner === 'lefteria_fc_academy')
    ? '?organization_id=' + newsOwner : '';
  // Native CMS pages share a route between First Team and Academy. The
  // verified Academy owner is part of the page identity and must survive
  // canonicalization; unrelated search params and spoofed owners must not.
  const websiteOrganization = routeParts.length === 3 && routeParts[1] === 'pages'
    && /^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(routeParts[2])
    && newsOwner === 'lefteria_fc_academy'
    ? '?organization_id=lefteria_fc_academy' : '';
  return 'https://lefteriafc.cy' + url.pathname + owner + newsOrganization + websiteOrganization;
}

/** Only already-published HTTPS media URLs may be advertised to social crawlers.
 * Relative/private attachments are not transformed into crawlable endpoints.
 */
export function publicSocialImage(value: string | null | undefined): string | null {
  if (!value || value.length > 2048 || /[\u0000-\u001f\u007f\\]/.test(value)) return null;
  try {
    const url = new URL(value);
    if (url.protocol !== 'https:' || url.username || url.password) return null;
    return url.href;
  } catch { return null; }
}
