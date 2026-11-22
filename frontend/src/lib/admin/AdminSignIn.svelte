<script lang="ts">
  import PublicIcon from '../public/PublicIcon.svelte';
  import { publicImages } from '../public/images.ts';
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
  <header class="admin-access-header">
    <a href="/" class="admin-access-brand"><img src="/mobile/club-logo.png" alt="" width="44" height="44" /><span>ΛΕΥΤΕΡΙΑ <em>FC</em></span></a>
    <a class="admin-home-link" href="/">Επιστροφή στην ιστοσελίδα <PublicIcon name="arrow" size={20} /></a>
  </header>
  <div class="admin-signin-grid">
  <section class="admin-signin-story" style:background-image={`linear-gradient(180deg,#080a0b55,#080a0bf5),url("${publicImages.hero}")`} aria-labelledby="admin-welcome-title">
    
    <h2 id="admin-welcome-title">ADMIN PORTAL</h2>
    <p class="admin-signin-lead">Διαχείριση λογαριασμού</p>
    <p>Πρόσβαση στο σύστημα διαχείρισης της Λευτεριάς FC για τη διοίκηση, το προπονητικό επιτελείο και το προσωπικό Ακαδημίας.</p>
    <div class="admin-access-roles" aria-label="Ρόλοι διαχείρισης">
      <span><PublicIcon name="shield" size={24} /> SUPER ADMIN</span>
      <span><PublicIcon name="academy" size={24} /> COACH</span>
      <span><PublicIcon name="users" size={24} /> STAFF</span>
    </div>
    <p class="admin-signin-motto">ONE TEAM <span aria-hidden="true">●</span> ONE DREAM</p>
  </section>
  <section class="admin-panel">
    <img class="admin-crest" src="/mobile/club-logo.png" alt="Λευτεριά FC" width="72" height="72" />
    <p class="admin-access-wordmark">ΛΕΥΤΕΡΙΑ <em>FC</em></p>
    <p class="admin-eyebrow">ADMIN PORTAL</p>
    <h1>Διαχείριση λογαριασμού</h1>
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
        {busy ? 'Σύνδεση…' : 'ΕΙΣΟΔΟΣ ΣΤΗ ΔΙΑΧΕΙΡΙΣΗ'} <PublicIcon name="arrow" size={22} />
      </button>
    </form>
  </section>
  </div>
  <footer class="admin-access-footer"><span>ΛΕΥΤΕΡΙΑ FC · Σύστημα Διαχείρισης Συλλόγου</span><a href="/privacy">Πολιτική απορρήτου</a><a href="/contact">Επικοινωνήστε με τον σύλλογο <PublicIcon name="arrow" size={18} /></a></footer>
</main>
