<script lang="ts">
  import { getContext } from 'svelte';
  import { goto } from '$app/navigation';
  import { browser } from '$app/env';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';
  import { ADMIN_AUTH_CONTEXT, type AdminAuthController } from '../../../lib/admin/controller.ts';

  type Player = { id: string; name: string };
  type Entry = { id: string; status: string; status_version: number; applicant_first_name: string;
    applicant_last_name: string; applicant_birth_date: string; guardian_name: string;
    guardian_relationship: string; guardian_phone: string; guardian_email: string };
  type Order = { id: string; player_name: string; item: string; size: string | null; quantity: number; status: string };

  const auth = getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  const access = $derived($auth.status === 'authenticated' && !!$auth.session &&
    ['manager', 'super_admin'].includes($auth.session.user.role) &&
    $auth.session.user.organizations.some(org => org.id === 'lefteria_fc_academy'));
  const api = '/api/admin/academy/office/';
  let tab = $state<'registrations' | 'orders'>('registrations');
  let offset = $state(0);
  let revision = $state(0);
  let entries = $state<Entry[]>([]);
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

<svelte:head><title>Γραμματεία Ακαδημίας | Λευτεριά FC</title></svelte:head>
{#if $auth.status === 'authenticated' && $auth.session}
  <AdminFrame user={$auth.session.user} active="academy-office" busy={$auth.busy} onLogout={() => void auth.logout()}>
    {#if access}
      <h1>Γραμματεία Ακαδημίας</h1>
      <nav aria-label="Γραμματεία Ακαδημίας">
        <button class="admin-secondary" aria-pressed={tab === 'registrations'} disabled={busy}
          onclick={() => { tab = 'registrations'; offset = 0; }}>Εγγραφές παικτών</button>
        <button class="admin-secondary" aria-pressed={tab === 'orders'} disabled={busy}
          onclick={() => { tab = 'orders'; offset = 0; }}>Εσωτερικές παραγγελίες</button>
      </nav>
      {#if error}<p role="alert" class="admin-error">{error}</p>{/if}
      {#if message}<p role="status">{message}</p>{/if}
      {#key $auth.session.user.id}
        {#if tab === 'registrations'}
          <section class="admin-panel">
            <h2>Νέα αίτηση εγγραφής</h2>
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
          </section>
          <h2>Αιτήσεις εγγραφής</h2>
          <p>Η έγκριση αφορά την αίτηση. Η ένταξη στο ρόστερ ολοκληρώνεται χωριστά.</p>
          {#each entries.slice(0, 25) as row (row.id)}
            <article class="admin-panel">
              <h3>{row.applicant_first_name} {row.applicant_last_name} · {labels[row.status] ?? row.status}</h3>
              <p>{row.applicant_birth_date} · {row.guardian_name} ({row.guardian_relationship}) · {row.guardian_phone} · {row.guardian_email}</p>
              {#each transitions[row.status] ?? [] as status (status)}
                <button class="admin-secondary" disabled={busy}
                  onclick={() => void save('registrations/review', {id: row.id, status, version: row.status_version})}>
                  {labels[status]}
                </button>
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
</style>
