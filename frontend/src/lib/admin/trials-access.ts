import type { WebAdminSession } from './controller.ts';
import { hasAdminPermission } from './permissions.ts';

// Presentation access only. This read-only panel uses the public closed-state
// projection; any future business writes must recheck DB-backed admin grants.
export function canViewFirstTeamTrials(user: WebAdminSession['user']): boolean {
  return user.organizations.some(org => org.id === 'lefteria_fc') &&
    (hasAdminPermission(user.permissions, 'settings:read') ||
     hasAdminPermission(user.permissions, 'marketing:read'));
}
