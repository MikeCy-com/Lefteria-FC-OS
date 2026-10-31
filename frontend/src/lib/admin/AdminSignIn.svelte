<script lang="ts">
  import type { AdminRememberDays } from './controller.ts';

  let { busy = false, error = null, onLogin }: {
    busy?: boolean; error?: string | null;
    onLogin: (username: string, password: string, rememberDays: AdminRememberDays) => Promise<boolean>;
  } = $props();
  let username = $state('');
  let password = $state('');
  let rememberDays = $state<AdminRememberDays>(null);
  let showPassword = $state(false);

  async function submit(event: SubmitEvent) {
    event.preventDefault();
    if (busy) return;
    try { await onLogin(username, password, rememberDays); }
    finally { password = ''; }
  }
</script>

<main class="admin-signin" data-testid="admin-login-page">
  <a class="admin-home-link" href="/">← Επιστροφή στην ιστοσελίδα</a>
  <section class="admin-panel">
    <img class="admin-crest" src="/mobile/club-logo.png" alt="Λευτεριά FC" width="72" height="72" />
    <p class="admin-eyebrow">LEFTERIA FC OS</p>
    <h1>Σύνδεση διαχείρισης</h1>
    <p class="admin-muted">Συνδεθείτε με τον λογαριασμό διαχείρισής σας.</p>
    {#if error}<p class="admin-error" role="alert" data-testid="admin-login-error">{error}</p>{/if}
    <form onsubmit={submit} aria-busy={busy}>
      <label for="admin-username">Όνομα χρήστη</label>
      <input id="admin-username" name="username" bind:value={username} required maxlength="80"
        autocomplete="username" autocapitalize="none" spellcheck="false" disabled={busy} data-testid="admin-login-username" />
      <label for="admin-password">Κωδικός</label>
      <div class="admin-password-field">
        <input id="admin-password" name="password" type={showPassword ? 'text' : 'password'} bind:value={password}
          required autocomplete="current-password" disabled={busy} data-testid="admin-login-password" />
        <button type="button" class="admin-secondary" disabled={busy} onclick={() => showPassword = !showPassword}
          aria-controls="admin-password" aria-pressed={showPassword} aria-label={showPassword ? 'Απόκρυψη κωδικού' : 'Εμφάνιση κωδικού'}>
          {showPassword ? 'Απόκρυψη' : 'Εμφάνιση'}
        </button>
      </div>
      <label for="admin-remember">Διάρκεια σύνδεσης σε αυτή τη συσκευή</label>
      <select id="admin-remember" bind:value={rememberDays} disabled={busy}>
        <option value={null}>24 ώρες</option>
        <option value={30}>30 ημέρες</option>
        <option value={60}>60 ημέρες</option>
        <option value={90}>90 ημέρες</option>
      </select>
      <button class="admin-primary" type="submit" disabled={busy} data-testid="admin-login-submit">
        {busy ? 'Σύνδεση…' : 'Σύνδεση'}
      </button>
    </form>
  </section>
</main>
