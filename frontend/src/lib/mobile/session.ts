import { isMobileRole, type MobileRole } from './navigation.ts';

const TOKEN_KEY = 'mobile_token';
const ROLES_KEY = 'mobile_available_roles';

export interface MobileSessionStorage {
  readToken(): string | null;
  write(token: string, roles: readonly MobileRole[]): void;
  clear(): void;
}

// These legacy keys preserve browser-session compatibility. Cached roles are
// display hints only: /me must validate the token and return current grants.
export function createMobileSessionStorage(storage: Pick<Storage, 'getItem' | 'setItem' | 'removeItem'> | null): MobileSessionStorage {
  return {
    readToken() {
      try { return storage?.getItem(TOKEN_KEY) || null; } catch { return null; }
    },
    write(token, roles) {
      try {
        storage?.setItem(TOKEN_KEY, token);
        storage?.setItem(ROLES_KEY, JSON.stringify(roles.filter(isMobileRole)));
      } catch {
        // Authentication remains usable in this tab when browser persistence
        // is unavailable. Remove an older saved session before continuing.
        try { storage?.removeItem(TOKEN_KEY); } catch { /* Browser storage may be blocked. */ }
        try { storage?.removeItem(ROLES_KEY); } catch { /* Remove each key independently. */ }
      }
    },
    clear() {
      try { storage?.removeItem(TOKEN_KEY); } catch { /* Browser storage may be blocked. */ }
      try { storage?.removeItem(ROLES_KEY); } catch { /* Remove each key independently. */ }
    }
  };
}

export function browserMobileSessionStorage(): MobileSessionStorage {
  try {
    return createMobileSessionStorage(typeof window === 'undefined' ? null : window.localStorage);
  } catch {
    return createMobileSessionStorage(null);
  }
}
