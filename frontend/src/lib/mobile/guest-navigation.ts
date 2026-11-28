import { allowedTabs, isMobileRole, TAB_NAMES, type MobileTab } from './navigation.ts';

// A guest is an anonymous visitor, never an identity or an API principal.
// Only these destinations are safe to browse without a mobile session.
export const GUEST_PUBLIC_LINKS = [
  { tab: 'news', href: '/news', label: 'Νέα & άρθρα' },
  { tab: 'matches', href: '/team?tab=results', label: 'Αποτελέσματα αγώνων' },
  { tab: 'fixtures', href: '/team?tab=schedule', label: 'Πρόγραμμα αγώνων' },
  { tab: 'standings', href: '/team?tab=overview#standings', label: 'Βαθμολογία' },
  { tab: 'team', href: '/team', label: 'Ομάδα & ρόστερ' },
  { tab: 'shop', href: '/shop', label: 'Επίσημο κατάστημα' },
] as const satisfies readonly { tab: MobileTab; href: string; label: string }[];

const aliases: Readonly<Record<string, MobileTab>> = {
  family: 'children', education_hub: 'hub', schedule: 'calendar',
  training: 'calendar', event: 'calendar', match: 'matches'
};

export function readMobileTab(value: unknown): MobileTab | null {
  return typeof value === 'string' && Object.hasOwn(TAB_NAMES, value)
    ? value as MobileTab : null;
}

export function guestRequestedTab(location: { pathname: string; search: string }): MobileTab {
  const raw = new URLSearchParams(location.search).get('tab') || location.pathname.split('/')[2];
  return readMobileTab(raw) ?? (raw && Object.hasOwn(aliases, raw) ? aliases[raw] ?? 'home' : 'home');
}

export function guestPublicLink(tab: MobileTab) {
  return GUEST_PUBLIC_LINKS.find(link => link.tab === tab) ?? null;
}

export function isGuestPublicTab(tab: MobileTab): boolean {
  return tab === 'home' || tab === 'more' || guestPublicLink(tab) !== null;
}

export function guestLoginHref(tab: MobileTab = 'home'): string {
  return '/app/login?' + new URLSearchParams({ return_tab: tab }).toString();
}

// Never accept a raw return URL, external origin, resource identifier, or
// mutation. Only a known navigation tab allowed for the verified active role
// can be resumed. The destination itself still enforces backend permissions.
export function mobileAfterLogin(returnTab: unknown, role: unknown, currentRoleAvailable: boolean): string {
  const tab = readMobileTab(returnTab);
  if (!currentRoleAvailable || !tab || !isMobileRole(role) || !allowedTabs(role).includes(tab) || tab === 'home')
    return '/app';
  return '/app?' + new URLSearchParams({ tab }).toString();
}

// A navigation hint only. The caller must pass the roles from the verified
// OTP response; this function never adds roles or requests a role switch.
export function roleCanResumeTab(returnTab: unknown, role: unknown): boolean {
  const tab = readMobileTab(returnTab);
  return tab !== null && isMobileRole(role) && allowedTabs(role).includes(tab);
}
