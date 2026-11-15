<script lang="ts">
  import {getContext} from 'svelte';
  import {browser} from '$app/env';
  import {goto} from '$app/navigation';
  import {ADMIN_AUTH_CONTEXT,type AdminAuthController} from '../../../lib/admin/controller.ts';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';
  import {ADMIN_ROLE_NAMES,isAdminRole,adminCapabilityName} from '../../../lib/admin/permissions.ts';

  const auth=getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  $effect(()=>{if(browser&&$auth.status==='anonymous')void goto('/admin/login',{replace:true});});
  const user=$derived($auth.session?.user);
  const roleName=$derived(user&&isAdminRole(user.role)?ADMIN_ROLE_NAMES[user.role]:'—');
  const initials=$derived(user?(user.name||user.username).trim().split(/\s+/u)
    .slice(0,2).map(value=>value.slice(0,1).toLocaleUpperCase('el-GR')).join(''):'FC');
  const expiry=$derived($auth.session?
    new Intl.DateTimeFormat('el-GR',{dateStyle:'medium',timeStyle:'short',timeZone:'Asia/Nicosia'})
      .format(new Date($auth.session.expires_at)):'');
</script>
<svelte:head><title>Το προφίλ μου | Λευτεριά FC OS</title></svelte:head>
{#if $auth.status==='authenticated' && $auth.session && user}
  <AdminFrame {user} active="profile" busy={$auth.busy} onLogout={()=>void auth.logout()}>
    <div class="admin-my-profile" data-testid="admin-my-profile">
      <header class="admin-profile-page-header">
        <div><p class="admin-eyebrow">ΠΡΟΣΩΠΙΚΟΣ ΛΟΓΑΡΙΑΣΜΟΣ</p>
          <h1>Το προφίλ μου</h1>
          <p class="admin-muted">Τα στοιχεία αυτά ανήκουν στον συνδεδεμένο, επαληθευμένο λογαριασμό διαχείρισης.</p>
        </div>
        <button class="admin-secondary" type="button" disabled={$auth.busy}
          onclick={()=>void auth.bootstrap()}>Ανανέωση στοιχείων</button>
      </header>
      <section class="admin-panel admin-profile-identity" aria-label="Προσωπικά στοιχεία">
        <div class="admin-profile-initials" aria-label="Αρχικά προφίλ">{initials}</div>
        <div><h2>{user.name || user.username}</h2>
          <p class="admin-muted">{roleName} · {user.username}</p>
          <p class="admin-profile-unavailable">Εικόνα προφίλ: δεν υπάρχει επαληθευμένο API μεταφόρτωσης για τον διαχειριστή.</p>
        </div>
      </section>
      <div class="admin-profile-columns">
        <section class="admin-panel" aria-labelledby="my-profile-account-heading">
          <h2 id="my-profile-account-heading">Στοιχεία λογαριασμού</h2>
          <dl class="admin-details">
            <div><dt>Όνομα</dt><dd>{user.name || '—'}</dd></div>
            <div><dt>Όνομα χρήστη</dt><dd>{user.username}</dd></div>
            <div><dt>Ρόλος</dt><dd data-testid="admin-role">{roleName}</dd></div>
            <div><dt>Email</dt><dd>Δεν παρέχεται από τον επαληθευμένο λογαριασμό</dd></div>
          </dl>
          <p class="admin-profile-unavailable" role="status">Η επεξεργασία ονόματος, email και φωτογραφίας χρειάζεται ξεχωριστό προστατευμένο API και δεν εκτελείται μέσω αυτής της προβολής.</p>
        </section>
        <section class="admin-panel" id="security" aria-labelledby="my-profile-security-heading">
          <h2 id="my-profile-security-heading">Ασφάλεια & σύνδεση</h2>
          <p>Η τρέχουσα σύνδεση λήγει <time datetime={$auth.session.expires_at}>{expiry}</time> (ώρα Κύπρου).</p>
          <p class="admin-profile-unavailable">Η αλλαγή κωδικού απαιτεί επαλήθευση του τρέχοντος κωδικού και ασφαλή ακύρωση προηγούμενων συνεδριών. Δεν είναι ακόμη διαθέσιμη.</p>
          <button class="admin-secondary" type="button" disabled={$auth.busy}
            onclick={()=>void auth.logout()}>Αποσύνδεση από αυτή τη συσκευή</button>
        </section>
      </div>
      <section class="admin-panel" aria-labelledby="my-profile-access-heading">
        <h2 id="my-profile-access-heading">Οι αναθέσεις μου</h2>
        {#if user.organizations.length}
          <ul class="admin-profile-orgs">
            {#each user.organizations as org (org.id)}
              <li><strong>{org.name}</strong><span>Επαληθευμένη πρόσβαση</span></li>
            {/each}
          </ul>
          <details class="admin-profile-permissions">
            <summary>Προβολή επιμέρους δικαιωμάτων</summary>
            <ul>{#each user.permissions as permission (permission)}
              <li>{adminCapabilityName(permission)}</li>
            {/each}</ul>
          </details>
        {:else}
          <p class="admin-muted">Δεν έχει ανατεθεί οργανισμός.</p>
        {/if}
      </section>
    </div>
  </AdminFrame>
{:else}
  <AdminSessionStatus unavailable={$auth.status==='unavailable'} busy={$auth.busy}
    error={$auth.error} onRetry={()=>void auth.bootstrap()} onLogout={()=>void auth.logout()}/>
{/if}
<style>
  .admin-my-profile{display:grid;gap:16px;max-width:1100px}
  .admin-profile-page-header{display:flex;align-items:flex-end;justify-content:space-between;flex-wrap:wrap;gap:20px}
  .admin-profile-page-header h1{margin:6px 0 10px}
  .admin-profile-identity{display:flex;align-items:center;gap:20px}
  .admin-profile-initials{display:grid;place-items:center;width:96px;height:96px;flex:none;border-radius:50%;
    background:linear-gradient(140deg,#ffce5b,#9e6c13);color:#171109;
    font:900 37px/1 'Fira Sans Extra Condensed',sans-serif}
  .admin-profile-identity h2{margin:0 0 8px}
  .admin-profile-columns{display:grid;grid-template-columns:1fr 1fr;gap:16px}
  .admin-profile-columns .admin-panel{margin:0}
  .admin-profile-unavailable{font-size:13px;line-height:1.55;color:#dbbf8b;margin-top:17px}
  .admin-profile-orgs{display:flex;gap:12px;flex-wrap:wrap;list-style:none;padding:0;margin:15px 0}
  .admin-profile-orgs li{display:grid;gap:4px;background:#171a1b;border:1px solid #5f5130;padding:14px}
  .admin-profile-orgs span{color:#b2b8b9;font-size:12px}
  .admin-profile-permissions{margin-top:12px;color:#ddd}
  .admin-profile-permissions summary{cursor:pointer;color:#ffcb59}
  @media(max-width:750px){.admin-profile-columns{grid-template-columns:1fr}.admin-profile-identity{align-items:flex-start}}
</style>
