import type { PublicArticle, PublicFixture, PublicGroup } from './types';
import type { PublicSections } from './sections-types';
import { articleLink } from './content.ts';
import { academyGroupLink } from './navigation.ts';
import { catalogHref } from './PublicCatalog.ts';

// Only the verified public host may advertise an indexable sitemap.
// Preview, localhost and host-header aliases must never list canonical content.
export const CANONICAL_PUBLIC_ORIGIN = 'https://lefteriafc.cy';
const PUBLIC_HOSTS = new Set([CANONICAL_PUBLIC_ORIGIN, 'https://www.lefteriafc.cy']);

export function isIndexablePublicOrigin(url: URL): boolean {
  return PUBLIC_HOSTS.has(url.origin) && !url.username && !url.password;
}

const PUBLIC_ROUTES = [
  '/', '/about', '/team', '/news', '/academy', '/academy/philosophy',
  '/academy/groups', '/events', '/partners', '/sponsors',
  '/sponsors/first-team', '/sponsors/academy', '/seasons', '/contact',
  '/privacy', '/terms'
] as const;

export function robotsFor(url: URL): string {
  if (!isIndexablePublicOrigin(url)) return 'User-agent: *\nDisallow: /\n';
  return 'User-agent: *\n'
    + 'Disallow: /admin\nDisallow: /app\nDisallow: /api\n'
    + 'Disallow: /login\nDisallow: /profile\nDisallow: /checkout\n'
    + 'Disallow: /cart\nDisallow: /register\n'
    + 'Allow: /\n\nSitemap: ' + CANONICAL_PUBLIC_ORIGIN + '/sitemap.xml\n';
}

export type PublicSitemapData = {
  articles: readonly PublicArticle[];
  groups: readonly PublicGroup[];
  sites: readonly { organization_id: string; fixtures: readonly PublicFixture[] }[];
  sections: readonly PublicSections[];
};

type SitemapRow = { loc: string; lastmod?: string; frequency: 'daily' | 'weekly' | 'monthly'; priority: string };

function safeId(value: string | null | undefined): value is string {
  return typeof value === 'string' && value.length > 0 && value.length <= 128
    && value !== '.' && value !== '..' && !/[\u0000-\u001f\u007f\\/?#]/.test(value);
}

function xmlEscape(value: string): string {
  return value.replace(/[&<>"']/g, char => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&apos;'
  })[char] ?? char);
}

function validLastMod(value: string | null | undefined): string | undefined {
  if (!value || !/^\d{4}-\d\d-\d\d(?:T|$)/.test(value)) return undefined;
  const date = new Date(value);
  return Number.isFinite(date.getTime()) ? date.toISOString().slice(0, 10) : undefined;
}

export function buildPublicSitemap(data: PublicSitemapData): string {
  const rows = new Map<string, SitemapRow>();
  function add(path: string, frequency: SitemapRow['frequency'] = 'weekly', priority = '0.6', lastmod?: string) {
    if (!path.startsWith('/') || path.startsWith('//')) return;
    const url = new URL(path, CANONICAL_PUBLIC_ORIGIN);
    if (url.origin !== CANONICAL_PUBLIC_ORIGIN) return;
    rows.set(url.href, { loc: url.href, frequency, priority, ...(lastmod ? { lastmod } : {}) });
  }

  for (const path of PUBLIC_ROUTES) add(path, path === '/' || path === '/news' ? 'daily' : 'weekly', path === '/' ? '1.0' : '0.7');

  // The authenticated source database never enters this feed. Articles come
  // exclusively from the safe, anonymous public-content projection.
  for (const article of data.articles) {
    if (safeId(article.id)) add(articleLink(article), 'weekly', '0.8', validLastMod(article.created_at));
  }

  for (const group of data.groups) {
    if (group.organization_id === 'lefteria_fc_academy' && safeId(group.id)) add(academyGroupLink(group));
  }

  // The public match-detail page requires an ID to be unique across the two
  // organizations. Avoid advertising duplicate IDs that currently resolve 503.
  const allFixtures = data.sites.flatMap(site => site.fixtures
    .filter(fixture => fixture.organization_id === site.organization_id && safeId(fixture.id)));
  const counts = new Map<string, number>();
  for (const fixture of allFixtures) counts.set(fixture.id, (counts.get(fixture.id) ?? 0) + 1);
  for (const fixture of allFixtures) {
    if (counts.get(fixture.id) === 1) add('/match/' + encodeURIComponent(fixture.id));
  }

  for (const section of data.sections) {
    if (section.organization_id !== 'lefteria_fc' && section.organization_id !== 'lefteria_fc_academy') continue;
    // Youth Academy profiles and unconfirmed ages are intentionally not indexed.
    if (section.organization_id === 'lefteria_fc') {
      for (const player of section.players) {
        if (player.organization_id === 'lefteria_fc' && player.team_type === 'First Team'
            && player.age !== null && Number(player.age) >= 18 && safeId(player.id))
          add('/player/' + encodeURIComponent(player.id));
      }
      for (const staff of section.staff) {
        if (staff.organization_id === 'lefteria_fc' && safeId(staff.id))
          add('/staff/' + encodeURIComponent(staff.id));
      }
    }
    // Other published catalog records are safe public projections, not
    // unrestricted CRM, family, payments or player-development records.
    for (const event of section.events) {
      if (event.organization_id === section.organization_id && safeId(event.id))
        add(catalogHref('events', event, event.title));
    }
    for (const partner of section.partners) {
      if (partner.organization_id === section.organization_id && safeId(partner.slug))
        add('/partners/' + encodeURIComponent(partner.slug)
          + (section.organization_id === 'lefteria_fc_academy' ? '?scope=academy' : '?scope=first_team'));
    }
    for (const sponsor of section.sponsors) {
      if (sponsor.organization_id === section.organization_id && safeId(sponsor.id))
        add(catalogHref('sponsors', sponsor, sponsor.name));
    }
    for (const season of section.archived_seasons) {
      if (season.organization_id === section.organization_id && safeId(season.id))
        add(catalogHref('seasons', season, season.season_name));
    }
  }

  const entries = [...rows.values()].sort((a, b) => a.loc.localeCompare(b.loc, 'en'));
  const xml = entries.map(row => '  <url>\n'
    + '    <loc>' + xmlEscape(row.loc) + '</loc>\n'
    + (row.lastmod ? '    <lastmod>' + xmlEscape(row.lastmod) + '</lastmod>\n' : '')
    + '    <changefreq>' + row.frequency + '</changefreq>\n'
    + '    <priority>' + row.priority + '</priority>\n'
    + '  </url>').join('\n');
  return '<?xml version="1.0" encoding="UTF-8"?>\n'
    + '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n'
    + xml + '\n</urlset>\n';
}
