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
    ["standings", "Βαθμολογία", "/team?tab=overview"], ["vote", "Παίκτης του μήνα", "/vote"], ["tickets", "Εισιτήρια", "/shop"],
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
export function navigationActive(item: PublicNavigationItem, pathname: string): boolean {
  return item.path ? item.path === '/' ? pathname === '/' : pathname.startsWith(item.path) :
    (item.items ?? []).some(([, , path]) => pathname === path.split('?')[0]);
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
