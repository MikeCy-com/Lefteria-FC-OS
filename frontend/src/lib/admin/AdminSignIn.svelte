<script lang="ts">
  import { publicImages } from '../public/images';
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

<main class="admin-signin admin-signin-editorial" data-testid="admin-login-page">
  <a class="admin-home-link" href="/">← Επιστροφή στην ιστοσελίδα</a>
  <div class="admin-signin-grid">
  <section class="admin-signin-story" style:background-image={`linear-gradient(180deg,#080a0b55,#080a0bf5),url("${publicImages.pitch}")`} aria-labelledby="admin-welcome-title">
    <p class="admin-eyebrow">ΛΕΥΤΕΡΙΑ FC</p>
    <h2 id="admin-welcome-title">ADMIN<br /><em>PORTAL</em></h2>
    <p class="admin-signin-lead">Η ομάδα πίσω από την ομάδα.</p>
    <p>Ο χώρος διαχείρισης του συλλόγου και της Ακαδημίας.</p>
    <img src="/mobile/club-logo.png" alt="" width="180" height="180" />
    <p class="admin-signin-motto">ONE TEAM <span aria-hidden="true">●</span> ONE DREAM</p>
  </section>
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
  </div>
</main>
