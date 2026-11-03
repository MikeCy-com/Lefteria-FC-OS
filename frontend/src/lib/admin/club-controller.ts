import { writable } from 'svelte/store';
import { createApiClient } from '../api/client.ts';
import type { components } from '../api/schema';
import type { WebAdminSession } from './controller.ts';
import { hasAdminPermission } from './permissions.ts';

type ClubProfileWire = components['schemas']['AdminClubProfileDto'];
export type ClubProfile = Omit<ClubProfileWire, 'version'> & { version: number };
type ClubUpdate = components['schemas']['AdminClubUpdateCommand'];
export type ClubOrganization = 'lefteria_fc' | 'lefteria_fc_academy';
export const CLUB_SOCIAL_FIELDS = ['facebook', 'instagram', 'twitter', 'youtube', 'tiktok'] as const;
export const CLUB_TEXT_FIELDS = [
  { key: 'name', label: 'Όνομα συλλόγου', limit: 200 }, { key: 'greek_name', label: 'Ελληνικό όνομα', limit: 200 },
  { key: 'motto', label: 'Σύνθημα', limit: 1000 }, { key: 'city', label: 'Πόλη', limit: 200 },
  { key: 'country', label: 'Χώρα', limit: 100 }, { key: 'address', label: 'Διεύθυνση', limit: 1000 },
  { key: 'email', label: 'Δημόσιο email επικοινωνίας', limit: 254 }, { key: 'phone', label: 'Δημόσιο τηλέφωνο', limit: 40 },
  { key: 'website', label: 'Ιστοσελίδα (HTTPS)', limit: 2048 }, { key: 'logo_url', label: 'Σύνδεσμος λογοτύπου', limit: 2048 },
  { key: 'stadium_image_url', label: 'Σύνδεσμος φωτογραφίας γηπέδου', limit: 2048 }
] as const;
type TextField = typeof CLUB_TEXT_FIELDS[number]['key'];
export type ClubDraft = Record<TextField | 'founded' | 'description' | 'history', string> &
  { social_links: Record<typeof CLUB_SOCIAL_FIELDS[number], string> };
type Authority = WebAdminSession['user'];
export interface ClubEditorState {
  status: 'idle' | 'loading' | 'ready' | 'not_found' | 'forbidden' | 'unavailable';
  organizations: { id: ClubOrganization; name: string }[];
  organizationId: ClubOrganization | null;
  profile: ClubProfile | null;
  draft: ClubDraft | null;
  canWrite: boolean;
  editing: boolean;
  saving: boolean;
  dirty: boolean;
  conflict: boolean;
  error: string | null;
  success: string | null;
}

export function isClubOrganization(value: unknown): value is ClubOrganization { return value === 'lefteria_fc' || value === 'lefteria_fc_academy'; }
export function canReadClub(user: Authority): boolean {
  return hasAdminPermission(user.permissions, 'settings:read') && user.organizations.some(org => isClubOrganization(org.id));
}

function draftFrom(profile: ClubProfile): ClubDraft {
  const club = profile.club;
  return { name: club.name, greek_name: club.greek_name ?? '', founded: club.founded?.toString() ?? '',
    description: club.description ?? '', motto: club.motto ?? '', history: club.history ?? '',
    logo_url: club.logo_url ?? '', stadium_image_url: club.stadium_image_url ?? '', city: club.city ?? '', country: club.country ?? '',
    address: club.address ?? '', email: club.email ?? '', phone: club.phone ?? '', website: club.website ?? '',
    social_links: { facebook: club.social_links.facebook ?? '', instagram: club.social_links.instagram ?? '',
      twitter: club.social_links.twitter ?? '', youtube: club.social_links.youtube ?? '', tiktok: club.social_links.tiktok ?? '' } };
}

// Deliberate allowlist: response identifiers, provider credentials and banking
// information can never be spread into an update command.
function updateBody(profile: ClubProfile, draft: ClubDraft): ClubUpdate {
  const optional = (value: string) => value === '' ? null : value;
  return { expected_version: profile.version, club: {
    name: draft.name, greek_name: optional(draft.greek_name), founded: draft.founded === '' ? null : Number(draft.founded),
    description: optional(draft.description), motto: optional(draft.motto), history: optional(draft.history),
    logo_url: optional(draft.logo_url), stadium_image_url: optional(draft.stadium_image_url), city: optional(draft.city),
    country: optional(draft.country), address: optional(draft.address), email: optional(draft.email), phone: optional(draft.phone),
    website: optional(draft.website), social_links: Object.fromEntries(CLUB_SOCIAL_FIELDS.filter(key => draft.social_links[key] !== '')
      .map(key => [key, draft.social_links[key]]))
  } };
}

function readProfile(value: ClubProfileWire | undefined, organization: ClubOrganization): ClubProfile {
  if (!value || typeof value.version !== 'number' || !Number.isSafeInteger(value.version) || value.version < 0 || !value.club ||
      value.club.organization_id !== organization || typeof value.club.id !== 'string' || typeof value.club.name !== 'string' ||
      !value.club.social_links || typeof value.club.social_links !== 'object' || Array.isArray(value.club.social_links))
    throw new Error('Invalid club response.');
  // Touch every editable value before accepting the payload; malformed data
  // never becomes a successful save or a form for the wrong organization.
  const profile = { version: value.version, club: value.club };
  const draft = draftFrom(profile);
  if (Object.entries(draft).some(([key, field]) => key !== 'social_links' && typeof field !== 'string') ||
      Object.values(draft.social_links).some(field => typeof field !== 'string')) throw new Error('Invalid club fields.');
  return profile;
}

export function createClubEditorController(options: {
  baseUrl: string; fetch?: typeof globalThis.fetch; initialOrganization?: string | null;
  confirmDiscard?: () => boolean; onUnauthenticated: () => void; onForbidden?: () => void;
}) {
  const api = createApiClient(options);
  let state: ClubEditorState = { status: 'idle', organizations: [], organizationId: null, profile: null, draft: null,
    canWrite: false, editing: false, saving: false, dirty: false, conflict: false, error: null, success: null };
  const store = writable(state);
  let authority: Authority | null = null;
  let signature = '';
  let revision = 0;
  let disposed = false;
  const update = (patch: Partial<ClubEditorState>) => { if (!disposed) { state = { ...state, ...patch }; store.set(state); } };
  const clear = (status: ClubEditorState['status'], error: string | null = null) => {
    ++revision; update({ status, profile: null, draft: null, editing: false, saving: false, dirty: false, conflict: false, error, success: null });
  };
  const allowed = (id: string | null): id is ClubOrganization => authority !== null && canReadClub(authority) &&
    state.organizations.some(org => org.id === id);

  function denied(status: number) {
    if (status === 401) {
      authority = null; signature = ''; clear('forbidden'); update({ organizations: [], organizationId: null, canWrite: false });
      options.onUnauthenticated(); return;
    }
    clear('forbidden', 'Δεν έχετε πλέον πρόσβαση σε αυτό το προφίλ συλλόγου.');
    options.onForbidden?.();
  }

  async function load() {
    const organization = state.organizationId;
    if (disposed || !allowed(organization) || state.saving) return false;
    clear('loading'); const current = revision;
    try {
      const result = await api.GET('/api/admin/club', { credentials: 'same-origin', params: { query: { organization_id: organization } } });
      if (disposed || current !== revision) return false;
      if (result.response.status === 401 || result.response.status === 403) { denied(result.response.status); return false; }
      if (result.response.status === 404) { clear('not_found', 'Δεν υπάρχει καταχωρισμένο προφίλ για αυτόν τον οργανισμό.'); return false; }
      if (!result.response.ok) throw new Error('Club unavailable.');
      const profile = readProfile(result.data, organization);
      update({ status: 'ready', profile, draft: draftFrom(profile), error: null }); return true;
    } catch { if (current === revision) clear('unavailable', 'Το προφίλ συλλόγου δεν είναι διαθέσιμο. Δοκιμάστε ξανά.'); return false; }
  }

  async function setAuthority(user: Authority | null) {
    if (disposed) return;
    if (!user) { authority = null; signature = ''; clear('idle'); update({ organizations: [], organizationId: null, canWrite: false }); return; }
    const next = JSON.stringify([user.id, [...user.permissions].sort(), user.organizations.map(org => [org.id, org.name])]);
    if (next === signature) return;
    const changedIdentity = authority?.id !== user.id;
    const previousWrite = state.canWrite;
    authority = user; signature = next;
    const organizations = user.organizations.filter((org): org is { id: ClubOrganization; name: string } => isClubOrganization(org.id));
    update({ organizations, canWrite: hasAdminPermission(user.permissions, 'settings:write') });
    if (!canReadClub(user)) { clear('forbidden', 'Δεν έχετε δικαίωμα προβολής προφίλ συλλόγου.'); return; }
    if (changedIdentity) {
      clear('idle');
      const initial = options.initialOrganization;
      update({ organizationId: initial ? isClubOrganization(initial) ? initial : null : organizations[0]?.id ?? null });
    }
    if (!allowed(state.organizationId)) { clear('forbidden', 'Ο επιλεγμένος οργανισμός δεν έχει ανατεθεί στον λογαριασμό σας.'); return; }
    if (previousWrite && !state.canWrite && state.editing) {
      ++revision;
      update({ saving: false, editing: false, dirty: false, draft: state.profile ? draftFrom(state.profile) : null,
        error: 'Το δικαίωμα επεξεργασίας άλλαξε. Μπορείτε μόνο να προβάλετε τα στοιχεία.', success: null });
    }
    if (state.status === 'idle' || state.status === 'forbidden') await load();
  }

  async function selectOrganization(id: string) {
    if (disposed || state.saving || id === state.organizationId || !allowed(id)) return false;
    if (state.dirty && !(options.confirmDiscard?.() ?? false)) return false;
    update({ organizationId: id }); return load();
  }
  function edit() { if (state.status === 'ready' && state.canWrite && !state.saving && !state.conflict) update({ editing: true, error: null, success: null }); }
  function change(field: Exclude<keyof ClubDraft, 'social_links'>, value: string) {
    if (!state.editing || !state.canWrite || state.saving || !state.draft || !state.profile) return;
    const draft = { ...state.draft, [field]: value };
    update({ draft, dirty: JSON.stringify(draft) !== JSON.stringify(draftFrom(state.profile)), success: null });
  }
  function changeSocial(field: typeof CLUB_SOCIAL_FIELDS[number], value: string) {
    if (!state.editing || !state.canWrite || state.saving || !state.draft || !state.profile) return;
    const draft = { ...state.draft, social_links: { ...state.draft.social_links, [field]: value } };
    update({ draft, dirty: JSON.stringify(draft) !== JSON.stringify(draftFrom(state.profile)), success: null });
  }
  function cancel() { if (state.profile && !state.saving) update({ draft: draftFrom(state.profile), dirty: false, editing: false, error: null, success: null }); }
  async function save() {
    if (disposed || !state.editing || !state.dirty || !state.profile || !state.draft || state.conflict || state.saving || !state.canWrite || !allowed(state.organizationId)) return false;
    if (!/^\d{4}$/.test(state.draft.founded) && state.draft.founded !== '' ||
        state.draft.founded !== '' && (Number(state.draft.founded) < 1800 || Number(state.draft.founded) > 2200)) {
      update({ error: 'Το έτος ίδρυσης πρέπει να είναι από 1800 έως 2200.' }); return false;
    }
    const organization = state.organizationId; const current = ++revision;
    const body = updateBody(state.profile, state.draft);
    update({ saving: true, error: null, success: null });
    try {
      const result = await api.PUT('/api/admin/club', { credentials: 'same-origin', headers: { 'X-Lefteria-Admin': '1', 'Content-Type': 'application/json' },
        params: { query: { organization_id: organization } }, body });
      if (disposed || current !== revision) return false;
      if (result.response.status === 401 || result.response.status === 403) { denied(result.response.status); return false; }
      if (result.response.status === 409) {
        update({ conflict: true, error: 'Τα στοιχεία άλλαξαν από άλλον διαχειριστή. Φορτώστε την τελευταία έκδοση πριν συνεχίσετε.' }); return false;
      }
      if (result.response.status === 422) { update({ error: 'Ελέγξτε τα πεδία. Οι αλλαγές σας δεν αποθηκεύτηκαν.' }); return false; }
      if (result.response.status === 404) { update({ error: 'Το προφίλ δεν υπάρχει πλέον. Οι αλλαγές σας δεν αποθηκεύτηκαν.' }); return false; }
      if (!result.response.ok) throw new Error('Save unavailable.');
      const profile = readProfile(result.data, organization);
      update({ profile, draft: draftFrom(profile), editing: false, dirty: false, conflict: false, success: 'Οι αλλαγές αποθηκεύτηκαν.' }); return true;
    } catch { if (current === revision) update({ error: 'Η αποθήκευση δεν επιβεβαιώθηκε. Οι αλλαγές σας παραμένουν στη φόρμα.' }); return false; }
    finally { if (current === revision) update({ saving: false }); }
  }
  return { subscribe: store.subscribe, getState: () => state, setAuthority, selectOrganization, load, edit, change, changeSocial, cancel, save,
    dispose() { ++revision; disposed = true; authority = null; state = { ...state, profile: null, draft: null, organizations: [], organizationId: null, dirty: false }; store.set(state); } };
}

export type ClubEditorController = ReturnType<typeof createClubEditorController>;
