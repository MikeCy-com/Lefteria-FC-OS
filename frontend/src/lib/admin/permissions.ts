export const ADMIN_ROLE_NAMES = {
  super_admin: 'Super Admin', manager: 'Manager', editor: 'Editor', marketing: 'Marketing',
  coach: 'Coach', matchday_editor: 'Match-day Editor'
} as const;

export type AdminRole = keyof typeof ADMIN_ROLE_NAMES;

const capabilityNames: Record<string, string> = {
  teams: 'Ομάδες', academy: 'Ακαδημία', fixtures: 'Αγώνες', results: 'Αποτελέσματα',
  live_score: 'Ζωντανό σκορ', match_reports: 'Αναφορές αγώνων', events: 'Εκδηλώσεις',
  attendance: 'Παρουσίες', news: 'Νέα', sponsors: 'Χορηγοί', gallery: 'Φωτογραφίες',
  messages: 'Μηνύματα', wall: 'Κοινότητα', finance: 'Οικονομικά', shop: 'Κατάστημα',
  facilities: 'Εγκαταστάσεις', training: 'Προπονήσεις', marketing: 'Προώθηση',
  staff: 'Προσωπικό', settings: 'Ρυθμίσεις', social: 'Κοινωνικά δίκτυα', users: 'Λογαριασμοί'
};
const actionNames: Record<string, string> = {
  '*': 'διαχείριση', read: 'προβολή', write: 'επεξεργασία', archive: 'αρχειοθέτηση',
  compliance: 'έλεγχος συμμόρφωσης', read_assigned: 'προβολή ανατεθειμένων ομάδων',
  write_assigned: 'επεξεργασία ανατεθειμένων ομάδων'
};

export function adminCapabilityName(permission: string): string {
  if (permission === '*:*') return 'Πλήρης διαχείριση';
  const [resource, action] = permission.split(':');
  return capabilityNames[resource] && actionNames[action]
    ? `${capabilityNames[resource]} — ${actionNames[action]}` : 'Πρόσθετη δυνατότητα';
}

export function isAdminRole(value: unknown): value is AdminRole {
  return typeof value === 'string' && Object.prototype.hasOwnProperty.call(ADMIN_ROLE_NAMES, value);
}

// Presentation only: every API operation still authorizes current database grants.
export function hasAdminPermission(permissions: readonly string[], permission: string): boolean {
  return permissions.includes('*:*') || permissions.includes(permission) || permissions.includes(`${permission.split(':', 1)[0]}:*`);
}

export function pendingAdminModules(permissions: readonly string[], organizationIds: readonly string[]) {
  const has = (permission: string) => hasAdminPermission(permissions, permission);
  const hasOrganization = organizationIds.some(id => id === 'lefteria_fc' || id === 'lefteria_fc_academy');
  return [
    ...(hasOrganization && (has('teams:read') || has('teams:read_assigned')) ? ['Ομάδες & παίκτες'] : []),
    ...(organizationIds.includes('lefteria_fc_academy') && (has('academy:read') || has('academy:read_assigned')) ? ['Ακαδημία'] : []),
    ...(hasOrganization && (has('training:read') || has('training:read_assigned')) ? ['Πρόγραμμα & παρουσίες'] : []),
    ...(has('news:read') ? ['Νέα ιστοσελίδας'] : []),
    ...(has('sponsors:read') ? ['Χορηγοί'] : []),
    ...(has('gallery:read') ? ['Φωτογραφίες'] : []),
    ...(has('social:write') ? ['Social Control Centre'] : []),
    ...(has('finance:read') ? ['Οικονομικά'] : []),
    ...(has('users:write') ? ['Διαχείριση λογαριασμών'] : [])
  ];
}
