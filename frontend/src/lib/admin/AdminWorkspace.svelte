<script lang="ts">
  import type { WebAdminSession } from './controller.ts';
  import { ADMIN_ROLE_NAMES, adminCapabilityName, isAdminRole, pendingAdminModules } from './permissions.ts';
  import { canReadClub } from './club-controller.ts';
  import AdminFrame from './AdminFrame.svelte';

  let { session, busy = false, onLogout, onRefresh }: {
    session: WebAdminSession; busy?: boolean; onLogout: () => void; onRefresh: () => void;
  } = $props();
  const user = $derived(session.user);
  const roleName = $derived(isAdminRole(user.role) ? ADMIN_ROLE_NAMES[user.role] : '—');
  const modules = $derived(pendingAdminModules(user.permissions, user.organizations.map(org => org.id)));
  const expiry = $derived(new Intl.DateTimeFormat('el-GR', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'Asia/Nicosia' }).format(new Date(session.expires_at)));
</script>

<div class="admin-workspace" data-testid="admin-workspace">
  <AdminFrame {user} {busy} {onLogout}>
      <div class="admin-workspace-intro"><p class="admin-eyebrow">LEFTERIA FC · ΔΙΑΧΕΙΡΙΣΗ</p><h1>Καλώς ήρθατε,<br />{user.name || user.username}</h1>
      <p class="admin-muted">Ο λογαριασμός, η σύνδεση και τα τρέχοντα δικαιώματά σας.</p></div>
      {#if canReadClub(user)}<section class="admin-panel" aria-labelledby="website-title"><p class="admin-eyebrow">ΙΣΤΟΣΕΛΙΔΑ</p><h2 id="website-title">Προφίλ συλλόγου</h2><p class="admin-muted">Τα δημόσια στοιχεία, η ιστορία, η επικοινωνία και τα κοινωνικά δίκτυα κάθε οργανισμού σας.</p><a class="admin-module-link" href="/admin/club">Άνοιγμα προφίλ συλλόγου <span aria-hidden="true">↗</span></a></section>{/if}
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
  </AdminFrame>
</div>
