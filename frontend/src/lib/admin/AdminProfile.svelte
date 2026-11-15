<script lang="ts">
  import type { WebAdminSession } from './controller.ts';
  import { ADMIN_ROLE_NAMES, adminCapabilityName, isAdminRole } from './permissions.ts';
  import AdminFrame from './AdminFrame.svelte';
  let { session, busy = false, onLogout, onRefresh }: {
    session: WebAdminSession; busy?: boolean; onLogout: () => void; onRefresh: () => void;
  } = $props();
  const user = $derived(session.user);
  const roleName = $derived(isAdminRole(user.role) ? ADMIN_ROLE_NAMES[user.role] : '—');
  const expiry = $derived(new Intl.DateTimeFormat('el-GR', {
    dateStyle:'medium', timeStyle:'short', timeZone:'Asia/Nicosia'
  }).format(new Date(session.expires_at)));
</script>
<AdminFrame {user} active="profile" {busy} {onLogout}>
  <h1>Το προφίλ μου</h1>
  <div class="admin-account-grid">
    <section class="admin-panel" id="account" aria-labelledby="account-title">
      <p class="admin-eyebrow">Ο ΛΟΓΑΡΙΑΣΜΟΣ ΣΑΣ</p>
      <h2 id="account-title">Στοιχεία λογαριασμού</h2>
      <dl class="admin-details">
        <div><dt>Όνομα</dt><dd>{user.name || '—'}</dd></div>
        <div><dt>Όνομα χρήστη</dt><dd>{user.username}</dd></div>
        <div><dt>Ρόλος</dt><dd data-testid="admin-role">{roleName}</dd></div>
      </dl>
    </section>
    <section class="admin-panel" id="session" aria-labelledby="session-title">
      <p class="admin-eyebrow">ΑΣΦΑΛΕΙΑ</p>
      <h2 id="session-title">Τρέχουσα σύνδεση</h2>
      <p class="admin-muted">Η σύνδεση λήγει στις <time datetime={session.expires_at}>{expiry}</time> (ώρα Κύπρου).</p>
      <button type="button" class="admin-secondary" disabled={busy} onclick={onRefresh}>Ανανέωση στοιχείων πρόσβασης</button>
    </section>
  </div>
  <section class="admin-panel" id="access" aria-labelledby="access-title">
    <p class="admin-eyebrow">ΠΡΟΣΒΑΣΗ</p><h2 id="access-title">Δικαιώματα & οργανισμοί</h2>
    {#if user.organizations.length}
      <ul class="admin-tags" data-testid="admin-organizations">
        {#each user.organizations as organization (organization.id)}<li>{organization.name}</li>{/each}
      </ul>
      <p class="admin-muted">Η πρόσβαση ελέγχεται ξανά από το API για κάθε προστατευμένη ενέργεια.</p>
    {:else}
      <p class="admin-muted">Δεν έχει ανατεθεί οργανισμός σε αυτόν τον λογαριασμό.</p>
    {/if}
    <h3>Δυνατότητες ρόλου</h3>
    <ul class="admin-permissions" data-testid="admin-permissions">
      {#each user.permissions as permission}<li>{adminCapabilityName(permission)}</li>{/each}
    </ul>
  </section>
</AdminFrame>
