import { writable } from 'svelte/store';
import { createApiClient } from '../api/client.ts';
import type { components } from '../api/schema';
import { isMobileRole, type MobileRole } from './navigation.ts';
import type { FanRegistrationForm, MobileIdentityOption, MobileLoginStep, RememberDays } from './components/component-types.ts';
import type { MobileSessionStorage } from './session.ts';

type User = components['schemas']['MobileUserDto'];
type Organization = components['schemas']['MobileOrganizationContextDto'];
type Verification = components['schemas']['MobileVerificationResult'];
type LoginResult = components['schemas']['MobileVerificationResultMobileLoginResult'];
export type FavoriteTeam = Exclude<components['schemas']['MobileProfilePreferenceBody']['favorite_team'], undefined>;
export type MobileProfileUpdate = components['schemas']['MobileProfileUpdateBody'];

export interface MobileAuthState {
  status: 'initializing' | 'anonymous' | 'authenticated' | 'unavailable';
  busy: boolean;
  error: string | null;
  step: MobileLoginStep;
  phone: string;
  detectedRole: MobileRole | null;
  identities: readonly MobileIdentityOption[];
  user: User | null;
  availableRoles: readonly MobileRole[];
  organization: Organization | null;
  organizationError: string | null;
  resendAvailableAt: number;
}

class ApiFailure extends Error {
  readonly status: number;
  constructor(status: number, message: string) { super(message); this.status = status; }
}

function responseData<T>(result: { data?: T; error?: unknown; response: Response }): T {
  if (result.data !== undefined && result.response.ok) return result.data;
  const error = result.error;
  const detail = error && typeof error === 'object' && 'detail' in error && typeof error.detail === 'string'
    ? error.detail : 'Η υπηρεσία δεν είναι διαθέσιμη. Δοκιμάστε ξανά.';
  throw new ApiFailure(result.response.status, detail);
}

function validRoles(roles: readonly string[] | null | undefined): MobileRole[] {
  return [...new Set((roles ?? []).filter(isMobileRole))];
}

export const MOBILE_AUTH_CONTEXT = Symbol('mobile-auth-controller');

export function createMobileAuthController(options: {
  baseUrl: string;
  storage: MobileSessionStorage;
  fetch?: typeof globalThis.fetch;
  now?: () => number;
}) {
  const api = createApiClient(options);
  const now = options.now ?? Date.now;
  let state: MobileAuthState = {
    status: 'initializing', busy: false, error: null, step: 'phone', phone: '',
    detectedRole: null, identities: [], user: null, availableRoles: [], organization: null,
    organizationError: null, resendAvailableAt: 0
  };
  const store = writable(state);
  let token: string | null = null;
  let pendingCode: string | null = null;
  let revision = 0;

  function update(patch: Partial<MobileAuthState>) {
    state = { ...state, ...patch };
    store.set(state);
  }

  function anonymous(error: string | null = null) {
    token = null;
    pendingCode = null;
    options.storage.clear();
    update({ status: 'anonymous', user: null, availableRoles: [], organization: null,
      organizationError: null, identities: [], detectedRole: null, step: 'phone', error });
  }

  function authorization() {
    if (!token) throw new ApiFailure(401, 'Απαιτείται σύνδεση.');
    return { Authorization: `Bearer ${token}` };
  }

  function rememberValid(value: RememberDays) {
    return value === null || value === 30 || value === 60 || value === 90;
  }

  function applyUser(user: User, behavior: { preserveOrganization?: boolean } = {}) {
    if (!isMobileRole(user.role) || typeof user.id !== 'string' || typeof user.name !== 'string' ||
        typeof user.current_role_available !== 'boolean') throw new Error('Invalid identity response');
    const roles = validRoles(user.available_roles);
    update({ status: 'authenticated', user, availableRoles: roles, error: null,
      ...(behavior.preserveOrganization && user.current_role_available ? {} : { organization: null, organizationError: null }),
      identities: [], detectedRole: null, phone: user.phone });
    if (token) options.storage.write(token, roles);
  }

  function applyOwnProfile(user: User, original: User) {
    if (user.id !== original.id || user.role !== original.role || user.phone !== original.phone) {
      throw new Error('Invalid profile response identity');
    }
    applyUser(user, { preserveOrganization: true });
  }

  async function organizationContext(headers: { Authorization: string }, operationRevision: number) {
    try {
      const data = responseData(await api.GET('/api/mobile/management/organization', { headers }));
      if (revision === operationRevision) update({ organization: data, organizationError: null });
    } catch (error) {
      if (revision !== operationRevision) return;
      if (error instanceof ApiFailure && error.status === 401) { anonymous(error.message); return; }
      update({ organization: null, organizationError: message(error) });
    }
  }

  function message(error: unknown) {
    return error instanceof ApiFailure ? error.message : 'Δεν ήταν δυνατή η επικοινωνία με την υπηρεσία. Δοκιμάστε ξανά.';
  }

  async function action(work: (operationRevision: number) => Promise<void>, authenticated = false,
    failureScope: 'general' | 'organization' = 'general'): Promise<boolean> {
    if (state.busy) return false;
    const operationRevision = ++revision;
    update({ busy: true, error: null });
    try {
      await work(operationRevision);
      return revision === operationRevision;
    } catch (error) {
      if (revision !== operationRevision) return false;
      if (authenticated && error instanceof ApiFailure && error.status === 401) anonymous(error.message);
      else {
        const detail = message(error);
        update({ error: detail, ...(failureScope === 'organization' ? { organizationError: detail } : {}) });
      }
      return false;
    } finally {
      if (revision === operationRevision) update({ busy: false });
    }
  }

  async function finishLogin(data: LoginResult, operationRevision: number) {
    if (revision !== operationRevision) return;
    if (!data.token || !isMobileRole(data.role) || data.user.role !== data.role) throw new Error('Invalid login response');
    token = data.token;
    pendingCode = null;
    applyUser(data.user);
    if (data.role === 'management' && data.user.current_role_available) await organizationContext(authorization(), operationRevision);
  }

  async function verify(code: string, rememberDays: RememberDays, role?: MobileRole) {
    return action(async (operationRevision) => {
      if (!/^[0-9]{6}$/.test(code) || !rememberValid(rememberDays)) throw new Error('Invalid verification input');
      const body = { phone: state.phone, code, remember_days: rememberDays, ...(role ? { role } : {}) };
      const data: Verification = responseData(await api.POST('/api/mobile/auth/verify-otp', { body }));
      if (revision !== operationRevision) return;
      if ('multi_role' in data && data.multi_role === true && 'identities' in data) {
        const identities = data.identities.filter((identity): identity is MobileIdentityOption => isMobileRole(identity.role));
        if (!identities.length) throw new Error('Invalid identity response');
        pendingCode = code;
        update({ step: 'choose_role', identities, phone: data.phone, detectedRole: null });
      } else if ('token' in data && 'user' in data) {
        await finishLogin(data, operationRevision);
      } else throw new Error('Invalid verification response');
    });
  }

  return {
    subscribe: store.subscribe,
    getState: () => state,
    async bootstrap() {
      return action(async (operationRevision) => {
        token = options.storage.readToken();
        if (!token) { anonymous(); return; }
        try {
          const user = responseData(await api.GET('/api/mobile/auth/me', { headers: authorization() }));
          if (revision !== operationRevision) return;
          applyUser(user);
          if (user.role === 'management' && user.current_role_available) await organizationContext(authorization(), operationRevision);
        } catch (error) {
          if (revision !== operationRevision) return;
          if (error instanceof ApiFailure && error.status === 401) anonymous(error.message);
          else update({ status: 'unavailable', error: message(error), user: null, organization: null, availableRoles: [] });
        }
      });
    },
    async requestOtp(phone: string) {
      return action(async (operationRevision) => {
        const data = responseData(await api.POST('/api/mobile/auth/request-otp', { body: { phone } }));
        if (revision !== operationRevision) return;
        pendingCode = null;
        update({ status: 'anonymous', step: 'otp', phone: data.phone, identities: [],
          detectedRole: isMobileRole(data.role_detected) ? data.role_detected : null, resendAvailableAt: now() + 60_000 });
      });
    },
    verifyOtp: verify,
    async chooseRole(role: MobileRole, rememberDays: RememberDays) {
      if (!pendingCode || !state.identities.some((identity) => identity.role === role)) return false;
      return verify(pendingCode, rememberDays, role);
    },
    async registerFan(registration: FanRegistrationForm) {
      return action(async (operationRevision) => {
        const data = responseData(await api.POST('/api/mobile/auth/register-fan', { body: {
          phone: registration.phone, first_name: registration.firstName, last_name: registration.lastName,
          birthday: registration.birthday, name: null
        } }));
        if (revision !== operationRevision) return;
        pendingCode = null;
        update({ status: 'anonymous', step: 'otp', phone: data.phone, detectedRole: 'fan', identities: [], resendAvailableAt: now() + 60_000 });
      });
    },
    changeStep(step: MobileLoginStep) {
      if (state.busy) return;
      if (step === 'choose_role') return;
      pendingCode = null;
      update({ step, error: null, identities: [] });
    },
    async switchRole(role: MobileRole) {
      if (!state.user || !isMobileRole(role) ||
          (!state.user.current_role_available && role !== 'fan')) return false;
      return action(async (operationRevision) => {
        update({ organization: null, organizationError: null });
        const data = responseData(await api.POST('/api/mobile/auth/switch-role', { headers: authorization(), body: { role } }));
        if (revision !== operationRevision) return;
        if (data.noop === true && data.token === null) {
          const user = responseData(await api.GET('/api/mobile/auth/me', { headers: authorization() }));
          if (revision !== operationRevision) return;
          applyUser(user);
        } else {
          if (!data.token || !data.user || data.role !== role || data.user.role !== role) throw new Error('Invalid role response');
          token = data.token;
          applyUser(data.user);
        }
        if (state.user?.role === 'management' && state.user.current_role_available) await organizationContext(authorization(), operationRevision);
      }, true, state.user.role === 'management' ? 'organization' : 'general');
    },
    async selectOrganization(organizationId: string) {
      if (state.user?.role !== 'management' || !state.user.current_role_available) return false;
      return action(async (operationRevision) => {
        update({ organization: null, organizationError: null });
        const data = responseData(await api.POST('/api/mobile/management/organization', {
          headers: authorization(), body: { organization_id: organizationId }
        }));
        if (revision !== operationRevision) return;
        update({ organization: data });
      }, true, 'organization');
    },
    async refreshOrganization() {
      if (state.user?.role !== 'management' || !state.user.current_role_available) return false;
      return action(async (operationRevision) => {
        update({ organization: null, organizationError: null });
        await organizationContext(authorization(), operationRevision);
      }, true);
    },
    async updateFavoriteTeam(favoriteTeam: FavoriteTeam) {
      const identity = state.user;
      if (state.status !== 'authenticated' || !identity?.current_role_available) return false;
      return action(async (operationRevision) => {
        if (favoriteTeam !== null && favoriteTeam !== 'first_team' && favoriteTeam !== 'academy' && favoriteTeam !== 'both') {
          throw new Error('Invalid team preference');
        }
        const data = responseData(await api.PUT('/api/mobile/auth/me', {
          headers: authorization(), body: { favorite_team: favoriteTeam }
        }));
        if (revision !== operationRevision) return;
        if (data.ok !== true) throw new Error('Invalid preference response');
        applyOwnProfile(data.user, identity);
      }, true);
    },
    async updateProfile(changes: MobileProfileUpdate) {
      const identity = state.user;
      if (state.status !== 'authenticated' || !identity?.current_role_available) return false;
      return action(async (operationRevision) => {
        const body: MobileProfileUpdate = {
          ...(changes.name != null ? { name: changes.name } : {}),
          ...(changes.email != null ? { email: changes.email } : {}),
          ...(changes.first_name != null ? { first_name: changes.first_name } : {}),
          ...(changes.last_name != null ? { last_name: changes.last_name } : {}),
          ...(changes.birthday != null ? { birthday: changes.birthday } : {}),
          ...(changes.favourite_player_id != null ? { favourite_player_id: changes.favourite_player_id } : {}),
          ...(changes.notification_prefs != null ? { notification_prefs: changes.notification_prefs } : {})
        };
        const user = responseData(await api.PUT('/api/mobile/profile', { headers: authorization(), body }));
        if (revision !== operationRevision) return;
        applyOwnProfile(user, identity);
      }, true);
    },
    logout() {
      ++revision;
      anonymous();
      update({ busy: false });
    },
    dispose() { ++revision; pendingCode = null; }
  };
}

export type MobileAuthController = ReturnType<typeof createMobileAuthController>;
