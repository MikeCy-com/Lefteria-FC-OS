<script lang="ts">
  import type { WebAdminSession } from './controller.ts';
  import { ADMIN_ROLE_NAMES, adminCapabilityName, isAdminRole, pendingAdminModules } from './permissions.ts';

  let { session, busy = false, onLogout, onRefresh }: {
    session: WebAdminSession; busy?: boolean; onLogout: () => void; onRefresh: () => void;
  } = $props();
  const user = $derived(session.user);
  const roleName = $derived(isAdminRole(user.role) ? ADMIN_ROLE_NAMES[user.role] : '—');
  const modules = $derived(pendingAdminModules(user.permissions, user.organizations.map(org => org.id)));
  const expiry = $derived(new Intl.DateTimeFormat('el-GR', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'Asia/Nicosia' }).format(new Date(session.expires_at)));
</script>

<div class="admin-workspace" data-testid="admin-workspace">
  <header class="admin-header">
    <a class="admin-brand" href="/admin"><img src="/mobile/club-logo.png" alt="" width="40" height="40" /><span>LEFTERIA FC OS <small>Διαχείριση</small></span></a>
    <div class="admin-actions"><a href="/">Ιστοσελίδα</a><button type="button" class="admin-secondary" disabled={busy} onclick={onLogout} data-testid="admin-logout">Αποσύνδεση</button></div>
  </header>
  <div class="admin-workspace-body">
    <nav class="admin-sidebar" aria-label="Διαχείριση λογαριασμού">
      <a href="#account">Λογαριασμός</a><a href="#session">Σύνδεση</a><a href="#access">Δικαιώματα πρόσβασης</a>
    </nav>
    <main class="admin-content">
      <p class="admin-eyebrow">ΔΙΑΧΕΙΡΙΣΗ</p><h1>Καλώς ήρθατε, {user.name || user.username}</h1>
      <p class="admin-muted">Ο λογαριασμός, η σύνδεση και τα τρέχοντα δικαιώματά σας.</p>
      <section class="admin-panel" id="account" aria-labelledby="account-title">
        <h2 id="account-title">Λογαριασμός</h2>
        <dl class="admin-details"><div><dt>Όνομα</dt><dd>{user.name || '—'}</dd></div><div><dt>Όνομα χρήστη</dt><dd>{user.username}</dd></div><div><dt>Ρόλος</dt><dd data-testid="admin-role">{roleName}</dd></div></dl>
      </section>
      <section class="admin-panel" id="session" aria-labelledby="session-title">
        <h2 id="session-title">Σύνδεση</h2>
        <p>Η τρέχουσα σύνδεση λήγει στις <time datetime={session.expires_at}>{expiry}</time> (ώρα Κύπρου).</p>
        <button type="button" class="admin-secondary" disabled={busy} onclick={onRefresh}>Ανανέωση στοιχείων πρόσβασης</button>
      </section>
      <section class="admin-panel" id="access" aria-labelledby="access-title">
        <h2 id="access-title">Δικαιώματα πρόσβασης</h2>
        <h3>Οργανισμοί</h3>
        {#if user.organizations.length}
          <ul class="admin-tags" data-testid="admin-organizations">{#each user.organizations as organization (organization.id)}<li>{organization.name}</li>{/each}</ul>
          <p class="admin-muted">Η πρόσβαση διαχείρισης καλύπτει τους παραπάνω οργανισμούς σύμφωνα με τον ρόλο σας.</p>
        {:else}<p>Δεν έχει ανατεθεί οργανισμός σε αυτόν τον λογαριασμό.</p>{/if}
        <h3>Δυνατότητες ρόλου</h3>
        <ul class="admin-permissions" data-testid="admin-permissions">{#each user.permissions as permission}<li>{adminCapabilityName(permission)}</li>{/each}</ul>
      </section>
      {#if modules.length}
        <section class="admin-panel" aria-labelledby="modules-title"><h2 id="modules-title">Ενότητες</h2>
          <ul class="admin-pending-modules">{#each modules as name}<li><span>{name}</span><span class="admin-development">Under Developments</span></li>{/each}</ul>
        </section>
      {/if}
    </main>
  </div>
</div>
