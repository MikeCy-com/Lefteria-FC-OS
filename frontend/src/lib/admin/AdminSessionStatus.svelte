<script lang="ts">
  let { unavailable, busy, error, onRetry, onLogout }: {
    unavailable: boolean; busy: boolean; error: string | null;
    onRetry: () => void; onLogout: () => void;
  } = $props();
</script>

<main class="admin-signin">
  <a class="admin-home-link" href="/">← Επιστροφή στην ιστοσελίδα</a>
  <section class="admin-panel" aria-busy={busy}>
    <h1>Διαχείριση</h1>
    {#if unavailable}
      <p class="admin-error" role="alert">{error}</p>
      <div class="admin-actions">
        <button class="admin-primary" type="button" disabled={busy} onclick={onRetry}>Δοκιμή ξανά</button>
        <button class="admin-secondary" type="button" disabled={busy} onclick={onLogout}>Αποσύνδεση</button>
      </div>
    {:else}<p role="status">Έλεγχος σύνδεσης…</p>{/if}
  </section>
</main>
