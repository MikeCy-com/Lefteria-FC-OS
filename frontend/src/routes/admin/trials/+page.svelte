<script lang="ts">
  import { getContext } from 'svelte';
  import { browser } from '$app/env';
  import { goto } from '$app/navigation';
  import type { components } from '../../../lib/api/schema';
  import { ADMIN_AUTH_CONTEXT, type AdminAuthController } from '../../../lib/admin/controller.ts';
  import { canViewFirstTeamTrials } from '../../../lib/admin/trials-access.ts';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';

  type Settings = components['schemas']['FirstTeamTrialsSettings'];
  const auth = getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  let settings = $state<Settings | null>(null);
  let loading = $state(true);
  let unavailable = $state(false);

  $effect(() => {
    if (browser && $auth.status === 'anonymous') void goto('/admin/login', { replace: true });
  });

  $effect(() => {
    // Authenticate and authorize first. The view must never fetch this
    // private endpoint for an anonymous or Academy-only browser session.
    if (!browser || $auth.status !== 'authenticated' || !$auth.session ||
        !canViewFirstTeamTrials($auth.session.user)) return;

    settings = null;
    loading = true;
    unavailable = false;
    const controller = new AbortController();
    void (async () => {
      try {
        // The server rechecks the fresh cookie and current FC grant on every
        // call. No form, applicants or management writes are requested.
        const response = await fetch('/api/admin/trials/settings?organization_id=lefteria_fc', {
          method: 'GET', credentials: 'same-origin', cache: 'no-store',
          signal: controller.signal
        });
        if (!response.ok) throw new Error('Trial status unavailable');
        const result: unknown = await response.json();
        if (result && typeof result === 'object' &&
            Object.keys(result).length === 5 &&
            'open' in result && result.open === false &&
            'headline' in result && typeof result.headline === 'string' &&
            'subtitle' in result && typeof result.subtitle === 'string' &&
            'button_text' in result && typeof result.button_text === 'string' &&
            'closed_message' in result && typeof result.closed_message === 'string') {
          settings = result as Settings;
        } else throw new Error('Unverified trial status');
      } catch {
        if (!controller.signal.aborted) unavailable = true;
      } finally {
        if (!controller.signal.aborted) loading = false;
      }
    })();
    return () => controller.abort();
  });
</script>

<svelte:head><title>Δοκιμαστικά πρώτης ομάδας | Διαχείριση Λευτεριά FC</title></svelte:head>

{#if $auth.status === 'authenticated' && $auth.session}
  <AdminFrame user={$auth.session.user} active="trials" busy={$auth.busy}
    onLogout={() => void auth.logout()}>
    {#if canViewFirstTeamTrials($auth.session.user)}
      <section class="admin-panel trials-admin" data-testid="admin-trials-readonly">
        <p class="admin-eyebrow">ΠΡΩΤΗ ΟΜΑΔΑ · ΔΟΚΙΜΑΣΤΙΚΑ</p>
        <h1>Κατάσταση εγγραφών</h1>
        <p class="admin-muted">Επαληθευμένη ιδιωτική προβολή με έλεγχο πρόσβασης. Η επεξεργασία ρυθμίσεων και οι αιτήσεις αθλητών δεν έχουν ενεργοποιηθεί.</p>
        {#if loading}
          <p role="status" data-testid="admin-trials-loading">Έλεγχος κατάστασης…</p>
        {:else if unavailable || !settings}
          <p role="alert" class="trials-alert" data-testid="admin-trials-unavailable">Η επίσημη κατάσταση δεν είναι διαθέσιμη. Δεν εμφανίζουμε μη επαληθευμένες εγγραφές.</p>
        {:else}
          <div class="trials-state" data-testid="admin-trials-closed" role="status">
            <strong>ΚΛΕΙΣΤΕΣ ΕΓΓΡΑΦΕΣ</strong>
            <span>Δεν επιτρέπεται η υποβολή νέων αιτήσεων.</span>
          </div>
          <dl class="admin-details trials-fields">
            <div><dt>Τίτλος σελίδας</dt><dd>{settings.headline}</dd></div>
            <div><dt>Υπότιτλος</dt><dd>{settings.subtitle}</dd></div>
            <div><dt>Μήνυμα κλειστών εγγραφών</dt><dd>{settings.closed_message}</dd></div>
          </dl>
        {/if}
        <a href="/trials" class="admin-module-link">Προβολή δημόσιας σελίδας <span aria-hidden="true">↗</span></a>
      </section>
    {:else}
      <section class="admin-panel" data-testid="admin-trials-denied">
        <h1>Δεν έχετε πρόσβαση στα δοκιμαστικά πρώτης ομάδας.</h1>
        <a href="/admin">Επιστροφή στη διαχείριση</a>
      </section>
    {/if}
  </AdminFrame>
{:else}
  <AdminSessionStatus unavailable={$auth.status === 'unavailable'} busy={$auth.busy} error={$auth.error}
    onRetry={() => void auth.bootstrap()} onLogout={() => void auth.logout()} />
{/if}

<style>
  .trials-admin { max-width: 900px; }
  .trials-admin h1 { margin: .4rem 0 1rem; font-size: clamp(1.75rem, 3vw, 2.8rem); }
  .trials-state { display:flex;flex-wrap:wrap;align-items:center;gap:12px;margin:2rem 0;padding:20px;border-left:4px solid #f5a623;background:rgb(245 166 35 / 8%);color:#e7e7e7; }
  .trials-state strong { color:#f5a623;letter-spacing:.06em; }
  .trials-fields { margin:1.25rem 0 2rem;overflow-wrap:anywhere; }
  .trials-alert { color:#f6cb84;border:1px solid rgb(245 166 35 / 40%);padding:16px;margin:1rem 0; }
</style>
