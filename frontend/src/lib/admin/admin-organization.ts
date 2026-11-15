/**
 * Admin organization scope is determined by current session grants and role
 * permissions. There is deliberately NO global organization preference:
 * a user can see all permitted modules without switching the whole dashboard.
 *
 * Every API still independently authorizes cookie, role/capability, exact
 * organization and (for coaches) assignment to the specific team/group.
 */
export type AdminOrganizationId = 'lefteria_fc' | 'lefteria_fc_academy';
export type AdminOrganizationUser = {
  id: string;
  role: string;
  organizations: readonly { id: string; name: string }[];
};

const validId = (id: unknown): id is AdminOrganizationId =>
  id === 'lefteria_fc' || id === 'lefteria_fc_academy';

export function adminOrganizationsFor(
  user: AdminOrganizationUser | null,
  permitted?: readonly string[]
): AdminOrganizationId[] {
  if (!user) return [];
  const granted = user.organizations.map(org => org.id).filter(validId);
  return granted.filter((id, index) =>
    granted.indexOf(id) === index && (permitted === undefined || permitted.includes(id)));
}

/**
 * Default for legacy, single-owner data requests. Never treat this as a
 * permission or an admin-wide selection. Multi-owner workflows should present
 * their own destination filter, or render both authorized owners.
 */
export function preferredAdminOrganization(user: AdminOrganizationUser | null): AdminOrganizationId | null {
  return adminOrganizationsFor(user)[0] ?? null;
}

export function scopedAdminOrganization(
  user: AdminOrganizationUser | null,
  permitted: readonly string[]
): AdminOrganizationId | null {
  return adminOrganizationsFor(user, permitted)[0] ?? null;
}
