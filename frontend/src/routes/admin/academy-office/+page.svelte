<script lang="ts">
  import { getContext } from 'svelte';
  import { goto } from '$app/navigation';
  import { browser } from '$app/env';
  import { page } from '$app/state';
  import {filterRegistrations,registrationNextStep,registrationPageSummary,
    type RegistrationQueue} from '../../../lib/admin/academy-registration-triage.ts';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';
  import AcademyGuardianProofReview from '../../../lib/admin/AcademyGuardianProofReview.svelte';
  import { ADMIN_AUTH_CONTEXT, type AdminAuthController } from '../../../lib/admin/controller.ts';
  import { hasAdminPermission } from '../../../lib/admin/permissions.ts';

  type Player = { id: string; name: string };
  type Entry = { id: string; status: string; status_version: number; applicant_first_name: string;
    applicant_last_name: string; applicant_birth_date: string; guardian_name: string;
    guardian_relationship: string; guardian_phone: string; guardian_email: string;
    submission_source?: 'website' | 'staff'; onboarded_player_id?: string | null };
  type Order = { id: string; player_name: string; item: string; size: string | null; quantity: number; status: string };
  type Territory = {
    organization_id: 'lefteria_fc_academy';
    cities: {id: string; name: string; is_active: boolean}[];
    locations: {id: string; city_id: string; name: string; is_active: boolean; can_manage: boolean}[];
    groups: {id: string; city_id: string; location_id: string; name: string}[];
  };


  const auth = getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  const access = $derived($auth.status === 'authenticated' && !!$auth.session &&
    ['manager', 'super_admin'].includes($auth.session.user.role) &&
    $auth.session.user.organizations.some(org => org.id === 'lefteria_fc_academy') &&
    hasAdminPermission($auth.session.user.permissions, 'academy:read'));
  const api = '/api/admin/academy/office/';
  const canEnroll = $derived(access && !!$auth.session &&
    hasAdminPermission($auth.session.user.permissions, 'academy:write'));
  let territory = $state<Territory | null>(null);
  let territoryError = $state('');
  let territoryLoading = $state(false);
  let enrollingId = $state('');
  let cityId = $state('');
  let locationId = $state('');
  let groupId = $state('');
  let position = $state('');
  let shirtNumber = $state(0);
  let enrollmentRequestId = $state('');
  let guardianReviewId = $state('');
  let justEnrolled=$state<string|null>(null);
  const permittedCities = $derived(
    territory?.cities.filter(city => city.is_active &&
      territory?.locations.some(loc => loc.city_id === city.id && loc.can_manage && loc.is_active)) ?? []);
  const permittedLocations = $derived(
    territory?.locations.filter(loc => loc.city_id === cityId && loc.can_manage && loc.is_active) ?? []);
  const permittedGroups = $derived(
    territory?.groups.filter(group => group.city_id === cityId && group.location_id === locationId) ?? []);
  let tab = $state<'registrations' | 'orders'>('registrations');
  let applicationFilter = $state<RegistrationQueue>(
    page.url.searchParams.get('queue')==='action'?'action':'all');
  let applicationQuery = $state('');
  let offset = $state(0);
  let revision = $state(0);
  let entries = $state<Entry[]>([]);
  // The API fetches an extra row solely to show that another page exists.
  // Never count or render that look-ahead record on the current page.
  const registrationPageItems = $derived(entries.slice(0,25));
  const visibleEntries = $derived(filterRegistrations(registrationPageItems,applicationFilter,applicationQuery));
  const pageTotals = $derived(registrationPageSummary(registrationPageItems));
  let orders = $state<Order[]>([]);
  let players = $state<Player[]>([]);
  let search = $state('');
  let playerId = $state('');
  let lookedUp = $state(false);
  let loading = $state(false);
  let busy = $state(false);
  let message = $state('');
  let error = $state('');
  let requestId = $state<string | null>(null);
  const labels: Record<string, string> = {
    pending: 'Σε εκκρεμότητα', under_review: 'Υπό εξέταση', needs_information: 'Χρειάζονται στοιχεία',
    approved: 'Εγκρίθηκε', rejected: 'Απορρίφθηκε', withdrawn: 'Αποσύρθηκε',
    fulfilled: 'Παραδόθηκε', cancelled: 'Ακυρώθηκε'
  };
  const transitions: Record<string, string[]> = {
    pending: ['under_review'], under_review: ['needs_information', 'approved', 'rejected'],
    needs_information: ['under_review']
  };

  $effect(() => {
    if (browser && $auth.status === 'anonymous') void goto('/admin/login', { replace: true });
  });

  $effect(() => {
    void $auth.session?.user.id;
    void access;
    void tab;
    players = [];
    playerId = '';
    search = '';
    lookedUp = false;
    requestId = null;
    enrollingId = ''; cityId = ''; locationId = ''; groupId = '';
    position = ''; shirtNumber = 0; enrollmentRequestId = '';
    guardianReviewId = '';justEnrolled=null;
    message = '';
    error = '';
  });

  $effect(() => {
    const collection = tab;
    const page = offset;
    void revision;
    void $auth.session?.user.id;
    entries = [];
    orders = [];
    if (!access) return;
    const request = new AbortController();
    loading = true;
    void fetch(api + collection + '?offset=' + page, { signal: request.signal, cache: 'no-store' })
      .then(async response => {
        if (!response.ok) throw new Error();
        const rows = await response.json();
        if (!Array.isArray(rows)) throw new Error();
        if (request.signal.aborted) return;
        if (collection === 'registrations') entries = rows;
        else orders = rows;
      })
      .catch(() => { if (!request.signal.aborted) error = 'Η λίστα δεν είναι διαθέσιμη.'; })
      .finally(() => { if (!request.signal.aborted) loading = false; });
    return () => request.abort();
  });

  // Only one authorized territory directory is used. Server+SQL enforce
  // the actual city/location write grant again during enrollment.
  $effect(() => {
    const actor = $auth.session?.user.id;
    const allowed = canEnroll;
    territory = null;
    territoryError = '';
    territoryLoading = false;
    if (!actor || !allowed) return;
    const controller = new AbortController();
    territoryLoading = true;
    void fetch('/api/admin/football/territories?organization_id=lefteria_fc_academy', {
      credentials:'same-origin', cache:'no-store', signal:controller.signal
    }).then(async response => {
      if (!response.ok) throw new Error('territories_unavailable');
      const value: unknown = await response.json();
      if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error();
      const item = value as Record<string,unknown>;
      if (item.organization_id !== 'lefteria_fc_academy' ||
        !Array.isArray(item.cities) || !Array.isArray(item.locations) || !Array.isArray(item.groups))
        throw new Error();
      if (!controller.signal.aborted && canEnroll && $auth.session?.user.id === actor)
        territory = value as Territory;
    }).catch(() => {
      if (!controller.signal.aborted && $auth.session?.user.id === actor)
        territoryError = 'Οι επιτρεπόμενες πόλεις και τοποθεσίες δεν είναι διαθέσιμες.';
    }).finally(() => {
      if (!controller.signal.aborted && $auth.session?.user.id === actor)
        territoryLoading = false;
    });
    return () => controller.abort();
  });

  function selectForEnrollment(id: string) {
    if (!canEnroll || !territory) return;
    enrollingId = enrollingId === id ? '' : id;
    cityId = ''; locationId = ''; groupId = ''; position = '';
    shirtNumber = 0; enrollmentRequestId = '';
  }

  async function enroll(event: SubmitEvent, row: Entry) {
    event.preventDefault();
    if (busy || !canEnroll || row.status !== 'under_review' ||
        row.submission_source !== 'website' || row.id !== enrollingId ||
        !permittedGroups.some(group => group.id === groupId) ||
        !permittedLocations.some(loc => loc.id === locationId) ||
        !permittedCities.some(city => city.id === cityId) ||
        !['Goalkeeper','Defender','Midfielder','Forward'].includes(position) ||
        !Number.isInteger(shirtNumber) || shirtNumber < 1 || shirtNumber > 99) return;
    const actor = $auth.session?.user.id;
    const id = row.id, version = row.status_version;
    const selectedCity = cityId, selectedLocation = locationId, selectedGroup = groupId;
    const selectedPosition = position, selectedNumber = shirtNumber;
    enrollmentRequestId ||= crypto.randomUUID();
    const requestId = enrollmentRequestId;
    busy = true;
    error = ''; message = '';justEnrolled=null;
    try {
      const response = await fetch(api + 'registrations/enroll', {
        method:'POST', credentials:'same-origin', cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({id,version,request_id:requestId,
          city_id:selectedCity,location_id:selectedLocation,group_id:selectedGroup,
          position:selectedPosition,number:selectedNumber})
      });
      if ($auth.session?.user.id !== actor || !canEnroll) return;
      if (response.status === 401 || response.status === 403) {
        void auth.bootstrap();
        throw new Error('Η πρόσβαση ή οι εξουσιοδοτήσεις άλλαξαν. Ελέγξτε τις εγγραφές.');
      }
      if (response.status === 409) {
        revision++;
        throw new Error('Η αίτηση άλλαξε, ή υπάρχει πιθανή διπλή εγγραφή παιδιού. Απαιτείται έλεγχος πριν από νέα προσπάθεια.');
      }
      if (!response.ok) throw new Error(response.status === 503
        ? 'Η σύνδεση με το κανονικό ρόστερ δεν είναι διαθέσιμη.'
        : 'Η εγγραφή δεν εγκρίθηκε και δεν προστέθηκε στο ρόστερ. Ελέγξτε συγκαταθέσεις και στοιχεία.');
      const value: unknown = await response.json();
      if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('Μη επαληθευμένη απάντηση.');
      const result = value as Record<string,unknown>;
      if (result.id !== id || result.group_id !== selectedGroup ||
          result.city_id !== selectedCity || result.location_id !== selectedLocation ||
          result.status !== 'approved' || typeof result.player_id !== 'string')
        throw new Error('Δεν επαληθεύτηκε η αποθήκευση. Ελέγξτε τη λίστα.');
      enrollingId = ''; enrollmentRequestId = '';justEnrolled=result.player_id;
      message = 'Η έγκριση και η προσθήκη στο ρόστερ ολοκληρώθηκαν.';
      revision++;
    } catch (cause) {
      if ($auth.session?.user.id === actor) error = cause instanceof Error
        ? cause.message : 'Η ένταξη δεν επιβεβαιώθηκε. Ελέγξτε την αίτηση.';
    } finally { busy = false; }
  }

  async function searchPlayers(event: SubmitEvent) {
    event.preventDefault();
    if (busy || !access) return;
    const actor = $auth.session?.user.id;
    busy = true;
    players = [];
    playerId = '';
    lookedUp = false;
    error = '';
    try {
      const response = await fetch(api + 'players?search=' + encodeURIComponent(search.trim()), { cache: 'no-store' });
      if (!response.ok) throw new Error();
      const rows = await response.json();
      if (!Array.isArray(rows)) throw new Error();
      if (access && $auth.session?.user.id === actor) { players = rows; lookedUp = true; }
    } catch { if ($auth.session?.user.id === actor) error = 'Η αναζήτηση παικτών απέτυχε.'; }
    finally { busy = false; }
  }

  async function save(path: string, body: object) {
    if (busy || !access) return false;
    const actor = $auth.session?.user.id;
    busy = true;
    error = '';
    message = '';
    try {
      const response = await fetch(api + path, { method: 'POST', cache: 'no-store',
        headers: { 'Content-Type': 'application/json', 'X-Lefteria-Admin': '1' },
        body: JSON.stringify(body) });
      if (!access || $auth.session?.user.id !== actor) return false;
      if (response.status === 409) {
        revision++;
        error = 'Η εγγραφή υπάρχει ήδη ή άλλαξε από άλλον χρήστη. Ελέγξτε τη λίστα.';
        requestId = null;
        return false;
      }
      if (!response.ok) {
        error = response.status === 422 ? 'Ελέγξτε τα στοιχεία.' : 'Η καταχώριση δεν ολοκληρώθηκε.';
        return false;
      }
      message = 'Η καταχώριση αποθηκεύτηκε.';
      if (path === 'registrations' || path === 'orders') offset = 0;
      revision++;
      return true;
    } catch {
      if ($auth.session?.user.id === actor) error = 'Η σύνδεση διακόπηκε. Ελέγξτε τη λίστα και δοκιμάστε ξανά.';
      return false;
    } finally { busy = false; }
  }

  async function create(event: SubmitEvent) {
    event.preventDefault();
    if (busy || !access) return;
    const form = event.currentTarget as HTMLFormElement;
    const fields = Object.fromEntries(new FormData(form));
    requestId ??= crypto.randomUUID();
    const saved = await save(tab, { ...fields, id: requestId,
      ...(tab === 'orders' ? { quantity: Number(fields.quantity) } : {}) });
    if (saved) {
      form.reset();
      playerId = '';
      requestId = null;
    }
  }
</script>

<svelte:head><title>Εγγραφές Ακαδημίας | Λευτεριά FC</title></svelte:head>
{#if $auth.status === 'authenticated' && $auth.session}
  <AdminFrame user={$auth.session.user} active="academy-office" busy={$auth.busy} onLogout={() => void auth.logout()}>
    {#if access}
      <h1>Εγγραφές Ακαδημίας</h1>
      <p class="admin-muted registration-intro">Οι ηλεκτρονικές αιτήσεις από τη δημόσια ιστοσελίδα και οι χειροκίνητες καταχωρίσεις εμφανίζονται στην ίδια λίστα. Η έγκριση & ένταξη ηλεκτρονικής αίτησης δημιουργεί τον αθλητή στο επιλεγμένο τμήμα. Ο λογαριασμός γονέα και η ενεργοποίηση χρεώσεων απαιτούν ξεχωριστή επαλήθευση.</p>
      {#if $auth.session.user.role !== 'super_admin'}
        <p class="admin-muted">Οι διαχειριστές βλέπουν μόνο τις αιτήσεις που καταχώρισαν οι ίδιοι. Οι νέες δημόσιες αιτήσεις διατίθενται μόνο στον Super Admin μέχρι να ενεργοποιηθεί η ασφαλής ανάθεση ανά πόλη και τοποθεσία.</p>
      {/if}
      <nav aria-label="Εγγραφές Ακαδημίας">
        <button class="admin-secondary" aria-pressed={tab === 'registrations'} disabled={busy}
          onclick={() => { tab = 'registrations'; offset = 0; }}>Εγγραφές παικτών</button>
        <button class="admin-secondary" aria-pressed={tab === 'orders'} disabled={busy}
          onclick={() => { tab = 'orders'; offset = 0; }}>Εσωτερικές παραγγελίες</button>
      </nav>
      {#if error}<p role="alert" class="admin-error">{error}</p>{/if}
      {#if message}<p role="status">{message}</p>{/if}
      {#if justEnrolled && canEnroll}<a class="admin-primary" data-testid="newly-enrolled-player-360" data-sveltekit-reload href={'/admin/groups?'+new URLSearchParams({organization_id:'lefteria_fc_academy',view:'players',player_id:justEnrolled}).toString()}>Άνοιγμα νέου Player 360 ›</a>{/if}
      {#key $auth.session.user.id}
        {#if tab === 'registrations'}
          <details class="admin-panel manual-registration">
            <summary>Χειροκίνητη καταχώριση αίτησης</summary>
            <p class="admin-muted">Για αιτήσεις που παραλήφθηκαν εκτός της δημόσιας φόρμας. Δεν προστίθεται παίκτης στο ρόστερ.</p>
            <form onsubmit={create}>
              <fieldset disabled={busy}>
                <legend>Στοιχεία παίκτη και κηδεμόνα</legend>
                <label>Όνομα παίκτη <input name="first_name" maxlength="100" required /></label>
                <label>Επώνυμο παίκτη <input name="last_name" maxlength="100" required /></label>
                <label>Ημερομηνία γέννησης <input name="birth_date" type="date" min="2000-01-01" required /></label>
                <label>Ονοματεπώνυμο κηδεμόνα <input name="guardian_name" maxlength="200" required /></label>
                <label>Σχέση με τον παίκτη <input name="guardian_relationship" maxlength="80" required /></label>
                <label>Τηλέφωνο κηδεμόνα <input name="guardian_phone" type="tel" minlength="6" maxlength="32" required /></label>
                <label>Email κηδεμόνα <input name="guardian_email" type="email" maxlength="254" required /></label>
              </fieldset>
              <button class="admin-primary" disabled={busy}>Καταχώριση αίτησης</button>
            </form>
          </details>
          <div class="academy-registration-inbox-head">
            <div>
              <p class="admin-eyebrow">ΑΙΤΗΣΕΙΣ · ΕΛΕΓΧΟΣ · ΕΝΤΑΞΗ</p>
              <h2 data-testid="academy-registration-unified-inbox">Αιτήσεις εγγραφής</h2>
            </div>
            <button type="button" class="admin-secondary" disabled={busy||loading}
              onclick={()=>revision++} data-testid="academy-inbox-refresh">Ανανέωση λίστας</button>
          </div>
          <div class="academy-registration-summary" data-testid="academy-registration-page-summary">
            <div><strong>{pageTotals.loaded}</strong><span>Φορτωμένες αιτήσεις</span></div>
            <div><strong>{pageTotals.needsAttention}</strong><span>Χρειάζονται ενέργεια</span></div>
            <div><strong>{pageTotals.linked}</strong><span>Με καταγεγραμμένη ένταξη</span></div>
            <div><strong>{pageTotals.awaitingGuardianInfo}</strong><span>Αναμονή στοιχείων</span></div>
          </div>
          <p class="admin-muted academy-inbox-limits">
            Οι αριθμοί αφορούν μόνο τις αιτήσεις της τρέχουσας εξουσιοδοτημένης σελίδας
            και δεν αποτελούν συνολικά στατιστικά πόλης ή Ακαδημίας.
          </p>
          <div class="academy-registration-filters" data-testid="academy-registration-triage">
            <label for="academy-inbox-status">Κατάσταση εργασίας
              <select id="academy-inbox-status" bind:value={applicationFilter}
                disabled={loading||busy} data-testid="academy-inbox-status">
                <option value="action">Χρειάζονται ενέργεια</option>
                <option value="all">Όλες της σελίδας</option>
                <option value="pending">Νέες</option>
                <option value="under_review">Υπό έλεγχο</option>
                <option value="needs_information">Χρειάζονται στοιχεία</option>
                <option value="approved">Εγκεκριμένες</option>
                <option value="closed">Απορρίφθηκαν / αποσύρθηκαν</option>
              </select>
            </label>
            <label for="academy-inbox-query">Αναζήτηση παιδιού ή κηδεμόνα
              <input id="academy-inbox-query" type="search" maxlength="100"
                bind:value={applicationQuery} autocomplete="off"
                placeholder="Όνομα παιδιού, κηδεμόνα ή ID"
                data-testid="academy-inbox-search"/>
            </label>
            <button type="button" class="admin-secondary" disabled={busy||loading}
              onclick={()=>{applicationFilter='all';applicationQuery='';}}
              data-testid="academy-inbox-clear">Εκκαθάριση φίλτρων</button>
          </div>
          {#if territoryLoading}<p class="admin-muted" role="status">Φόρτωση επιτρεπόμενων τοποθεσιών…</p>
          {:else if territoryError}<p role="alert" class="admin-error">{territoryError}</p>{/if}
          <p>Ελέγξτε τα στοιχεία και την κατάσταση κάθε αίτησης. Η ένταξη σε συγκεκριμένο τμήμα απαιτεί ξεχωριστή επαληθευμένη διαδικασία στα <a href="/admin/groups">Ομάδες & τμήματα</a>· η απλή έγκριση δεν είναι προσθήκη στο ρόστερ.</p>
          {#if !loading && entries.length>0 && visibleEntries.length===0}
            <p class="admin-muted academy-inbox-empty" role="status"
              data-testid="academy-inbox-no-filter-results">
              Δεν βρέθηκαν αιτήσεις στην τρέχουσα σελίδα με αυτά τα φίλτρα.
              Μπορείτε να τα καθαρίσετε ή να αλλάξετε σελίδα.
            </p>
          {/if}
          {#each visibleEntries.slice(0, 25) as row (row.id)}
            <article class="admin-panel academy-registration-card"
              data-testid="academy-registration-triage-card">
              <div class="academy-registration-card-heading">
                <div>
                  <p class="admin-eyebrow">{row.submission_source==='website'?'ONLINE ΑΙΤΗΣΗ':'ΕΓΓΡΑΦΗ ΑΚΑΔΗΜΙΑΣ'}</p>
                  <h3>{row.applicant_first_name} {row.applicant_last_name}</h3>
                  <small class="registration-source">Αριθμός αίτησης: {row.id}</small>
                </div>
                <strong class="academy-registration-status" data-status={row.status}>
                  {labels[row.status] ?? row.status}
                </strong>
              </div>
              <div class="academy-registration-card-details">
                <div><span>Ημερομηνία γέννησης</span><strong>{row.applicant_birth_date}</strong></div>
                <div><span>Κηδεμόνας · {row.guardian_relationship}</span><strong>{row.guardian_name}</strong></div>
                <div><span>Στοιχεία επικοινωνίας</span>
                  <strong>{row.guardian_phone}</strong><small>{row.guardian_email}</small></div>
              </div>
              <p class="academy-registration-next" data-testid="academy-registration-next-step">
                <strong>Επόμενο βήμα:</strong> {registrationNextStep(row)}
              </p>
              {#if row.status === 'approved'}
                <p class="admin-muted">{row.onboarded_player_id ? 'Η σύνδεση στο ρόστερ έχει καταγραφεί.' : 'Εγκεκριμένη αίτηση · η τοποθέτηση του παιδιού σε πόλη, τοποθεσία και τμήμα παραμένει σε εκκρεμότητα.'}</p>
                {#if row.onboarded_player_id && canEnroll}
                  <a class="admin-primary" data-testid="approved-player-360" data-sveltekit-reload
                    href={'/admin/groups?'+new URLSearchParams({
                      organization_id:'lefteria_fc_academy',view:'players',player_id:row.onboarded_player_id
                    }).toString()}>
                    Άνοιγμα Player 360 · {row.applicant_first_name} ›
                  </a>
                  <a class="admin-secondary" data-testid="approved-player-documents"
                    href={'/admin/documents?'+new URLSearchParams({
                      organization_id:'lefteria_fc_academy',
                      player_id:row.onboarded_player_id
                    }).toString()}>
                    <span aria-hidden="true">▣</span> Προστατευμένα έγγραφα {row.applicant_first_name}
                  </a>
                {/if}
                {#if $auth.session.user.role === 'super_admin' &&
                    row.onboarded_player_id && row.submission_source === 'website'}
                  <button type="button" class="admin-secondary" disabled={busy}
                    aria-expanded={guardianReviewId === row.id}
                    onclick={() => guardianReviewId = guardianReviewId === row.id ? '' : row.id}>
                    {guardianReviewId === row.id ? 'Κλείσιμο ελέγχου κηδεμόνα' : 'Έλεγχος επαλήθευσης κηδεμόνα'}
                  </button>
                  {#if guardianReviewId === row.id}
                    <AcademyGuardianProofReview registrationId={row.id}
                      guardianPhone={row.guardian_phone} />
                  {/if}
                {/if}
              {/if}
              {#if row.status === 'under_review' && row.submission_source === 'website'}
                {#if canEnroll && territory}
                  <button type="button" class="admin-secondary" disabled={busy}
                    aria-expanded={enrollingId === row.id}
                    onclick={() => selectForEnrollment(row.id)}>
                    {enrollingId === row.id ? 'Κλείσιμο ένταξης' : 'Επιλογή πόλης, τοποθεσίας & τμήματος'}
                  </button>
                  {#if enrollingId === row.id}
                    <form class="enrollment-form" onsubmit={event => void enroll(event, row)}
                      data-testid="crm-approve-enroll-form">
                      <p class="admin-muted">Για ασφάλεια, η τελική ένταξη επιτρέπεται μόνο μετά την επαλήθευση της ηλεκτρονικής αίτησης και των συγκαταθέσεων. Η ενέργεια είναι ατομική.</p>
                      <label>Πόλη
                        <select required bind:value={cityId}
                          onchange={() => { locationId=''; groupId=''; enrollmentRequestId=''; }}>
                          <option value="">Επιλέξτε πόλη</option>
                          {#each permittedCities as city (city.id)}
                            <option value={city.id}>{city.name}</option>
                          {/each}
                        </select>
                      </label>
                      <label>Τοποθεσία
                        <select required bind:value={locationId}
                          onchange={() => { groupId=''; enrollmentRequestId=''; }}>
                          <option value="">Επιλέξτε τοποθεσία</option>
                          {#each permittedLocations as loc (loc.id)}
                            <option value={loc.id}>{loc.name}</option>
                          {/each}
                        </select>
                      </label>
                      <label>Τμήμα
                        <select required bind:value={groupId} onchange={() => enrollmentRequestId=''}>
                          <option value="">Επιλέξτε τμήμα</option>
                          {#each permittedGroups as group (group.id)}
                            <option value={group.id}>{group.name}</option>
                          {/each}
                        </select>
                      </label>
                      <label>Επιβεβαιωμένη θέση παίκτη
                        <select required bind:value={position} onchange={() => enrollmentRequestId=''}>
                          <option value="">Επιλέξτε θέση</option>
                          <option value="Goalkeeper">Τερματοφύλακας</option>
                          <option value="Defender">Αμυντικός</option>
                          <option value="Midfielder">Μέσος</option>
                          <option value="Forward">Επιθετικός</option>
                        </select>
                      </label>
                      <label>Επιβεβαιωμένος αριθμός φανέλας
                        <input type="number" required min="1" max="99" bind:value={shirtNumber}
                          oninput={() => enrollmentRequestId=''} />
                      </label>
                      <button class="admin-primary" disabled={busy || !groupId || !position ||
                        shirtNumber < 1 || shirtNumber > 99}>
                        Έγκριση & ένταξη στο ρόστερ
                      </button>
                    </form>
                  {/if}
                {:else}<p class="admin-muted">Για οριστική ένταξη χρειάζεται διαθέσιμη, εξουσιοδοτημένη τοποθεσία.</p>{/if}
              {:else if row.status === 'under_review' && row.submission_source === 'staff'}
                <p class="admin-muted">Η χειροκίνητη αίτηση δεν περιέχει ακόμη επαληθευμένο αποδεικτικό συγκατάθεσης κηδεμόνα. Η έγκρισή της δεν δημιουργεί αθλητή.</p>
              {/if}
              {#each transitions[row.status] ?? [] as status (status)}
                {#if status !== 'approved' || row.submission_source !== 'website'}
                  <button class="admin-secondary" disabled={busy}
                    onclick={() => void save('registrations/review', {id: row.id, status, version: row.status_version})}>
                    {status === 'approved' ? 'Έγκριση αίτησης μόνο (χωρίς ένταξη)' : labels[status]}
                  </button>
                {/if}
              {/each}
            </article>
          {/each}
        {:else}
          <section class="admin-panel">
            <h2>Νέα εσωτερική παραγγελία</h2>
            <form onsubmit={searchPlayers}>
              <label>Αναζήτηση παίκτη <input bind:value={search} maxlength="100" required /></label>
              <button class="admin-secondary" disabled={busy}>Αναζήτηση</button>
            </form>
            {#if lookedUp && !players.length}<p>Δεν βρέθηκαν παίκτες.</p>{/if}
            <form onsubmit={create}>
              <fieldset disabled={busy}>
                <legend>Στοιχεία παραγγελίας</legend>
                <label>Παίκτης <select name="player_id" bind:value={playerId} required>
                  <option value="">Επιλέξτε παίκτη από την αναζήτηση</option>
                  {#each players as player (player.id)}<option value={player.id}>{player.name}</option>{/each}
                </select></label>
                <label>Είδος <input name="item" placeholder="π.χ. Σετ προπόνησης" maxlength="120" required /></label>
                <label>Μέγεθος <input name="size" placeholder="π.χ. 152 ή M" maxlength="40" /></label>
                <label>Ποσότητα <input name="quantity" type="number" min="1" max="100" value="1" required /></label>
              </fieldset>
              <button class="admin-primary" disabled={busy || !playerId}>Δημιουργία παραγγελίας</button>
            </form>
          </section>
          <h2>Παραγγελίες</h2>
          {#each orders.slice(0, 25) as row (row.id)}
            <article class="admin-panel">
              <h3>{row.player_name} · {row.item}</h3>
              <p>{row.quantity}{row.size ? ' × ' + row.size : ''} · {labels[row.status] ?? row.status}</p>
              {#if row.status === 'pending'}
                <button class="admin-secondary" disabled={busy} onclick={() => void save('orders/status', {id:row.id,status:'fulfilled',version:0})}>Παραδόθηκε</button>
                <button class="admin-secondary" disabled={busy}
                  onclick={() => { if (window.confirm('Να ακυρωθεί η παραγγελία;')) void save('orders/status', {id:row.id,status:'cancelled',version:0}); }}>Ακύρωση</button>
              {/if}
            </article>
          {/each}
        {/if}
      {/key}
      {#if loading}<p role="status">Φόρτωση…</p>
      {:else if (tab === 'orders' ? orders : entries).length === 0 && !error}<p>Δεν υπάρχουν καταχωρίσεις.</p>{/if}
      {#if offset > 0}<button class="admin-secondary" disabled={loading || busy} onclick={() => offset -= 25}>Προηγούμενη</button>{/if}
      {#if (tab === 'orders' ? orders : entries).length > 25}
        <button class="admin-secondary" disabled={loading || busy || offset >= 20000} onclick={() => offset += 25}>Επόμενη</button>
      {/if}
    {:else}<p role="alert">Η σελίδα είναι διαθέσιμη σε διαχειριστές της Ακαδημίας.</p>{/if}
  </AdminFrame>
{:else}
  <AdminSessionStatus unavailable={$auth.status === 'unavailable'} busy={$auth.busy} error={$auth.error}
    onRetry={() => void auth.bootstrap()} onLogout={() => void auth.logout()} />
{/if}

<style>
  nav, form, fieldset { display: flex; flex-wrap: wrap; gap: 1rem; }
  form { flex-direction: column; align-items: flex-start; margin-block: 1rem; }
  fieldset { width: 100%; border: 0; padding: 0; margin: 0; min-width: 0; }
  label { display: grid; gap: .3rem; min-width: min(100%, 15rem); }
  article p { overflow-wrap: anywhere; }
  .registration-intro{max-width:75ch;margin:0 0 18px}
  .manual-registration{margin:18px 0}
  .manual-registration summary{cursor:pointer;font-weight:700;font-size:16px}
  .manual-registration[open] summary{margin-bottom:12px}
  .registration-source{font-size:12px;color:#aeb4b9;overflow-wrap:anywhere}
  .enrollment-form{border-top:1px solid #594824;padding-top:18px;display:grid;gap:14px;max-width:660px}
  .enrollment-form label{min-width:min(100%,20rem)}
  .enrollment-form select,.enrollment-form input{max-width:100%}
  .academy-registration-inbox-head{display:flex;justify-content:space-between;
    align-items:center;flex-wrap:wrap;gap:12px;margin-top:24px}
  .academy-registration-inbox-head h2{margin:0}
  .academy-registration-inbox-head .admin-eyebrow{margin:0 0 4px}
  .academy-registration-inbox-head button{min-height:44px}
  .academy-registration-summary{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));
    gap:10px;margin:16px 0 6px}
  .academy-registration-summary>div{display:grid;gap:6px;padding:15px;
    background:var(--admin-surface,#151515);
    border:1px solid var(--admin-border,#38332b);border-radius:10px}
  .academy-registration-summary strong{font-size:24px;font-variant-numeric:tabular-nums;
    line-height:1.15;color:var(--admin-gold,#f6bb38)}
  .academy-registration-summary span{font-size:12px;color:var(--admin-muted,#b4bab3)}
  .academy-inbox-limits{font-size:12px;max-width:80ch;line-height:1.5}
  .academy-registration-filters{display:flex;gap:12px;align-items:end;flex-wrap:wrap;
    margin:16px 0}
  .academy-registration-filters label{flex:1;min-width:min(100%,210px);font-size:13px}
  .academy-registration-filters input,.academy-registration-filters select{min-height:44px}
  .academy-registration-filters button{min-height:44px}
  .academy-inbox-empty{padding:18px;border:1px dashed var(--admin-border,#555);
    border-radius:9px;font-size:13px}
  .academy-registration-card{display:grid;gap:14px}
  .academy-registration-card-heading{display:flex;justify-content:space-between;
    gap:10px;align-items:start;flex-wrap:wrap}
  .academy-registration-card-heading h3{font-size:22px;margin:0 0 4px}
  .academy-registration-card-heading .admin-eyebrow{margin:0 0 4px}
  .academy-registration-status{font-size:12px;padding:6px 10px;
    border-radius:6px;border:1px solid var(--admin-gold,#f6bb38);
    color:var(--admin-gold,#f6bb38)}
  .academy-registration-status[data-status="rejected"],
  .academy-registration-status[data-status="withdrawn"]{
    border-color:var(--admin-muted,#bbb);color:var(--admin-muted,#bbb)}
  .academy-registration-card-details{display:grid;
    grid-template-columns:repeat(auto-fit,minmax(min(100%,180px),1fr));gap:10px}
  .academy-registration-card-details>div{display:grid;gap:5px;padding:12px;
    border:1px solid var(--admin-border,#3d3a33);border-radius:8px;
    background:var(--admin-surface,#171717)}
  .academy-registration-card-details span,.academy-registration-card-details small{
    color:var(--admin-muted,#afb5b7);font-size:12px}
  .academy-registration-card-details strong{font-size:13px;overflow-wrap:anywhere}
  .academy-registration-next{padding:12px 15px;
    border-inline-start:3px solid var(--admin-gold,#f6bb38);
    background:var(--admin-surface,#161616);font-size:13px;margin:0}
  @media(max-width:640px){.academy-registration-summary{grid-template-columns:repeat(2,minmax(0,1fr))}
    .academy-registration-filters{display:grid;grid-template-columns:1fr}
    .academy-registration-card-details{grid-template-columns:1fr}}

</style>
