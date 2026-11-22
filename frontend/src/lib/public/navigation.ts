import type { PublicGroup, PublicArticle, PublicOrganization } from './types';
import { articleLink, newsLink } from './content.ts';
import { publicSlug } from './public-urls.ts';
import type { PublicIconName } from './icon-types';

export type PublicMenuLink = readonly [id: string, label: string, path: string];
export type PublicNavigationItem = { id: string; label: string; icon: PublicIconName } &
  ({ path: string; items?: never } | { items: readonly PublicMenuLink[]; path?: never });


export const publicNavigation: readonly PublicNavigationItem[] = [
  { id: "home", label: "ΑΡΧΙΚΗ", path: "/", icon: 'home' },
  { id: "club", label: "ΣΥΛΛΟΓΟΣ", icon: 'info', items: [
    ["about", "Η ιστορία μας", "/about"], ["join", "Γίνε μέλος", "/join"], ["seasons", "Παλαιότερες σεζόν", "/seasons"],
    ["partners", "Συνεργάτες", "/partners"], ["sponsors-team", "Χορηγοί πρώτης ομάδας", "/sponsors/first-team"], ["sponsors-academy", "Χορηγοί ακαδημίας", "/sponsors/academy"],
  ] },
  { id: "team", label: "ΟΜΑΔΑ", icon: 'users', items: [
    ["overview", "Πρώτη ομάδα", "/team"], ["roster", "Ρόστερ", "/team?tab=roster"], ["results", "Αποτελέσματα", "/team?tab=results"],
    ["schedule", "Πρόγραμμα", "/team?tab=schedule"], ["gallery", "Γκαλερί", "/team?tab=gallery"], ["venues", "Γήπεδα", "/team?tab=venues"],
    ["trials", "Δοκιμαστικά", "/trials"], ["partners", "Συνεργάτες ομάδας", "/partners?scope=first_team"],
  ] },
  { id: "academy", label: "ΑΚΑΔΗΜΙΑ", icon: 'academy', items: [
    ["overview", "Η ακαδημία μας", "/academy"], ["philosophy", "Η φιλοσοφία μας", "/academy/philosophy"],
    ["groups", "Ηλικιακά τμήματα", "/academy/groups"], ["registration", "Εγγραφές αθλητών", "/academy/registration"],
    ["partners", "Συνεργάτες ακαδημίας", "/partners?scope=academy"],
  ] },
  { id: "matches", label: "ΑΓΩΝΕΣ", icon: 'calendar', items: [
    ["schedule", "Πρόγραμμα αγώνων", "/team?tab=schedule"], ["results", "Αποτελέσματα", "/team?tab=results"],
    ["standings", "Βαθμολογία", "/team?tab=overview#standings"], ["vote", "Παίκτης του μήνα", "/vote"], ["tickets", "Προϊόντα συλλόγου", "/shop"],
  ] },
  { id: "news", label: "ΝΕΑ", path: "/news", icon: 'news' },
  { id: "events", label: "ΕΚΔΗΛΩΣΕΙΣ", path: "/events", icon: 'event' },
  { id: "shop", label: "ΚΑΤΑΣΤΗΜΑ", path: "/shop", icon: 'bag' },
  { id: "contact", label: "ΕΠΙΚΟΙΝΩΝΙΑ", path: "/contact", icon: 'phone' },
];
export function academyGroupLink(group: Pick<PublicGroup, 'id' | 'name'>): string {
  const slug = publicSlug(group.name);
  return '/academy/' + (slug ? slug + '--' : '') + encodeURIComponent(group.id);
}
export function categoryLinks(item: PublicNavigationItem, groups: PublicGroup[]): readonly PublicMenuLink[] {
  return item.id === 'academy' ? [...(item.items ?? []), ...groups.map(group =>
    [group.id, group.name, academyGroupLink(group)] as const)] : item.items ?? [];
}
type NavigationLocation = string | Pick<URL, 'href'>;
function navigationLocation(location: NavigationLocation): URL {
  const url = new URL(typeof location === 'string' ? location : location.href, 'https://navigation.invalid');
  if (url.pathname.length > 1) url.pathname = url.pathname.replace(/\/+$/, '');
  const aliases: Record<string, string> = { '/fixtures': 'results', '/calendar': 'schedule', '/venues': 'venues', '/staff': 'overview' };
  if (aliases[url.pathname]) { url.searchParams.set('tab', aliases[url.pathname]); url.pathname = '/team'; }
  return url;
}
const within = (pathname: string, parent: string) => pathname === parent || pathname.startsWith(parent + '/');
function teamTab(url: URL): string {
  const value = url.searchParams.get('tab') ?? 'overview';
  return ['overview', 'roster', 'staff', 'schedule', 'results', 'statistics', 'gallery', 'venues'].includes(value) ? value : 'overview';
}
function scopedOwner(url: URL): 'academy' | 'team' | 'club' {
  // Fixed sponsor-directory routes outrank a conflicting caller query, just as
  // their server loaders do. Detail/catalog links carry their explicit scope.
  if (url.pathname === '/sponsors/academy') return 'academy';
  if (url.pathname === '/sponsors/first-team') return 'team';
  return url.searchParams.get('scope') === 'academy' ? 'academy' : url.searchParams.get('scope') === 'first_team' ? 'team' : 'club';
}
export function navigationOwner(location: NavigationLocation, organization: PublicOrganization = 'lefteria_fc'): string | null {
  const url = navigationLocation(location); const path = url.pathname;
  if (path === '/') return 'home';
  if (within(path, '/academy')) return 'academy';
  if (path === '/team') return ['schedule', 'results'].includes(teamTab(url)) || (teamTab(url) === 'overview' && url.hash === '#standings') ? 'matches' : 'team';
  if (within(path, '/player')) return organization === 'lefteria_fc_academy' ? 'academy' : 'team';
  if (within(path, '/staff') || path === '/trials') return 'team';
  if (within(path, '/match') || path === '/vote') return 'matches';
  if (within(path, '/partners') || within(path, '/sponsors')) return scopedOwner(url);
  if (within(path, '/about') || within(path, '/seasons') || ['/join', '/privacy', '/terms'].includes(path)) return 'club';
  if (within(path, '/news')) return 'news';
  if (within(path, '/events')) return 'events';
  if (['/shop', '/cart', '/checkout'].some(parent => within(path, parent))) return 'shop';
  if (path === '/contact') return 'contact';
  return null;
}
export function navigationActive(item: PublicNavigationItem, location: NavigationLocation, organization: PublicOrganization = 'lefteria_fc'): boolean {
  const url = navigationLocation(location);
  // Published CMS pages may sit at root or beneath a category; their route
  // has no fixed navigationOwner. Derive the active section from the actual
  // organization-scoped published URL projected into this menu instead.
  if (within(url.pathname, '/pages')) {
    // Direct CMS URLs remain valid bookmarks. Their nav owners now point to
    // native section anchors, so compare the server-published exact owner/slug
    // identity instead of treating a different /pages link as the destination.
    const slug=url.pathname.slice('/pages/'.length);
    const owner=url.searchParams.get('organization_id')??'lefteria_fc';
    if(!['lefteria_fc','lefteria_fc_academy'].includes(owner))return false;
    const expected='page-'+owner+'-'+slug;
    if(item.path && item.id===expected)return true;
    return item.items?.some(([id])=>id===expected)??false;
  }
  return navigationOwner(url, organization) === item.id;
}
function academyIdentity(pathname: string, groups: readonly PublicGroup[]): string | null {
  if (!pathname.startsWith('/academy/')) return null;
  let value: string;
  try { value = decodeURIComponent(pathname.slice('/academy/'.length)); } catch { return null; }
  if (groups.some(group => group.id === value)) return value;
  const separator = value.indexOf('--');
  const suffix = separator > 0 ? value.slice(separator + 2) : null;
  return suffix && groups.some(group => group.id === suffix) ? suffix : null;
}
export function navigationLinkCurrent(path: string, location: NavigationLocation, groups: readonly PublicGroup[] = []): 'page' | 'location' | undefined {
  if (!path.startsWith('/') || path.startsWith('//')) return undefined;
  const target = navigationLocation(path); const current = navigationLocation(location);
  if (within(target.pathname, '/pages') && within(current.pathname, '/pages')) {
    // Two organizations can publish the same slug. In that case pathname
    // equality does NOT mean the menu link belongs to the displayed page.
    // The canonical first-team URL omits organization_id; Academy requires it.
    const allowed = ['lefteria_fc','lefteria_fc_academy'];
    const targetOrg = target.searchParams.get('organization_id') ?? 'lefteria_fc';
    const currentOrg = current.searchParams.get('organization_id') ?? 'lefteria_fc';
    if (!allowed.includes(targetOrg) || !allowed.includes(currentOrg) ||
        targetOrg !== currentOrg || target.pathname !== current.pathname ||
        (target.hash && target.hash !== current.hash)) return undefined;
    return 'page';
  }
  if (target.pathname === '/team' && current.pathname === '/team') {
    const targetTab = teamTab(target);
    const currentTab = teamTab(current);
    if (targetTab !== currentTab) return undefined;
    if (target.hash) return target.hash === current.hash ? 'location' : undefined;
    // The standings link owns this anchored destination, not generic overview.
    return current.hash === '#standings' && targetTab === 'overview' ? undefined : 'page';
  }
  if (target.pathname === '/partners' && within(current.pathname, '/partners')) {
    if (scopedOwner(target) !== scopedOwner(current)) return undefined;
    return target.pathname === current.pathname ? 'page' : 'location';
  }
  if (['/sponsors/academy', '/sponsors/first-team'].includes(target.pathname) && within(current.pathname, '/sponsors')) {
    if (scopedOwner(target) !== scopedOwner(current)) return undefined;
    return target.pathname === current.pathname ? 'page' : 'location';
  }
  const targetGroup = academyIdentity(target.pathname, groups);
  if (targetGroup && targetGroup === academyIdentity(current.pathname, groups)) return 'page';
  if (target.pathname === current.pathname && (!target.hash || target.hash === current.hash)) return target.hash ? 'location' : 'page';
  if (['/news', '/events', '/seasons'].includes(target.pathname) && within(current.pathname, target.pathname)) return 'location';
  return undefined;
}
export type PublicSearchContext = {
  organization: PublicOrganization;
  navigation?: readonly PublicNavigationItem[];
};

/** Prevent cross-organization search results from opening the wrong hub.
 * Keep shared club destinations, but never show the other organization's
 * teams, trials, partners, sponsors, CMS pages, fixtures or groups. */
function belongsToSearchOrganization(path: string, organization: PublicOrganization): boolean {
  const url = new URL(path, 'https://public-search.invalid');
  const pathname = url.pathname;
  const scope = url.searchParams.get('scope');
  const contentOwner = url.searchParams.get('organization_id');
  const isAcademy = organization === 'lefteria_fc_academy';
  if (scope && scope !== (isAcademy ? 'academy' : 'first_team') && scope !== 'all') return false;
  if (pathname.startsWith('/pages/'))
    return (contentOwner ?? 'lefteria_fc') === organization;
  if (isAcademy) {
    if (pathname === '/team' || pathname.startsWith('/team/') ||
        pathname === '/trials' || pathname.startsWith('/trials/') ||
        pathname === '/vote' || pathname.startsWith('/vote/') ||
        pathname === '/sponsors/first-team' ||
        pathname === '/match' || pathname.startsWith('/match/')) return false;
  } else {
    if (pathname === '/academy' || pathname.startsWith('/academy/') ||
        pathname === '/sponsors/academy') return false;
  }
  return true;
}

export function publicSearch(
  query: string, groups: PublicGroup[], news: PublicArticle[],
  context?: PublicSearchContext
) {
  const normalize = (value: string) => value.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLocaleLowerCase('el-GR');
  const organization = context?.organization;
  const source = context?.navigation ?? publicNavigation;
  const links = source.flatMap(item => {
    if (organization &&
        (organization === 'lefteria_fc_academy' ? ['team','matches'].includes(item.id) : item.id === 'academy'))
      return [];
    return item.items ? item.items.map(([id,label,path]) => ({
      id: item.id + '-' + id, label, path
    })) : [{id:item.id,label:item.label,path:item.path}];
  }).filter(item => !organization || (
    (!/(?:^|-)page-(?:lefteria_fc_academy|lefteria_fc)-/.test(item.id) ||
      item.id.includes('page-'+organization+'-')) &&
    belongsToSearchOrganization(item.path, organization)))
    .map(item => {
      if (!organization) return item;
      if (item.path === '/news') return {...item,path:newsLink(organization)};
      if (item.path === '/partners') return {
        ...item,path:'/partners?scope=' + (organization === 'lefteria_fc_academy' ? 'academy' : 'first_team')
      };
      return item;
    });
  const groupResults = groups
    .filter(group => !organization || (organization === 'lefteria_fc_academy' &&
      group.organization_id === 'lefteria_fc_academy'))
    .map(group => ({id:group.id,label:'Ακαδημία · ' + group.name,path:academyGroupLink(group)}));
  const articles = news.filter(article => !organization || article.organization_id === organization)
    .map(article => ({
      id:article.id,label:article.title,
      path:articleLink(article,organization ?? null)
    }));
  const seen = new Set<string>();
  const normalized = normalize(query.trim());
  return [...links,...groupResults,...articles].filter(item => {
    if (seen.has(item.path)) return false;
    seen.add(item.path);
    return normalize(item.label).includes(normalized);
  }).slice(0,16);
}
