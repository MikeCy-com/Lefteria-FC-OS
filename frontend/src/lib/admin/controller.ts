import { writable } from 'svelte/store';
import { createApiClient } from '../api/client.ts';
import type { components } from '../api/schema';
import { isAdminRole } from './permissions.ts';

export type WebAdminSession = components['schemas']['WebAdminSessionDto'];
export type AdminRememberDays = null | 30 | 60 | 90;
export interface AdminAuthState {
  status: 'initializing' | 'anonymous' | 'authenticated' | 'unavailable';
  session: WebAdminSession | null;
  busy: boolean;
  error: string | null;
}

const UNAVAILABLE = 'Η υπηρεσία διαχείρισης δεν είναι διαθέσιμη. Δοκιμάστε ξανά.';
const EXPIRED = 'Η σύνδεσή σας έληξε. Συνδεθείτε ξανά.';
const TWO_FACTOR = 'Ο λογαριασμός απαιτεί δεύτερο παράγοντα. Η σύνδεση 2FA δεν είναι ακόμη διαθέσιμη.';
const mutation = { credentials: 'same-origin' as const, headers: { 'X-Lefteria-Admin': '1', 'Content-Type': 'application/json' } };

class AdminApiFailure extends Error {
  readonly status: number;
  readonly code: string | null;
  constructor(status: number, code: string | null) { super('Admin request failed.'); this.status = status; this.code = code; }
}

function sessionData(result: { data?: WebAdminSession; error?: unknown; response: Response }): WebAdminSession {
  if (!result.response.ok || result.data === undefined) {
    const error = result.error;
    const code = error && typeof error === 'object' && 'code' in error && typeof error.code === 'string' ? error.code : null;
    throw new AdminApiFailure(result.response.status, code);
  }
  const session = result.data;
  if (!session.user || !isAdminRole(session.user.role) || typeof session.user.id !== 'string' ||
      typeof session.user.username !== 'string' || typeof session.user.name !== 'string' ||
      !Array.isArray(session.user.permissions) || !session.user.permissions.every(p => typeof p === 'string') ||
      !Array.isArray(session.user.organizations) || !session.user.organizations.every(org =>
        (org.id === 'lefteria_fc' || org.id === 'lefteria_fc_academy') && typeof org.name === 'string') ||
      typeof session.expires_at !== 'string' || !Number.isFinite(Date.parse(session.expires_at))) {
    throw new Error('Invalid admin session response.');
  }
  return session;
}

export const ADMIN_AUTH_CONTEXT = Symbol('web-admin-auth');

export function createAdminAuthController(options: { baseUrl: string; fetch?: typeof globalThis.fetch; now?: () => number }) {
  const api = createApiClient(options);
  const now = options.now ?? Date.now;
  let state: AdminAuthState = { status: 'initializing', session: null, busy: false, error: null };
  const store = writable(state);
  let revision = 0;
  let disposed = false;

  function update(patch: Partial<AdminAuthState>) {
    if (disposed) return;
    state = { ...state, ...patch };
    store.set(state);
  }

  function accept(session: WebAdminSession) {
    if (Date.parse(session.expires_at) <= now()) throw new AdminApiFailure(401, 'session_expired');
    update({ status: 'authenticated', session, error: null });
  }

  async function bootstrap() {
    if (disposed || state.busy) return false;
    const current = ++revision;
    update({ status: 'initializing', session: null, busy: true, error: null });
    try {
      const result = await api.GET('/api/admin/auth/me', { credentials: 'same-origin' });
      if (current !== revision || disposed) return false;
      accept(sessionData(result));
      return true;
    } catch (error) {
      if (current !== revision || disposed) return false;
      update({ status: error instanceof AdminApiFailure && error.status === 401 ? 'anonymous' : 'unavailable', session: null,
        error: error instanceof AdminApiFailure && error.status === 401 ? null : UNAVAILABLE });
      return false;
    } finally { if (current === revision) update({ busy: false }); }
  }

  async function login(username: string, password: string, rememberDays: AdminRememberDays) {
    if (disposed || state.busy) return false;
    const current = ++revision;
    update({ status: 'anonymous', session: null, busy: true, error: null });
    try {
      const result = await api.POST('/api/admin/auth/login', { ...mutation, body: { username, password, remember_days: rememberDays } });
      if (current !== revision || disposed) return false;
      accept(sessionData(result));
      return true;
    } catch (error) {
      if (current !== revision || disposed) return false;
      update({ status: 'anonymous', session: null, error:
        error instanceof AdminApiFailure && error.code === 'two_factor_unavailable' ? TWO_FACTOR :
        error instanceof AdminApiFailure && error.status === 401 ? 'Λάθος όνομα χρήστη ή κωδικός.' :
        error instanceof AdminApiFailure && error.status === 429 ? 'Πολλές προσπάθειες. Δοκιμάστε ξανά σε λίγο.' : UNAVAILABLE });
      return false;
    } finally { if (current === revision) update({ busy: false }); }
  }

  async function logout() {
    if (disposed) return false;
    const current = ++revision;
    // Invalidate pending reads immediately; a late response cannot restore access.
    update({ status: 'initializing', session: null, busy: true, error: null });
    try {
      const result = await api.POST('/api/admin/auth/logout', mutation);
      if (current !== revision || disposed) return false;
      if (result.response.status !== 204 && result.response.status !== 401) throw new Error('Logout not confirmed.');
      update({ status: 'anonymous', session: null, error: null });
      return true;
    } catch {
      if (current !== revision || disposed) return false;
      update({ status: 'unavailable', session: null, error: 'Δεν επιβεβαιώθηκε η αποσύνδεση. Δοκιμάστε ξανά.' });
      return false;
    } finally { if (current === revision) update({ busy: false }); }
  }

  function expireIfNeeded() {
    if (state.session && Date.parse(state.session.expires_at) <= now()) {
      ++revision;
      update({ status: 'anonymous', session: null, busy: false, error: EXPIRED });
    }
  }

  return { subscribe: store.subscribe, getState: () => state, bootstrap, login, logout, expireIfNeeded,
    dispose() { ++revision; disposed = true; state = { status: 'anonymous', session: null, busy: false, error: null }; store.set(state); } };
}

export type AdminAuthController = ReturnType<typeof createAdminAuthController>;
