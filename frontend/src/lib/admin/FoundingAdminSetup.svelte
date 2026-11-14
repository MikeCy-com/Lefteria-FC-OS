<script lang="ts">
  import { getContext } from 'svelte';
  import { ADMIN_AUTH_CONTEXT, type AdminAuthController } from './controller';

  const auth = getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  let available = $state(false);
  let open = $state(false);
  let busy = $state(false);
  let message = $state('');
  let failure = $state(false);

  $effect(() => {
    const request = new AbortController();
    void fetch('/api/admin/auth/setup', { signal: request.signal, cache: 'no-store' })
      .then(async response => {
        if (response.ok) {
          const result = await response.json();
          if (!request.signal.aborted) available = result.available === true;
        }
      })
      .catch(() => { /* Existing deployments may not expose owner setup. */ });
    return () => request.abort();
  });

  async function createOwner(event: SubmitEvent) {
    event.preventDefault();
    if (busy) return;
    const form = event.currentTarget as HTMLFormElement;
    const data = new FormData(form);
    const username = String(data.get('username') ?? '').trim().toLowerCase();
    const password = String(data.get('password') ?? '');
    busy = true;
    message = '';
    failure = false;
    try {
      const response = await fetch('/api/admin/auth/setup', {
        method: 'POST', cache: 'no-store',
        headers: { 'Content-Type': 'application/json', 'X-Lefteria-Admin': '1' },
        body: JSON.stringify({ username, name: data.get('name'), password, setup_code: data.get('setup_code') })
      });
      if (response.status === 204) {
        available = false;
        open = false;
        message = 'Ο λογαριασμός ιδιοκτήτη δημιουργήθηκε. Μπορείτε να συνδεθείτε.';
        await auth.login(username, password, null);
      } else if (response.status === 409 || response.status === 404) {
        available = false;
        open = false;
        message = 'Η αρχική ρύθμιση έχει κλείσει. Συνδεθείτε με τον λογαριασμό σας.';
      } else {
        failure = true;
        message = response.status === 429 ? 'Περιμένετε πριν δοκιμάσετε ξανά.' :
          response.status === 403 ? 'Ο κωδικός αρχικής ρύθμισης είναι λανθασμένος.' :
          response.status === 422 ? 'Ελέγξτε τα στοιχεία και το μήκος του κωδικού πρόσβασης.' :
          'Η αρχική ρύθμιση δεν είναι διαθέσιμη αυτή τη στιγμή.';
      }
    } catch {
      failure = true;
      message = 'Η σύνδεση διακόπηκε. Δοκιμάστε να συνδεθείτε ή να υποβάλετε ξανά.';
    } finally {
      form.reset();
      busy = false;
    }
  }
</script>

{#if available || message}
  <section class="admin-panel owner-setup" aria-labelledby="owner-setup-title">
    <h2 id="owner-setup-title">Αρχική ρύθμιση ιδιοκτήτη</h2>
    {#if message}<p role={failure ? 'alert' : 'status'}>{message}</p>{/if}
    {#if available && !open}
      <button type="button" class="admin-secondary" onclick={() => { open = true; }}>
        Δημιουργία λογαριασμού ιδιοκτήτη
      </button>
    {:else if available}
      <form onsubmit={createOwner} aria-busy={busy}>
        <fieldset disabled={busy}>
          <legend>Στοιχεία ιδιοκτήτη</legend>
          <label>Ονοματεπώνυμο <input name="name" autocomplete="name" maxlength="200" required /></label>
          <label>Όνομα χρήστη <input name="username" autocomplete="username" minlength="3" maxlength="80" required /></label>
          <label>Νέος κωδικός πρόσβασης <input name="password" type="password" autocomplete="new-password" minlength="12" maxlength="72" required /></label>
          <label>Κωδικός αρχικής ρύθμισης <input name="setup_code" type="password" autocomplete="off" minlength="64" maxlength="64" required /></label>
        </fieldset>
        <button class="admin-primary" disabled={busy}>Δημιουργία λογαριασμού</button>
      </form>
    {/if}
  </section>
{/if}

<style>
  .owner-setup { max-width: 32rem; margin: 1rem auto; }
  fieldset, label { display: grid; gap: .5rem; }
  fieldset { margin-bottom: 1rem; border: 0; padding: 0; min-width: 0; }
</style>
