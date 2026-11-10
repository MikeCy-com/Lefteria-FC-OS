import type { PublicGroup, PublicArticle } from './types';
import { articleLink } from './content.ts';
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
    ["standings", "Βαθμολογία", "/team?tab=overview#standings"], ["vote", "Παίκτης του μήνα", "/vote"], ["tickets", "Εισιτήρια", "/shop"],
  ] },
  { id: "news", label: "ΝΕΑ", path: "/news", icon: 'news' },
  { id: "events", label: "ΕΚΔΗΛΩΣΕΙΣ", path: "/events", icon: 'event' },
  { id: "shop", label: "ΚΑΤΑΣΤΗΜΑ", path: "/shop", icon: 'bag' },
  { id: "contact", label: "ΕΠΙΚΟΙΝΩΝΙΑ", path: "/contact", icon: 'phone' },
];
export function academyGroupLink(group: Pick<PublicGroup, 'id' | 'name'>): string {
  const slug = group.name.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLocaleLowerCase('el-GR')
    .trim().replace(/[^a-z0-9α-ω]+/g, '-').replace(/^-+|-+$/g, '');
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
  const aliases: Record<string, string> = { '/fixtures': 'results', '/calendar': 'schedule', '/venues': 'venues', '/staff': 'staff' };
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
export function navigationOwner(location: NavigationLocation): string | null {
  const url = navigationLocation(location); const path = url.pathname;
  if (path === '/') return 'home';
  if (within(path, '/academy')) return 'academy';
  if (path === '/team') return ['schedule', 'results'].includes(teamTab(url)) || (teamTab(url) === 'overview' && url.hash === '#standings') ? 'matches' : 'team';
  if (within(path, '/player') || within(path, '/staff') || path === '/trials') return 'team';
  if (within(path, '/match') || path === '/vote') return 'matches';
  if (within(path, '/partners') || within(path, '/sponsors')) return scopedOwner(url);
  if (within(path, '/about') || within(path, '/seasons') || ['/join', '/privacy', '/terms'].includes(path)) return 'club';
  if (within(path, '/news')) return 'news';
  if (within(path, '/events')) return 'events';
  if (['/shop', '/cart', '/checkout'].some(parent => within(path, parent))) return 'shop';
  if (path === '/contact') return 'contact';
  return null;
}
export function navigationActive(item: PublicNavigationItem, location: NavigationLocation): boolean {
  return navigationOwner(location) === item.id;
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
  if (target.pathname === current.pathname && (!target.hash || target.hash === current.hash)) return 'page';
  if (['/news', '/events', '/seasons'].includes(target.pathname) && within(current.pathname, target.pathname)) return 'location';
  return undefined;
}
export function publicSearch(query: string, groups: PublicGroup[], news: PublicArticle[]) {
  const normalize = (value: string) => value.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLocaleLowerCase('el-GR');
  const links = publicNavigation.flatMap(item => item.items ? item.items.map(([id, label, path]) =>
    ({ id: item.id + '-' + id, label, path })) : [{ id: item.id, label: item.label, path: item.path }]);
  const all = [...links, ...groups.map(group => ({ id: group.id, label: 'Ακαδημία · ' + group.name, path: academyGroupLink(group) })),
    ...news.map(article => ({ id: article.id, label: article.title, path: articleLink(article) }))];
  const seen = new Set<string>();
  return all.filter(item => { if (seen.has(item.path)) return false; seen.add(item.path); return normalize(item.label).includes(normalize(query)); }).slice(0, 16);
}
