import { writable } from 'svelte/store';
import {validChatDirectory,validMessage,validPage,validRoom,validTextMessage,type GroupChatRoom,type GroupChatMessage,type GroupChatPage} from './group-chat.ts';
import { createApiClient } from '../api/client.ts';
import type { components } from '../api/schema';
import { isMobileRole, type MobileRole } from './navigation.ts';
import type { FanRegistrationForm, MobileIdentityOption, MobileLoginStep, RememberDays } from './components/component-types.ts';
import type { MobileSessionStorage } from './session.ts';
import { calendarItemKey, isAvailabilityStatus, isCalendarDate, isCalendarKind, isCalendarMonth } from './calendar.ts';
import type { AvailabilityStatus, CalendarLoadStatus, MobileCalendar, MobileCalendarItem, MobileCalendarResponses } from './calendar.ts';

type User = components['schemas']['MobileUserDto'];
type Organization = components['schemas']['MobileOrganizationContextDto'];
type Verification = components['schemas']['MobileVerificationResult'];
type LoginResult = components['schemas']['MobileVerificationResultMobileLoginResult'];
type Family = components['schemas']['ParentFamilyResult'];
type ParentActiveDelegations = components['schemas']['ParentActiveDelegationsResult'];
type OutgoingInvitations = components['schemas']['ParentOutgoingInvitationsResult'];
type RecipientInvitations = components['schemas']['RecipientPendingInvitationsResult'];
type DelegationAvailability = components['schemas']['FamilyDelegationAvailabilityDto'];
export type FavoriteTeam = Exclude<components['schemas']['MobileProfilePreferenceBody']['favorite_team'], undefined>;
export type MobileProfileUpdate = components['schemas']['MobileProfileUpdateBody'];
export type MobilePlayerOption = components['schemas']['MobilePlayerOptionDto'];
export type MobilePlayerOptionsStatus = 'idle' | 'loading' | 'ready' | 'error';
export const MAX_AVATAR_FILE_BYTES = 5 * 1024 * 1024;
export const AVATAR_FILE_TYPES = ['image/jpeg', 'image/png', 'image/webp', 'image/gif'] as const;

export function avatarFileError(file: File): string | null {
  if (!AVATAR_FILE_TYPES.some((type) => type === file.type)) return 'Επίλεξε φωτογραφία JPEG, PNG, WebP ή GIF.';
  if (file.size === 0) return 'Το αρχείο φωτογραφίας είναι κενό.';
  if (file.size > MAX_AVATAR_FILE_BYTES) return 'Η φωτογραφία πρέπει να είναι έως 5 MB.';
  return null;
}

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
  playerOptions: readonly MobilePlayerOption[];
  playerOptionsStatus: MobilePlayerOptionsStatus;
  playerOptionsError: string | null;
  family: Family | null;
  familyStatus: 'idle' | 'loading' | 'ready' | 'error';
  familyError: string | null;
  activeFamilyDelegations: ParentActiveDelegations | null;
  activeFamilyStatus: 'idle' | 'loading' | 'ready' | 'error';
  activeFamilyError: string | null;
  familyRevokingPlayerId: string | null;
  familyRevokeError: string | null;
  familyRevokeSuccess: string | null;
  outgoingInvitations: OutgoingInvitations | null;
  outgoingStatus: 'idle' | 'loading' | 'ready' | 'error';
  outgoingError: string | null;
  outgoingCancellingId: string | null;
  outgoingCancelError: string | null;
  recipientInvitations: RecipientInvitations | null;
  delegationAvailability: DelegationAvailability | null;
  delegationStatus: 'idle' | 'loading' | 'ready' | 'error';
  delegationError: string | null;
  delegatedCalendar: MobileCalendar | null;
  delegatedCalendarStatus: CalendarLoadStatus;
  delegatedCalendarError: string | null;
  delegatedCalendarLoadingMore: boolean;
  delegatedRespondingKey: string | null;
  delegatedRespondError: string | null;
  delegatedRespondSuccess: string | null;
  recipientStatus: 'idle' | 'loading' | 'ready' | 'error';
  recipientError: string | null;
  recipientDecliningId: string | null;
  recipientAcceptingId: string | null;
  recipientAcceptError: string | null;
  recipientAcceptSuccess: string | null;
  recipientDeclineError: string | null;
  calendar: MobileCalendar | null;
  calendarStatus: CalendarLoadStatus;
  calendarError: string | null;
  calendarLoadingMore: boolean;
  calendarResponses: MobileCalendarResponses | null;
  calendarResponsesKey: string | null;
  calendarResponsesStatus: CalendarLoadStatus;
  calendarResponsesError: string | null;
  calendarResponsesLoadingMore: boolean;
  resendAvailableAt: number;
}

class ApiFailure extends Error {
  readonly status: number;
  readonly code: string | null;
  constructor(status: number, message: string, code: string | null = null) { super(message); this.status = status; this.code = code; }
}

function responseData<T>(result: { data?: T; error?: unknown; response: Response }): T {
  if (result.data !== undefined && result.response.ok) return result.data;
  const error = result.error;
  const detail = error && typeof error === 'object' && 'detail' in error && typeof error.detail === 'string'
    ? error.detail : 'Η υπηρεσία δεν είναι διαθέσιμη. Δοκιμάστε ξανά.';
  const code = error && typeof error === 'object' && 'code' in error && typeof error.code === 'string' ? error.code : null;
  throw new ApiFailure(result.response.status, detail, code);
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
    organizationError: null, playerOptions: [], playerOptionsStatus: 'idle', playerOptionsError: null,
    family: null, familyStatus: 'idle', familyError: null,
    activeFamilyDelegations: null, activeFamilyStatus: 'idle', activeFamilyError: null,
    familyRevokingPlayerId: null, familyRevokeError: null, familyRevokeSuccess: null,
    outgoingInvitations: null, outgoingStatus: 'idle', outgoingError: null,
    outgoingCancellingId: null, outgoingCancelError: null,
    recipientInvitations: null, recipientStatus: 'idle', recipientError: null,
    delegationAvailability: null, delegationStatus: 'idle', delegationError: null,
    delegatedCalendar: null, delegatedCalendarStatus: 'idle',
    delegatedCalendarError: null, delegatedCalendarLoadingMore: false,
    delegatedRespondingKey: null, delegatedRespondError: null, delegatedRespondSuccess: null,
    recipientDecliningId: null, recipientDeclineError: null,
    recipientAcceptingId: null, recipientAcceptError: null, recipientAcceptSuccess: null,
    calendar: null, calendarStatus: 'idle', calendarError: null, calendarLoadingMore: false,
    calendarResponses: null, calendarResponsesKey: null, calendarResponsesStatus: 'idle',
    calendarResponsesError: null, calendarResponsesLoadingMore: false, resendAvailableAt: 0
  };
  const store = writable(state);
  let token: string | null = null;
  let pendingCode: string | null = null;
  let revision = 0;
  let playerOptionsRevision = 0;
  let familyRevision = 0;
  let activeFamilyRevision = 0;
  let outgoingRevision = 0;
  let recipientRevision = 0;
  let delegationRevision = 0;
  let delegatedCalendarRevision = 0;
  let calendarRevision = 0;
  let calendarResponsesRevision = 0;

  function update(patch: Partial<MobileAuthState>) {
    state = { ...state, ...patch };
    store.set(state);
  }

  function clearPlayerOptions() {
    ++playerOptionsRevision;
    update({ playerOptions: [], playerOptionsStatus: 'idle', playerOptionsError: null });
  }

  function clearOutgoingInvitations() {
    ++outgoingRevision;
    update({ outgoingInvitations: null, outgoingStatus: 'idle', outgoingError: null,
      outgoingCancellingId: null, outgoingCancelError: null });
  }

  function clearDelegatedCalendar() {
    ++delegatedCalendarRevision;
    update({delegatedCalendar:null,delegatedCalendarStatus:'idle',
      delegatedCalendarError:null,delegatedCalendarLoadingMore:false,
      delegatedRespondingKey:null,delegatedRespondError:null,delegatedRespondSuccess:null});
  }

  function clearRecipientInvitations() {
    clearDelegatedCalendar();
    ++recipientRevision;
    ++delegationRevision;
    update({ recipientInvitations: null, recipientStatus: 'idle', recipientError: null,
      recipientDecliningId: null, recipientDeclineError: null,
      recipientAcceptingId: null, recipientAcceptError: null, recipientAcceptSuccess: null,
      delegationAvailability: null, delegationStatus: 'idle', delegationError: null });
  }

  function clearFamily() {
    ++familyRevision;
    ++activeFamilyRevision;
    clearOutgoingInvitations();
    update({ family: null, familyStatus: 'idle', familyError: null,
      familyRevokingPlayerId: null, familyRevokeError: null, familyRevokeSuccess: null,
      activeFamilyDelegations: null, activeFamilyStatus: 'idle', activeFamilyError: null });
  }

  function clearCalendarResponses() {
    ++calendarResponsesRevision;
    update({ calendarResponses: null, calendarResponsesKey: null, calendarResponsesStatus: 'idle',
      calendarResponsesError: null, calendarResponsesLoadingMore: false });
  }

  function clearCalendar() {
    ++calendarRevision;
    clearCalendarResponses();
    update({ calendar: null, calendarStatus: 'idle', calendarError: null, calendarLoadingMore: false });
  }

  function denyCalendarAccess(error: ApiFailure) {
    clearFamily();
    // A current authorization denial invalidates earlier pages too: a child or
    // assignment may have been removed while another role grant remains active.
    clearCalendar();
    update({ calendarStatus: 'error', calendarError: error.message });
  }

  function anonymous(error: string | null = null) {
    clearPlayerOptions();
    clearCalendar();
    clearFamily();
    clearRecipientInvitations();
    token = null;
    pendingCode = null;
    options.storage.clear();
    update({ status: 'anonymous', user: null, availableRoles: [], organization: null,
      organizationError: null, identities: [], detectedRole: null, step: 'phone', error });
  }

  function endSession(error: string | null = null) {
    ++revision;
    anonymous(error);
    update({ busy: false });
  }

  function revokeCurrentRole(error: string) {
    ++revision;
    if (state.user) applyUser({ ...state.user, current_role_available: false });
    update({ busy: false, error });
  }

  function authorization() {
    if (!token) throw new ApiFailure(401, 'Απαιτείται σύνδεση.');
    return { Authorization: `Bearer ${token}` };
  }

  async function chatRequest(path:string,content?:string):Promise<unknown> {
    const current=state.user;
    if(state.status!=='authenticated' || !current?.current_role_available || !token)
      throw new ApiFailure(401,'Απαιτείται ενεργή σύνδεση.');
    const sessionToken=token, opRevision=revision;
    const reply=await (options.fetch??globalThis.fetch)(options.baseUrl+path,{
      method:content===undefined?'GET':'POST',cache:'no-store',
      headers:{...authorization(),...(content===undefined?{}:{'Content-Type':'application/json'})},
      ...(content===undefined?{}:{body:JSON.stringify({content})})
    });
    if(revision!==opRevision || token!==sessionToken ||
       state.user?.id!==current.id || state.user?.role!==current.role ||
       !state.user?.current_role_available)
      throw new ApiFailure(401,'Η συνεδρία άλλαξε. Ανανέωσε τα μηνύματα.');
    if(!reply.ok){
      if(reply.status===401)endSession('Η συνεδρία έληξε.');
      const payload:unknown=await reply.json().catch(()=>null);
      const detail=payload&&typeof payload==='object'&&'detail' in payload&&
        typeof payload.detail==='string'?payload.detail:'Η υπηρεσία μηνυμάτων δεν είναι διαθέσιμη.';
      throw new ApiFailure(reply.status,detail);
    }
    return reply.json();
  }
  function chatRoomPath(room:GroupChatRoom):string {
    if(!validRoom(room))throw new Error('Invalid chat room.');
    return '/api/mobile/chat/groups/'+encodeURIComponent(room.organization_id)+'/'+encodeURIComponent(room.group_id)+'/messages';
  }

  function rememberValid(value: RememberDays) {
    return value === null || value === 30 || value === 60 || value === 90;
  }

  function validateUser(user: User) {
    if (!user || !isMobileRole(user.role) || typeof user.id !== 'string' || !user.id.trim() ||
        typeof user.phone !== 'string' || !user.phone.trim() || typeof user.name !== 'string' || !user.name.trim() ||
        typeof user.current_role_available !== 'boolean') throw new Error('Invalid identity response');
  }

  function applyUser(user: User, behavior: { preserveOrganization?: boolean } = {}) {
    validateUser(user);
    const roles = validRoles(user.available_roles);
    if (!user.current_role_available || state.user?.id !== user.id || state.user?.role !== user.role || state.user?.phone !== user.phone) {
      clearPlayerOptions();
      clearCalendar();
    }
    clearFamily();
    clearRecipientInvitations();
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
      else if (authenticated && error instanceof ApiFailure && error.status === 403 && error.code === 'account_unavailable') revokeCurrentRole(error.message);
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
    if (!data || typeof data.token !== 'string' || !data.token.trim() || !isMobileRole(data.role) || !data.user ||
        data.user.role !== data.role || !Array.isArray(data.available_roles) || !data.available_roles.every(isMobileRole)) throw new Error('Invalid login response');
    validateUser(data.user);
    const roles = validRoles(data.available_roles);
    const userRoles = data.user.available_roles;
    if ((data.user.current_role_available && !roles.includes(data.role)) ||
        (userRoles !== undefined && (!Array.isArray(userRoles) || !userRoles.every(isMobileRole) ||
          validRoles(userRoles).length !== roles.length || roles.some((role) => !userRoles.includes(role))))) throw new Error('Invalid login roles');
    token = data.token;
    pendingCode = null;
    applyUser({ ...data.user, available_roles: roles });
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

  function calendarIdentity() {
    const identity = state.user;
    if (state.status !== 'authenticated' || !identity?.current_role_available || identity.role === 'fan' ||
        (identity.role === 'management' && (!state.organization?.organization_id || state.organization.requires_organization_selection))) return null;
    return identity;
  }

  function calendarScopeCurrent(identity: User, sessionToken: string | null, organizationId: string | null | undefined) {
    return token === sessionToken && state.status === 'authenticated' && state.user?.id === identity.id &&
      state.user.role === identity.role && state.user.phone === identity.phone && state.user.current_role_available &&
      (identity.role !== 'management' || state.organization?.organization_id === organizationId);
  }

  function validOffset(value: unknown, offset: number) {
    return value === null || (typeof value === 'number' && Number.isInteger(value) && value > offset);
  }

  function validCalendar(data: MobileCalendar, identity: User, month: string, playerId: string | undefined, offset: number) {
    if (!data || data.identity_id !== identity.id || data.role !== identity.role || data.month !== month ||
        typeof data.time_zone !== 'string' || !Array.isArray(data.players) || !Array.isArray(data.items) ||
        !validOffset(data.next_offset, offset)) return false;
    try { new Intl.DateTimeFormat('el-GR', { timeZone: data.time_zone }); } catch { return false; }
    const playerIds = new Set<string>();
    if (!data.players.every((player) => player && typeof player.id === 'string' && !!player.id.trim() &&
      typeof player.name === 'string' && typeof player.organization_id === 'string' && !!player.organization_id.trim() &&
      !playerIds.has(player.id) && !!playerIds.add(player.id))) return false;
    if (identity.role === 'parent' || identity.role === 'player') {
      if (playerId && data.selected_player_id !== playerId) return false;
      const selected = data.players.find((player) => player.id === data.selected_player_id);
      // An identity without any current player links may receive a verified empty calendar.
      if (!selected && (data.players.length > 0 || data.selected_player_id !== null || data.items.length > 0)) return false;
      if (selected && (selected.organization_id !== data.organization_id || (playerId && selected.id !== playerId))) return false;
      if (identity.role === 'player' && data.players.length > 1) return false;
    } else if (data.selected_player_id !== null || data.players.length !== 0) return false;
    if (identity.role === 'coach' && data.organization_id !== null) return false;
    if (identity.role === 'management' && data.organization_id !== state.organization?.organization_id) return false;
    if (data.organization_id !== null && (typeof data.organization_id !== 'string' || !data.organization_id.trim())) return false;
    const optionalText = ['start_time', 'starts_at', 'ends_at', 'arrival_time', 'location', 'location_url', 'description',
      'member_summary', 'session_journey', 'preparation', 'status', 'cancel_reason', 'content_status'] as const;
    return data.items.every((item) => item && typeof item.id === 'string' && !!item.id.trim() && isCalendarKind(item.kind) &&
      typeof item.event_type === 'string' && typeof item.organization_id === 'string' && !!item.organization_id.trim() &&
      (data.organization_id === null || item.organization_id === data.organization_id) && typeof item.title === 'string' &&
      isCalendarDate(item.date) && isCalendarDate(item.end_date) && item.end_date >= item.date &&
      item.date <= `${month}-31` && item.end_date >= `${month}-01` && typeof item.is_cancelled === 'boolean' &&
      optionalText.every((field) => item[field] == null || typeof item[field] === 'string') &&
      (item.duration_minutes == null || (typeof item.duration_minutes === 'number' && Number.isInteger(item.duration_minutes) && item.duration_minutes >= 0)) &&
      Array.isArray(item.responses) && item.responses.length <= 1 && item.responses.every((response) =>
        response && response.player_id === data.selected_player_id && isAvailabilityStatus(response.status) &&
        (response.updated_at == null || typeof response.updated_at === 'string')));
  }

  async function loadCalendar(month: string, playerId?: string, append = false) {
    const identity = calendarIdentity();
    if (state.busy || !identity || !isCalendarMonth(month) || (playerId !== undefined && !playerId.trim())) return false;
    const previous = state.calendar;
    const offset = append ? previous?.next_offset : 0;
    if (append && (state.calendarLoadingMore || state.calendarStatus !== 'ready' || previous?.month !== month ||
        previous.selected_player_id !== (playerId ?? null) || offset == null)) return false;
    if (typeof offset !== 'number' || !Number.isInteger(offset) || offset < 0) return false;
    const operationRevision = ++calendarRevision;
    const sessionToken = token;
    const organizationId = state.organization?.organization_id;
    const isCurrent = () => calendarRevision === operationRevision && calendarScopeCurrent(identity, sessionToken, organizationId);
    clearCalendarResponses();
    update(append ? { calendarLoadingMore: true, calendarError: null }
      : { calendar: null, calendarStatus: 'loading', calendarError: null, calendarLoadingMore: false });
    try {
      const data = responseData(await api.GET('/api/mobile/calendar', { headers: authorization(),
        params: { query: { month, offset: String(offset), ...(playerId ? { player_id: playerId } : {}) } } }));
      if (!isCurrent()) return false;
      if (!validCalendar(data, identity, month, playerId, offset) || (append &&
          (data.selected_player_id !== previous?.selected_player_id || data.organization_id !== previous.organization_id || data.time_zone !== previous.time_zone))) {
        throw new Error('Invalid calendar response');
      }
      const items = new Map<string, MobileCalendarItem>();
      for (const item of [...(append ? previous?.items ?? [] : []), ...data.items]) items.set(calendarItemKey(item), item);
      update({ calendar: { ...data, items: [...items.values()] }, calendarStatus: 'ready', calendarError: null, calendarLoadingMore: false });
      return true;
    } catch (error) {
      if (!isCurrent()) return false;
      if (error instanceof ApiFailure && error.status === 401) endSession(error.message);
      else if (error instanceof ApiFailure && error.status === 403 && error.code === 'account_unavailable') revokeCurrentRole(error.message);
      else if (error instanceof ApiFailure && error.status === 403) denyCalendarAccess(error);
      else update({ calendarStatus: append ? 'ready' : 'error', calendarError: message(error), calendarLoadingMore: false });
      return false;
    }
  }

  async function loadCalendarResponses(item: MobileCalendarItem, append = false) {
    const identity = calendarIdentity();
    const key = calendarItemKey(item);
    const currentItem = state.calendar?.items.find((candidate) => calendarItemKey(candidate) === key);
    if (state.busy || !identity || (identity.role !== 'coach' && identity.role !== 'management') ||
        state.calendarStatus !== 'ready' || !currentItem) return false;
    const previous = state.calendarResponses;
    const offset = append ? previous?.next_offset : 0;
    if (append && (state.calendarResponsesLoadingMore || state.calendarResponsesStatus !== 'ready' ||
        state.calendarResponsesKey !== key || offset == null)) return false;
    if (typeof offset !== 'number' || !Number.isInteger(offset) || offset < 0) return false;
    const operationRevision = ++calendarResponsesRevision;
    const monthRevision = calendarRevision;
    const sessionToken = token;
    const organizationId = state.organization?.organization_id;
    const isCurrent = () => operationRevision === calendarResponsesRevision && monthRevision === calendarRevision &&
      calendarScopeCurrent(identity, sessionToken, organizationId);
    update(append ? { calendarResponsesLoadingMore: true, calendarResponsesError: null }
      : { calendarResponses: null, calendarResponsesKey: key, calendarResponsesStatus: 'loading', calendarResponsesError: null, calendarResponsesLoadingMore: false });
    try {
      const data = responseData(await api.GET('/api/mobile/calendar/{event_type}/{event_id}/responses', { headers: authorization(),
        params: { path: { event_id: currentItem.id, event_type: currentItem.kind }, query: { offset: String(offset) } } }));
      if (!isCurrent()) return false;
      if (!data || data.event_id !== currentItem.id || data.event_type !== currentItem.kind ||
          data.organization_id !== currentItem.organization_id || !validOffset(data.next_offset, offset) ||
          !Array.isArray(data.players) || !data.players.every((player) => player && typeof player.id === 'string' && !!player.id.trim() &&
            typeof player.name === 'string' && typeof player.number === 'number' && Number.isInteger(player.number) &&
            typeof player.position === 'string' && (player.image_url == null || typeof player.image_url === 'string') &&
            (player.status === null || player.status === 'no_response' || isAvailabilityStatus(player.status)) &&
            (player.updated_at == null || typeof player.updated_at === 'string'))) {
        throw new Error('Invalid calendar replies');
      }
      const players = new Map([...(append ? previous?.players ?? [] : []), ...data.players].map((player) => [player.id, player]));
      update({ calendarResponses: { ...data, players: [...players.values()] }, calendarResponsesStatus: 'ready',
        calendarResponsesError: null, calendarResponsesLoadingMore: false });
      return true;
    } catch (error) {
      if (!isCurrent()) return false;
      if (error instanceof ApiFailure && error.status === 401) endSession(error.message);
      else if (error instanceof ApiFailure && error.status === 403 && error.code === 'account_unavailable') revokeCurrentRole(error.message);
      else if (error instanceof ApiFailure && error.status === 403) denyCalendarAccess(error);
      else update({ calendarResponsesStatus: append ? 'ready' : 'error', calendarResponsesError: message(error), calendarResponsesLoadingMore: false });
      return false;
    }
  }

  async function loadFamily() {
    const identity = state.user;
    if (state.busy || state.familyStatus === 'loading' || state.status !== 'authenticated' ||
        !identity?.current_role_available || identity.role !== 'parent' || !token) return false;
    const operationRevision = ++familyRevision;
    const bearer = token;
    const isCurrent = () => familyRevision === operationRevision && token === bearer &&
      state.status === 'authenticated' && state.user?.id === identity.id &&
      state.user?.phone === identity.phone && state.user?.role === 'parent' &&
      state.user.current_role_available;
    clearOutgoingInvitations();
    update({ family: null, familyStatus: 'loading', familyError: null });
    try {
      const data = responseData(await api.GET('/api/mobile/family', { headers: authorization() }));
      if (!isCurrent()) return false;
      const ids = new Set<string>();
      if (!data || !Array.isArray(data.children) || data.children.length > 100 ||
          !data.children.every((child) => child && typeof child.id === 'string' &&
            child.id.length > 0 && child.id.length <= 128 && !ids.has(child.id) && !!ids.add(child.id) &&
            typeof child.name === 'string' && child.name.length > 0 && child.name.length <= 200 &&
            (child.organization_id === 'lefteria_fc' || child.organization_id === 'lefteria_fc_academy') &&
            (child.team_type === 'First Team' || child.team_type === 'Academy') &&
            (child.team_id === null || typeof child.team_id === 'string') &&
            Array.isArray(child.academy_group_ids) && child.academy_group_ids.length <= 100 &&
            child.academy_group_ids.every((group) => typeof group === 'string' && group.length > 0 && group.length <= 128))) {
        throw new Error('Invalid family response');
      }
      update({ family: data, familyStatus: 'ready', familyError: null });
      return true;
    } catch (error) {
      if (!isCurrent()) return false;
      if (error instanceof ApiFailure && error.status === 401) endSession(error.message);
      else if (error instanceof ApiFailure && error.status === 403 && error.code === 'account_unavailable')
        revokeCurrentRole(error.message);
      else update({ family: null, familyStatus: 'error', familyError: message(error) });
      return false;
    }
  }

  async function loadActiveFamilyDelegations() {
    const identity = state.user;
    const family = state.family;
    if (state.busy || state.activeFamilyStatus === 'loading' ||
        state.status !== 'authenticated' || !identity?.current_role_available ||
        identity.role !== 'parent' || !token || !family || state.familyStatus !== 'ready') return false;
    const operationRevision = ++activeFamilyRevision;
    const linkedRevision = familyRevision;
    const bearer = token;
    const isCurrent = () => activeFamilyRevision === operationRevision &&
      familyRevision === linkedRevision && token === bearer &&
      state.status === 'authenticated' && state.user?.id === identity.id &&
      state.user?.phone === identity.phone && state.user?.role === 'parent' &&
      state.user.current_role_available && state.family === family;
    update({activeFamilyDelegations:null,activeFamilyStatus:'loading',activeFamilyError:null});
    try {
      const data=responseData(await api.GET('/api/mobile/family/active-children',
        {headers:authorization()}));
      if (!isCurrent()) return false;
      const allowed=new Map(family.children.map(child=>[child.id,child.organization_id]));
      const seen=new Set<string>();
      if (!data || !Array.isArray(data.items) || data.items.length>100 ||
          !data.items.every(item=>item && typeof item.player_id==='string' &&
            allowed.get(item.player_id)===item.organization_id &&
            !seen.has(item.player_id) && !!seen.add(item.player_id)))
        throw new Error('Invalid active delegation response');
      const items=data.items.map(item=>({player_id:item.player_id,organization_id:item.organization_id}));
      update({activeFamilyDelegations:{items},activeFamilyStatus:'ready',activeFamilyError:null});
      return true;
    } catch(error) {
      if (!isCurrent()) return false;
      if (error instanceof ApiFailure && error.status===401) endSession(error.message);
      else if (error instanceof ApiFailure && error.status===403 &&
          error.code==='account_unavailable') revokeCurrentRole(error.message);
      else update({activeFamilyDelegations:null,activeFamilyStatus:'error',activeFamilyError:message(error)});
      return false;
    }
  }

  async function revokeFamilyDelegation(playerId: string) {
    const identity = state.user;
    const family = state.family;
    if (state.status !== 'authenticated' || !identity?.current_role_available ||
        identity.role !== 'parent' || !token || state.busy || state.familyRevokingPlayerId ||
        state.familyStatus !== 'ready' || !family || state.activeFamilyStatus !== 'ready' ||
        !state.activeFamilyDelegations?.items.some(item=>item.player_id===playerId &&
          family.children.some(child=>child.id===item.player_id &&
            child.organization_id===item.organization_id))) return false;
    const operationRevision = familyRevision;
    const bearer = token;
    const isCurrent = () => familyRevision === operationRevision && token === bearer &&
      state.status === 'authenticated' && state.user?.id === identity.id &&
      state.user?.phone === identity.phone && state.user?.role === 'parent' &&
      state.user.current_role_available && state.family === family;
    update({ familyRevokingPlayerId: playerId, familyRevokeError: null,
      familyRevokeSuccess: null });
    try {
      const result = responseData(await api.POST('/api/mobile/family/children/{playerId}/revoke', {
        headers: authorization(), params: {path: {playerId}}
      }));
      if (!isCurrent()) return false;
      if (!result || result.ok !== true) throw new Error('Invalid revocation response');
      update({familyRevokingPlayerId: null,
        activeFamilyDelegations: state.activeFamilyDelegations ? {
          items:state.activeFamilyDelegations.items.filter(item=>item.player_id!==playerId)
        } : null,
        familyRevokeSuccess: 'Η οικογενειακή πρόσβαση ανακλήθηκε.',familyRevokeError: null});
      return true;
    } catch (error) {
      if (!isCurrent()) return false;
      if (error instanceof ApiFailure && error.status === 401) endSession(error.message);
      else if (error instanceof ApiFailure && error.status === 403 &&
          error.code === 'account_unavailable') revokeCurrentRole(error.message);
      else if (error instanceof ApiFailure && error.status === 403) {
        clearFamily();
        update({familyRevokeError: 'Η σύνδεση γονέα άλλαξε. Ανανέωσε τα στοιχεία.'});
      } else update({familyRevokingPlayerId: null,
        familyRevokeError: error instanceof ApiFailure && error.status === 404
          ? 'Δεν υπάρχει ενεργή οικογενειακή πρόσβαση για ανάκληση.' : message(error)});
      return false;
    }
  }

  async function loadOutgoingInvitations() {
    const identity = state.user;
    const family = state.family;
    if (state.busy || state.outgoingStatus === 'loading' ||
        state.status !== 'authenticated' || !identity?.current_role_available ||
        identity.role !== 'parent' || !token || state.familyStatus !== 'ready' || !family) return false;
    const operationRevision = ++outgoingRevision;
    const linkedRevision = familyRevision;
    const bearer = token;
    const isCurrent = () => outgoingRevision === operationRevision &&
      familyRevision === linkedRevision && token === bearer &&
      state.status === 'authenticated' && state.user?.id === identity.id &&
      state.user?.phone === identity.phone && state.user?.role === 'parent' &&
      state.user.current_role_available && state.familyStatus === 'ready' &&
      state.family === family;
    update({ outgoingInvitations: null, outgoingStatus: 'loading', outgoingError: null });
    try {
      const data = responseData(await api.GET('/api/mobile/family/outgoing-invitations',
        { headers: authorization() }));
      if (!isCurrent()) return false;
      const byChild = new Map(family.children.map(child => [child.id, child.organization_id]));
      const seenInvitations = new Set<string>();
      const seenChildren = new Set<string>();
      const now = Date.now();
      if (!data || !Array.isArray(data.items) || data.items.length > 100 ||
          !data.items.every((item) =>
            item && typeof item.id === 'string' &&
            /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(item.id) &&
            !seenInvitations.has(item.id) && !!seenInvitations.add(item.id) &&
            typeof item.player_id === 'string' && !seenChildren.has(item.player_id) &&
            !!seenChildren.add(item.player_id) &&
            typeof item.organization_id === 'string' &&
            byChild.get(item.player_id) === item.organization_id &&
            item.status === 'pending' &&
            typeof item.created_at === 'string' && Number.isFinite(Date.parse(item.created_at)) &&
            typeof item.expires_at === 'string' && Number.isFinite(Date.parse(item.expires_at)) &&
            Date.parse(item.expires_at) > Date.parse(item.created_at) &&
            Date.parse(item.expires_at) > now)) throw new Error('Invalid outgoing invitation response');
      update({ outgoingInvitations: data, outgoingStatus: 'ready', outgoingError: null });
      return true;
    } catch (error) {
      if (!isCurrent()) return false;
      if (error instanceof ApiFailure && error.status === 401) endSession(error.message);
      else if (error instanceof ApiFailure && error.status === 403 && error.code === 'account_unavailable')
        revokeCurrentRole(error.message);
      else update({ outgoingInvitations: null, outgoingStatus: 'error', outgoingError: message(error) });
      return false;
    }
  }

  async function cancelOutgoingInvitation(invitationId: string) {
    const identity = state.user;
    const family = state.family;
    const outbox = state.outgoingInvitations;
    if (!identity || state.status !== 'authenticated' || !identity.current_role_available ||
        identity.role !== 'parent' || !token || state.busy || state.outgoingCancellingId ||
        state.familyStatus !== 'ready' || !family || state.outgoingStatus !== 'ready' || !outbox ||
        !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(invitationId))
      return false;
    const invite = outbox.items.find(item => item.id === invitationId &&
      family.children.some(child => child.id === item.player_id &&
        child.organization_id === item.organization_id));
    if (!invite) return false;

    const operationRevision = ++outgoingRevision;
    const linkedRevision = familyRevision;
    const bearer = token;
    const isCurrent = () => outgoingRevision === operationRevision &&
      familyRevision === linkedRevision && token === bearer &&
      state.status === 'authenticated' && state.user?.id === identity.id &&
      state.user?.phone === identity.phone && state.user?.role === 'parent' &&
      state.user.current_role_available && state.family === family;
    update({ outgoingCancellingId: invitationId, outgoingCancelError: null });
    try {
      const result = responseData(await api.POST('/api/mobile/family/invitations/{invitationId}/cancel', {
        headers: authorization(), params: {path: {invitationId}}
      }));
      if (!isCurrent()) return false;
      if (!result || result.ok !== true) throw new Error('Invalid cancellation response');
      update({ outgoingInvitations: {items: outbox.items.filter(item => item.id !== invitationId)},
        outgoingCancellingId: null, outgoingCancelError: null });
      return true;
    } catch (error) {
      if (!isCurrent()) return false;
      if (error instanceof ApiFailure && error.status === 401) endSession(error.message);
      else if (error instanceof ApiFailure && error.status === 403 &&
          error.code === 'account_unavailable') revokeCurrentRole(error.message);
      else if (error instanceof ApiFailure && (error.status === 403 || error.status === 404)) {
        clearFamily();
        update({ outgoingCancelError: 'Η οικογενειακή σύνδεση άλλαξε. Ανανέωσε τα στοιχεία.' });
      }
      else update({ outgoingCancellingId: null, outgoingCancelError: message(error) });
      return false;
    }
  }

  async function loadDelegationAvailability() {
    const identity = state.user;
    if (state.busy || state.delegationStatus === 'loading' ||
        state.status !== 'authenticated' || !identity?.current_role_available ||
        identity.role !== 'fan' || !token) return false;
    const operationRevision = ++delegationRevision;
    const bearer = token;
    const isCurrent = () => delegationRevision === operationRevision && token === bearer &&
      state.status === 'authenticated' && state.user?.id === identity.id &&
      state.user?.phone === identity.phone && state.user?.role === 'fan' &&
      state.user.current_role_available;
    // A status refresh invalidates previously displayed private child data
    // until the current grant is verified again by the server.
    clearDelegatedCalendar();
    update({ delegationAvailability: null, delegationStatus: 'loading', delegationError: null });
    try {
      const data = responseData(await api.GET('/api/mobile/family/active-delegation',
        { headers: authorization() }));
      if (!isCurrent()) return false;
      if (!data || typeof data.has_active_delegation !== 'boolean')
        throw new Error('Invalid recipient delegation status');
      // Whitelist the sole public field; never retain unexpected player,
      // guardian, grant, or phone information returned by a faulty server.
      if (!data.has_active_delegation) clearDelegatedCalendar();
      update({ delegationAvailability: {has_active_delegation: data.has_active_delegation},
        delegationStatus: 'ready', delegationError: null });
      return true;
    } catch (error) {
      if (!isCurrent()) return false;
      if (error instanceof ApiFailure && error.status === 401) endSession(error.message);
      else if (error instanceof ApiFailure && error.status === 403 &&
          error.code === 'account_unavailable') revokeCurrentRole(error.message);
      else update({ delegationAvailability: null, delegationStatus: 'error',
        delegationError: message(error) });
      return false;
    }
  }

  // Only the already-accepted, dynamically revalidated grant can populate
  // this separate READ-ONLY member calendar. It is not a player role.
  async function loadDelegatedCalendar(month: string, append = false) {
    const identity=state.user;
    const grant=state.delegationAvailability;
    const previous=state.delegatedCalendar;
    if (state.busy || state.delegatedRespondingKey || state.delegatedCalendarStatus==='loading' ||
        state.delegatedCalendarLoadingMore || !isCalendarMonth(month) ||
        state.status!=='authenticated' || !identity?.current_role_available ||
        identity.role!=='fan' || !token || state.delegationStatus!=='ready' ||
        !grant?.has_active_delegation ||
        (append && (!previous || previous.month!==month || previous.next_offset===null))) return false;
    const operationRevision=++delegatedCalendarRevision;
    const bearer=token;
    const offset=append ? Number(previous!.next_offset) : 0;
    if (!Number.isSafeInteger(offset) || offset<0 || offset>100000) return false;
    const isCurrent=()=>delegatedCalendarRevision===operationRevision && token===bearer &&
      state.status==='authenticated' && state.user?.id===identity.id &&
      state.user?.phone===identity.phone && state.user?.role==='fan' &&
      state.user.current_role_available && state.delegationAvailability===grant &&
      state.delegationStatus==='ready';
    update(append?{delegatedCalendarLoadingMore:true,delegatedCalendarError:null}:
      {delegatedCalendar:null,delegatedCalendarStatus:'loading',delegatedCalendarError:null});
    try {
      const data=responseData(await api.GET('/api/mobile/family/calendar',{
        headers:authorization(),params:{query:{month,offset:String(offset)}}
      }));
      if (!isCurrent()) return false;
      const child=data?.players?.[0];
      if (!data || data.role!=='fan' || data.month!==month ||
          !Array.isArray(data.players) || data.players.length!==1 ||
          !child || !child.id || !data.organization_id ||
          child.organization_id!==data.organization_id ||
          data.selected_player_id!==child.id || !Array.isArray(data.items) ||
          data.items.length>200 ||
          (data.next_offset!==null && (typeof data.next_offset!=='number' ||
            !Number.isSafeInteger(data.next_offset) || data.next_offset<=offset || data.next_offset>100000)) ||
          !data.items.every(item=>item && isCalendarKind(item.kind) &&
            isCalendarDate(item.date) && isCalendarDate(item.end_date) &&
            item.end_date>=item.date && typeof item.title==='string' &&
            item.title.length>0 && item.title.length<=2048 &&
            item.organization_id===data.organization_id && Array.isArray(item.responses) &&
            item.responses.every(response=>response.player_id===child.id)))
        throw new Error('Invalid delegated calendar response');
      const prior=append ? previous!.items : [];
      const keys=new Set(prior.map(calendarItemKey));
      if(data.items.some(item=>{const key=calendarItemKey(item);if(keys.has(key))return true;keys.add(key);return false;}))
        throw new Error('Repeated delegated calendar event');
      update({delegatedCalendar:{...data,items:[...prior,...data.items]},
        delegatedCalendarStatus:'ready',delegatedCalendarLoadingMore:false,delegatedCalendarError:null});
      return true;
    } catch(error) {
      if (!isCurrent()) return false;
      if(error instanceof ApiFailure && error.status===401) endSession(error.message);
      else if(error instanceof ApiFailure && error.status===403){
        clearDelegatedCalendar();
        update({delegationAvailability:null,delegationStatus:'error',
          delegationError:'Η οικογενειακή πρόσβαση άλλαξε. Έλεγξε ξανά τη σύνδεση.'});
      } else update({delegatedCalendar:append?previous:null,
        delegatedCalendarStatus:append?'ready':'error',delegatedCalendarLoadingMore:false,
        delegatedCalendarError:message(error)});
      return false;
    }
  }

  async function submitDelegatedAvailability(item: MobileCalendarItem,status: AvailabilityStatus) {
    const identity=state.user;
    const calendar=state.delegatedCalendar;
    const grant=state.delegationAvailability;
    const key=calendarItemKey(item);
    const original=calendar?.items.find(candidate=>calendarItemKey(candidate)===key);
    if(state.busy || state.delegatedRespondingKey || !isAvailabilityStatus(status) ||
        state.status!=='authenticated' || !identity?.current_role_available ||
        identity.role!=='fan' || !token || state.delegationStatus!=='ready' ||
        !grant?.has_active_delegation || state.delegatedCalendarStatus!=='ready' ||
        !calendar || !original || original.is_cancelled ||
        !calendar.selected_player_id || original.organization_id!==calendar.organization_id)
      return false;
    const operationRevision=++delegatedCalendarRevision;
    const bearer=token;
    const isCurrent=()=>delegatedCalendarRevision===operationRevision && token===bearer &&
      state.status==='authenticated' && state.user?.id===identity.id &&
      state.user?.phone===identity.phone && state.user?.role==='fan' &&
      state.user.current_role_available && state.delegationStatus==='ready' &&
      state.delegationAvailability===grant && state.delegatedCalendar===calendar;
    update({delegatedRespondingKey:key,delegatedRespondError:null,delegatedRespondSuccess:null});
    try {
      const result=responseData(await api.POST('/api/mobile/family/availability',{
        headers:authorization(),body:{event_id:original.id,event_type:original.kind,status}
      }));
      if(!isCurrent())return false;
      if(!result || result.player_id!==calendar.selected_player_id ||
          result.event_id!==original.id || result.event_type!==original.kind ||
          result.status!==status)
        throw new Error('Invalid delegated RSVP confirmation');
      update({delegatedRespondingKey:null,delegatedRespondError:null,
        delegatedRespondSuccess:'Η δήλωση συμμετοχής αποθηκεύτηκε.'});
      // Never infer the server's child data or replay previous attendance;
      // requery through the same live, revocation-aware calendar endpoint.
      return await loadDelegatedCalendar(calendar.month);
    }catch(error){
      if(!isCurrent())return false;
      if(error instanceof ApiFailure && error.status===401)endSession(error.message);
      else if(error instanceof ApiFailure && error.status===403){
        clearDelegatedCalendar();
        update({delegationAvailability:null,delegationStatus:'error',
          delegationError:'Η οικογενειακή πρόσβαση άλλαξε. Έλεγξε ξανά τη σύνδεση.'});
      }else update({delegatedRespondingKey:null,delegatedRespondError:message(error),
        delegatedRespondSuccess:null});
      return false;
    }
  }

  async function loadRecipientInvitations() {
    const identity = state.user;
    if (state.busy || state.recipientDecliningId || state.recipientStatus === 'loading' ||
        state.status !== 'authenticated' || !identity?.current_role_available ||
        identity.role !== 'fan' || !token) return false;
    const operationRevision = ++recipientRevision;
    const bearer = token;
    const isCurrent = () => recipientRevision === operationRevision && token === bearer &&
      state.status === 'authenticated' && state.user?.id === identity.id &&
      state.user?.phone === identity.phone && state.user?.role === 'fan' &&
      state.user.current_role_available;
    update({ recipientInvitations: null, recipientStatus: 'loading', recipientError: null });
    try {
      const data = responseData(await api.GET('/api/mobile/family/invitations',
        { headers: authorization() }));
      if (!isCurrent()) return false;
      const seen = new Set<string>();
      const now = Date.now();
      if (!data || !Array.isArray(data.items) || data.items.length > 1 ||
          !data.items.every(item => item &&
            typeof item.id === 'string' &&
            /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(item.id) &&
            !seen.has(item.id) && !!seen.add(item.id) &&
            (item.organization_id === 'lefteria_fc' ||
              item.organization_id === 'lefteria_fc_academy') &&
            item.status === 'pending' &&
            typeof item.created_at === 'string' &&
            typeof item.expires_at === 'string' &&
            Number.isFinite(Date.parse(item.created_at)) &&
            Number.isFinite(Date.parse(item.expires_at)) &&
            Date.parse(item.expires_at) > Date.parse(item.created_at) &&
            Date.parse(item.expires_at) > now))
        throw new Error('Invalid recipient invitation response');
      // Exact projection prevents retaining unexpected youth or guardian
      // fields even if a compromised upstream includes extra JSON keys.
      const items = data.items.map(item => ({
        id: item.id, organization_id: item.organization_id,
        status: item.status, created_at: item.created_at,
        expires_at: item.expires_at
      }));
      update({ recipientInvitations: {items}, recipientStatus: 'ready', recipientError: null });
      return true;
    } catch (error) {
      if (!isCurrent()) return false;
      if (error instanceof ApiFailure && error.status === 401) endSession(error.message);
      else if (error instanceof ApiFailure && error.status === 403 &&
          error.code === 'account_unavailable') revokeCurrentRole(error.message);
      else update({ recipientInvitations: null, recipientStatus: 'error',
        recipientError: message(error) });
      return false;
    }
  }

  async function acceptRecipientInvitation(invitationId: string) {
    const identity = state.user;
    const inbox = state.recipientInvitations;
    if (state.status !== 'authenticated' || !identity?.current_role_available ||
        identity.role !== 'fan' || !token || state.busy || state.recipientDecliningId ||
        state.recipientAcceptingId || state.recipientStatus !== 'ready' || !inbox ||
        !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(invitationId) ||
        !inbox.items.some(item => item.id === invitationId && item.status === 'pending' &&
          Date.parse(item.expires_at) > Date.now())) return false;
    const operationRevision = ++recipientRevision;
    const bearer = token;
    const isCurrent = () => recipientRevision === operationRevision && token === bearer &&
      state.status === 'authenticated' && state.user?.id === identity.id &&
      state.user?.phone === identity.phone && state.user?.role === 'fan' &&
      state.user.current_role_available;
    update({recipientAcceptingId: invitationId, recipientAcceptError: null,
      recipientAcceptSuccess: null});
    try {
      const result = responseData(await api.POST(
        '/api/mobile/family/invitations/{invitationId}/accept',
        {headers: authorization(), params: {path:{invitationId}}}));
      if (!isCurrent()) return false;
      if (!result || result.ok !== true) throw new Error('Invalid acceptance confirmation');
      update({recipientInvitations:{items:inbox.items.filter(item=>item.id!==invitationId)},
        recipientAcceptingId:null,recipientAcceptError:null,
        recipientAcceptSuccess:'Η οικογενειακή σύνδεση έγινε αποδεκτή. Ελέγχουμε την πρόσβαση στο πρόγραμμα και τις δηλώσεις συμμετοχής του αθλητή. Δεν ενεργοποιείται σύνδεση ως αθλητής.',
        delegationAvailability:null,delegationStatus:'idle',delegationError:null});
      // Server status is queried afresh, never inferred from the POST reply.
      void loadDelegationAvailability();
      return true;
    } catch(error) {
      if (!isCurrent()) return false;
      if (error instanceof ApiFailure && error.status===401) endSession(error.message);
      else if (error instanceof ApiFailure && error.status===403 &&
          error.code==='account_unavailable') revokeCurrentRole(error.message);
      else if (error instanceof ApiFailure && (error.status===403 || error.status===404)) {
        clearRecipientInvitations();
        update({recipientAcceptError:'Η καταχώριση ή η επιβεβαίωση άλλαξε. Ανανέωσε τις προσκλήσεις σου.'});
      } else update({recipientAcceptingId:null,recipientAcceptError:message(error)});
      return false;
    }
  }

  async function declineRecipientInvitation(invitationId: string) {
    const identity = state.user;
    const inbox = state.recipientInvitations;
    if (state.status !== 'authenticated' || !identity?.current_role_available ||
        identity.role !== 'fan' || !token || state.busy || state.recipientDecliningId || state.recipientAcceptingId ||
        state.recipientStatus !== 'ready' || !inbox ||
        !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(invitationId) ||
        !inbox.items.some(item => item.id === invitationId && item.status === 'pending' &&
          item.expires_at && Date.parse(item.expires_at) > Date.now())) return false;

    const operationRevision = ++recipientRevision;
    const bearer = token;
    const isCurrent = () => recipientRevision === operationRevision && token === bearer &&
      state.status === 'authenticated' && state.user?.id === identity.id &&
      state.user?.phone === identity.phone && state.user?.role === 'fan' &&
      state.user.current_role_available;
    update({ recipientDecliningId: invitationId, recipientDeclineError: null });
    try {
      const result = responseData(await api.POST(
        '/api/mobile/family/invitations/{invitationId}/decline',
        { headers: authorization(), params: {path:{invitationId}} }));
      if (!isCurrent()) return false;
      if (!result || result.ok !== true) throw new Error('Invalid decline confirmation');
      update({ recipientInvitations: {
          items: inbox.items.filter(item => item.id !== invitationId)
        }, recipientDecliningId: null, recipientDeclineError: null });
      return true;
    } catch(error) {
      if (!isCurrent()) return false;
      if (error instanceof ApiFailure && error.status === 401) endSession(error.message);
      else if (error instanceof ApiFailure && error.status === 403 &&
          error.code === 'account_unavailable') revokeCurrentRole(error.message);
      else if (error instanceof ApiFailure && (error.status === 403 || error.status === 404)) {
        clearRecipientInvitations();
        update({ recipientDeclineError:
          'Η καταχώριση ή η πρόσβαση άλλαξε. Ανανέωσε τις προσκλήσεις σου.' });
      } else update({ recipientDecliningId: null, recipientDeclineError: message(error) });
      return false;
    }
  }

  return {
    subscribe: store.subscribe,
    getState: () => state,
    async bootstrap() {
      return action(async (operationRevision) => {
        clearPlayerOptions();
        clearCalendar();
        clearFamily();
        clearRecipientInvitations();
        pendingCode = null;
        update({ status: 'initializing', user: null, availableRoles: [], organization: null,
          organizationError: null, identities: [], detectedRole: null, phone: '', step: 'phone' });
        token = options.storage.readToken();
        if (!token) { anonymous(); return; }
        const headers = authorization();
        try {
          const result = await api.GET('/api/mobile/auth/me', { headers });
          if (revision !== operationRevision) return;
          if (result.response.status === 401) {
            const login = responseData(await api.POST('/api/mobile/auth/exchange-legacy', { headers }));
            if (revision !== operationRevision) return;
            await finishLogin(login, operationRevision);
          } else {
            const user = responseData(result);
            applyUser(user);
            if (user.role === 'management' && user.current_role_available) await organizationContext(authorization(), operationRevision);
          }
        } catch (error) {
          if (revision !== operationRevision) return;
          if (error instanceof ApiFailure && error.status === 401) anonymous(error.message);
          else update({ status: 'unavailable', error: message(error), user: null, organization: null, availableRoles: [] });
        }
      });
    },
    async requestOtp(phone: string) {
      return action(async (operationRevision) => {
        clearPlayerOptions();
        clearCalendar();
        clearFamily();
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
        clearPlayerOptions();
        clearCalendar();
        clearFamily();
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
        clearPlayerOptions();
        clearCalendar();
        clearFamily();
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
        clearCalendar();
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
        clearCalendar();
        update({ organization: null, organizationError: null });
        await organizationContext(authorization(), operationRevision);
      }, true);
    },
    loadFamily,
    loadActiveFamilyDelegations,
    loadOutgoingInvitations,
    revokeFamilyDelegation,
    cancelOutgoingInvitation,
    loadRecipientInvitations,
    loadDelegationAvailability,
    loadDelegatedCalendar,
    submitDelegatedAvailability,
    declineRecipientInvitation,
    acceptRecipientInvitation,
    loadCalendar,
    loadMoreCalendar() {
      const calendar = state.calendar;
      return calendar ? loadCalendar(calendar.month, calendar.selected_player_id ?? undefined, true) : Promise.resolve(false);
    },
    loadCalendarResponses,
    clearCalendarResponses,
    async submitAvailability(item: MobileCalendarItem, status: AvailabilityStatus) {
      const identity = calendarIdentity();
      const calendar = state.calendar;
      const currentItem = calendar?.items.find((candidate) => calendarItemKey(candidate) === calendarItemKey(item));
      if (!identity || (identity.role !== 'parent' && identity.role !== 'player') || state.calendarStatus !== 'ready' ||
          !calendar?.selected_player_id || !currentItem || currentItem.is_cancelled || !isAvailabilityStatus(status)) return false;
      const playerId = calendar.selected_player_id;
      const sessionToken = token;
      const organizationId = state.organization?.organization_id;
      return action(async (operationRevision) => {
        ++calendarRevision;
        update({ calendarLoadingMore: false });
        clearCalendarResponses();
        const data = responseData(await api.POST('/api/mobile/availability', { headers: authorization(), body: {
          event_id: currentItem.id, event_type: currentItem.kind, player_id: playerId, status
        } }));
        if (revision !== operationRevision || !calendarScopeCurrent(identity, sessionToken, organizationId)) return;
        if (!data || data.event_id !== currentItem.id || data.event_type !== currentItem.kind ||
            data.player_id !== playerId || !isAvailabilityStatus(data.status)) throw new Error('Invalid availability response');
        // Read confirmed server state, including cancellation and assignment changes.
        // Release only this action's busy flag while its guarded refresh is pending.
        update({ busy: false });
        if (!await loadCalendar(calendar.month, playerId) && revision === operationRevision) {
          throw new ApiFailure(503, 'Η απάντηση αποθηκεύτηκε. Δεν ήταν δυνατή η ανανέωση του προγράμματος. Δοκιμάστε ξανά.');
        }
      }, true);
    },
    async loadPlayerOptions() {
      const identity = state.user;
      if (state.busy || state.status !== 'authenticated' || !identity?.current_role_available || state.playerOptionsStatus === 'loading') return false;
      const operationRevision = ++playerOptionsRevision;
      const sessionToken = token;
      const isCurrent = () => playerOptionsRevision === operationRevision && token === sessionToken &&
        state.status === 'authenticated' && state.user?.id === identity.id && state.user.role === identity.role &&
        state.user.phone === identity.phone && state.user.current_role_available;
      update({ playerOptions: [], playerOptionsStatus: 'loading', playerOptionsError: null });
      try {
        const result = responseData(await api.GET('/api/mobile/players-list', { headers: authorization() }));
        if (!isCurrent()) return false;
        if (!Array.isArray(result.players) || !result.players.every((player) =>
          typeof player.id === 'string' && player.id.length > 0 && typeof player.name === 'string' &&
          Number.isInteger(player.number) && typeof player.position === 'string' &&
          (player.image_url === null || typeof player.image_url === 'string'))) throw new Error('Invalid player options response');
        update({ playerOptions: result.players, playerOptionsStatus: 'ready', playerOptionsError: null });
        return true;
      } catch (error) {
        if (!isCurrent()) return false;
        if (error instanceof ApiFailure && error.status === 401) endSession(error.message);
        else update({ playerOptions: [], playerOptionsStatus: 'error', playerOptionsError: message(error) });
        return false;
      }
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
    async uploadAvatar(file: File) {
      const identity = state.user;
      if (state.status !== 'authenticated' || !identity?.current_role_available) return false;
      return action(async (operationRevision) => {
        const validation = avatarFileError(file);
        if (validation) throw new ApiFailure(422, validation);
        const headers = authorization();
        const result = responseData(await api.POST('/api/mobile/profile/avatar', {
          headers, body: { file },
          bodySerializer: () => {
            const form = new FormData();
            form.append('file', file, file.name);
            return form;
          }
        }));
        if (revision !== operationRevision) return;
        if (!/^\/api\/uploads\/avatars\/[a-zA-Z0-9_-]+\.jpg$/.test(result.avatar_url)) throw new Error('Invalid avatar response');
        const user = responseData(await api.GET('/api/mobile/auth/me', { headers }));
        if (revision !== operationRevision) return;
        applyOwnProfile(user, identity);
      }, true);
    },
    async getChatRooms():Promise<GroupChatRoom[]> {
      const result=await chatRequest('/api/mobile/chat/groups');
      if(!validChatDirectory(result))throw new Error('Invalid group directory response.');
      return result.groups.map(g=>({organization_id:g.organization_id,group_id:g.id,kind:g.kind,name:g.name}));
    },
    async getChatMessages(room:GroupChatRoom,offset=0):Promise<GroupChatPage> {
      if(!Number.isInteger(offset)||offset<0||offset>100000)throw new Error('Invalid page.');
      const result=await chatRequest(chatRoomPath(room)+'?offset='+offset);
      if(!validPage(result,room))throw new Error('Invalid chat messages response.');
      return result;
    },
    async sendChatMessage(room:GroupChatRoom,content:string):Promise<GroupChatMessage> {
      if(!validTextMessage(content))throw new Error('Invalid chat message.');
      const result=await chatRequest(chatRoomPath(room),content);
      if(!validMessage(result,room))throw new Error('Server did not confirm message.');
      return result;
    },
    logout() { endSession(); },
    dispose() { ++revision; pendingCode = null; clearPlayerOptions(); clearCalendar(); clearFamily(); clearRecipientInvitations(); }
  };
}

export type MobileAuthController = ReturnType<typeof createMobileAuthController>;
