/**
 * Web-admin organization context.
 *
 * This is a navigation preference, NOT an authorization grant. The API must
 * continue to verify the authenticated cookie, capability and organization
 * ownership on every read/write. Never use this preference on public pages or
 * in the mobile member app.
 */
export type AdminOrganizationId = 'lefteria_fc' | 'lefteria_fc_academy';
export type AdminOrganizationUser = {
  id: string;
  role: string;
  organizations: readonly { id: string; name: string }[];
};
export type AdminOrganizationStorage = Pick<Storage, 'getItem' | 'setItem'>;

const validId = (id: unknown): id is AdminOrganizationId =>
  id === 'lefteria_fc' || id === 'lefteria_fc_academy';

export const adminOrganizationKey = (user: AdminOrganizationUser): string =>
  'lefteria:os:web-admin:organization:v1:' + user.id;

/** Read only genuine, assigned context. Missing or tampered values fall back
 * to the first authorized organization, not to an arbitrary persisted value.
 */
export function preferredAdminOrganization(
  user: AdminOrganizationUser | null,
  storage?: AdminOrganizationStorage | null
): AdminOrganizationId | null {
  if (!user) return null;
  const allowed = user.organizations.map(org => org.id).filter(validId);
  if (!allowed.length) return null;
  let persisted: string | null = null;
  try {
    persisted = (storage ?? (typeof window === 'undefined' ? null : window.localStorage))?.getItem(adminOrganizationKey(user)) ?? null;
  } catch { /* browser storage may be unavailable; grants still apply */ }
  return validId(persisted) && allowed.includes(persisted) ? persisted
    : allowed.includes('lefteria_fc') ? 'lefteria_fc' : allowed[0];
}

/** Restrict the current global context further by this page's capabilities.
 * A club selection must NOT silently turn into academy just because an admin
 * opened an academy-only deep link.
 */
export function scopedAdminOrganization(
  user: AdminOrganizationUser | null,
  permitted: readonly string[],
  storage?: AdminOrganizationStorage | null
): AdminOrganizationId | null {
  const selected = preferredAdminOrganization(user, storage);
  return selected && permitted.includes(selected) ? selected : null;
}

/** Persist the preference for this account only, after assignment checks. */
export function chooseAdminOrganization(
  user: AdminOrganizationUser,
  selection: string,
  storage?: AdminOrganizationStorage | null
): boolean {
  if (!validId(selection) || !user.organizations.some(org => org.id === selection)) return false;
  try {
    const target = storage ?? (typeof window === 'undefined' ? null : window.localStorage);
    if (!target) return false;
    target.setItem(adminOrganizationKey(user), selection);
    return true;
  } catch { return false; }
}
