export type MobileRole = 'fan' | 'player' | 'parent' | 'coach' | 'management';

export const ROLE_NAMES = {
  parent: 'Γονέας', player: 'Παίκτης', coach: 'Προπονητής',
  management: 'Διοίκηση', fan: 'Φίλαθλος'
} as const satisfies Record<MobileRole, string>;

export const TAB_NAMES = {
  home: 'Αρχική', calendar: 'Πρόγραμμα', matches: 'Αγώνες', chat: 'Μηνύματα',
  profile: 'Προφίλ', notifications: 'Ειδοποιήσεις', more: 'Περισσότερα',
  social: 'Lefteria', financials: 'Οικονομικά', reels: 'Στιγμές', gamehub: 'Παιχνίδια',
  news: 'Νέα', announcements: 'Ανακοινώσεις', shop: 'Κατάστημα', team: 'Ομάδα & Ακαδημία',
  teams: 'Ομάδες', squad: 'Ρόστερ', children: 'Παιδιά', education: 'Εκπαίδευση', hub: 'HUB', fixtures: 'Πρόγραμμα αγώνων',
  standings: 'Βαθμολογία', predict: 'Προβλέψεις', potm: 'Παίκτης του μήνα'
} as const;

export type MobileTab = keyof typeof TAB_NAMES;

export interface MobileRoleDefinition {
  readonly label: string;
  readonly defaultTab: MobileTab;
  readonly primaryTabs: readonly MobileTab[];
  readonly allowedTabs: readonly MobileTab[];
}

const commonTabs = ['home', 'profile', 'notifications', 'more'] as const;
const memberTabs = ['calendar', 'matches', 'chat', 'announcements', 'social'] as const;

// Only the active role contributes navigation. Other identities require an
// explicit switch. These affordances do not replace server authorization.
export const MOBILE_ROLE_DEFINITIONS = {
  fan: {
    label: ROLE_NAMES.fan,
    defaultTab: 'home',
    primaryTabs: ['home', 'matches', 'gamehub', 'shop'],
    allowedTabs: [...commonTabs, 'matches', 'gamehub', 'news', 'shop', 'team', 'fixtures', 'standings', 'predict', 'potm', 'hub']
  },
  player: {
    label: ROLE_NAMES.player,
    defaultTab: 'home',
    primaryTabs: ['home', 'matches', 'social', 'shop'],
    allowedTabs: [...commonTabs, ...memberTabs, 'shop', 'hub']
  },
  parent: {
    label: ROLE_NAMES.parent,
    defaultTab: 'home',
    primaryTabs: ['home', 'calendar', 'children', 'chat', 'more'],
    allowedTabs: [...commonTabs, ...memberTabs, 'children', 'education', 'hub', 'financials']
  },
  coach: {
    label: ROLE_NAMES.coach,
    defaultTab: 'home',
    primaryTabs: ['home', 'calendar', 'squad', 'chat', 'more'],
    allowedTabs: [...commonTabs, ...memberTabs, 'squad']
  },
  management: {
    label: ROLE_NAMES.management,
    defaultTab: 'home',
    primaryTabs: ['home', 'calendar', 'teams', 'financials', 'more'],
    allowedTabs: [...commonTabs, ...memberTabs, 'teams', 'financials']
  }
} as const satisfies Record<MobileRole, MobileRoleDefinition>;

const legacyTabAliases: Readonly<Partial<Record<string, MobileTab>>> = {
  education_hub: 'hub', schedule: 'calendar', training: 'calendar', event: 'calendar', match: 'matches',
  parent: 'home', player: 'home', coach: 'home', management: 'home'
};

export function isMobileRole(value: unknown): value is MobileRole {
  return typeof value === 'string' && Object.hasOwn(MOBILE_ROLE_DEFINITIONS, value);
}

export function primaryTabs(role: MobileRole): readonly MobileTab[] {
  return MOBILE_ROLE_DEFINITIONS[role].primaryTabs;
}

export function allowedTabs(role: MobileRole): readonly MobileTab[] {
  return MOBILE_ROLE_DEFINITIONS[role].allowedTabs;
}

export function canComposePost(role: MobileRole): boolean {
  // Parent mode has no composer, even when a separate fan identity is available.
  return role === 'fan' || role === 'player';
}

export function normalizeTab(rawTab: string | null | undefined, role: MobileRole): MobileTab {
  const fallback = MOBILE_ROLE_DEFINITIONS[role].defaultTab;
  if (!rawTab) return fallback;
  // Preserve the old family-settings URL without granting another role access.
  if (rawTab === 'family') return role === 'parent' ? 'children' : fallback;
  const candidate = role === 'fan' && (rawTab === 'social' || rawTab === 'reels')
    ? 'home'
    : legacyTabAliases[rawTab] ?? rawTab;
  return allowedTabs(role).find((tab) => tab === candidate) ?? fallback;
}

export function tabFromLocation(
  location: { readonly pathname: string; readonly search: string },
  role: MobileRole
): MobileTab {
  const query = new URLSearchParams(location.search);
  const segment = location.pathname.split('/')[2];
  // An empty query falls back to the path; an invalid nonempty query goes home.
  return normalizeTab(query.get('tab') || segment, role);
}

export function bottomTabFor(activeTab: string, role: MobileRole): MobileTab | null {
  return primaryTabs(role).find((tab) => tab === activeTab) ?? null;
}


// Fan public reads already exist on the club website. Member match/calendar
// routes stay private and must never inherit this navigation shortcut.
export function fanPublicDestination(tab: string, role: MobileRole | null): string | null {
  if (role !== 'fan') return null;
  switch (tab) {
    case 'matches': return '/team?tab=results';
    case 'fixtures': return '/team?tab=schedule';
    case 'standings': return '/team?tab=overview#standings';
    case 'news': return '/news';
    case 'team': return '/team';
    default: return null;
  }
}

// A secondary destination may highlight its *own* parent section, but never
// borrow another role's tab. Header destinations remain intentionally unselected.
const CONTEXTUAL_PRIMARY_TABS: Readonly<Partial<Record<MobileRole, Partial<Record<MobileTab, MobileTab>>>>> = {
  parent: { financials: 'more', education: 'more', hub: 'more', announcements: 'chat' },
  coach: { announcements: 'chat' },
  management: { announcements: 'more' }
};

export function contextualBottomTabFor(activeTab: MobileTab, role: MobileRole): MobileTab | null {
  const direct = bottomTabFor(activeTab, role);
  if (direct) return direct;
  if (!allowedTabs(role).includes(activeTab)) return null;
  const contextual = CONTEXTUAL_PRIMARY_TABS[role]?.[activeTab];
  return contextual && primaryTabs(role).includes(contextual) ? contextual : null;
}
