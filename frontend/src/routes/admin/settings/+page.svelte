<script lang="ts">
  import {getContext} from 'svelte';
  import {browser} from '$app/env';
  import {goto} from '$app/navigation';
  import {ADMIN_AUTH_CONTEXT,type AdminAuthController} from '../../../lib/admin/controller.ts';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';
  import {hasAdminPermission} from '../../../lib/admin/permissions.ts';
  const auth=getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  $effect(()=>{if(browser&&$auth.status==='anonymous')void goto('/admin/login',{replace:true});});
  const granted=$derived($auth.session?.user.organizations ?? []);
  const canClub=$derived(Boolean($auth.session && hasAdminPermission($auth.session.user.permissions,'settings:read') && granted.length));
  const canTerritories=$derived(Boolean($auth.session && ['manager','super_admin'].includes($auth.session.user.role) &&
    (hasAdminPermission($auth.session.user.permissions,'academy:read') && granted.some(x=>x.id==='lefteria_fc_academy') ||
     hasAdminPermission($auth.session.user.permissions,'teams:read') && granted.some(x=>x.id==='lefteria_fc'))));
  const canStaff=$derived(Boolean($auth.session && ['manager','super_admin'].includes($auth.session.user.role) &&
    (hasAdminPermission($auth.session.user.permissions,'academy:write') && granted.some(x=>x.id==='lefteria_fc_academy') ||
     hasAdminPermission($auth.session.user.permissions,'teams:write') && granted.some(x=>x.id==='lefteria_fc'))));
  const canUsers=$derived($auth.session?.user.role==='super_admin' &&
    hasAdminPermission($auth.session.user.permissions,'users:read'));
</script>
<svelte:head><title>Ρυθμίσεις | Λευτεριά FC OS</title></svelte:head>
{#if $auth.status==='authenticated' && $auth.session}
  <AdminFrame user={$auth.session.user} active="settings" busy={$auth.busy}
    onLogout={()=>void auth.logout()}>
    <div class="admin-settings-home" data-testid="admin-settings-home">
      <header><p class="admin-eyebrow">ΡΥΘΜΙΣΕΙΣ · ΛΕΥΤΕΡΙΑ FC</p>
        <h1>Ρυθμίσεις συλλόγου</h1>
        <p class="admin-muted">Οι ρυθμίσεις χωρίζονται ανά λειτουργία και οργανισμό.
          Οι σύνδεσμοι ανοίγουν τα πραγματικά, προστατευμένα εργαλεία.</p>
      </header>
      <div class="admin-settings-grid">
        {#if canClub}
          <section class="admin-panel" id="website-settings">
            <p class="admin-eyebrow">ΙΣΤΟΣΕΛΙΔΑ</p>
            <h2>Ρυθμίσεις ιστοσελίδας</h2>
            <p>Ταυτότητα, περιγραφή, στοιχεία επικοινωνίας και δημόσιες σελίδες κάθε οργανισμού.</p>
            <a class="admin-settings-link" href="/admin/club" data-testid="admin-settings-club">Προφίλ & δημόσια στοιχεία <span aria-hidden="true">↗</span></a>
            <p class="admin-settings-note">Οι αλλαγές εφαρμόζονται στον επιλεγμένο οργανισμό, όχι αυτόματα και στους δύο.</p>
          </section>
        {/if}
        {#if canTerritories}
          <section class="admin-panel" id="football-settings">
            <p class="admin-eyebrow">ΟΡΓΑΝΩΣΗ ΑΚΑΔΗΜΙΑΣ</p>
            <h2>Πόλεις & τοποθεσίες</h2>
            <p>Πόλεις, τοποθεσίες, βάσεις προπονήσεων, αντίπαλοι και γήπεδα.
              Οι αντίπαλοι και τα γήπεδα αγώνων της Ακαδημίας είναι κοινά <strong>ανά πόλη</strong>, όχι ανά τοποθεσία.</p>
            <a class="admin-settings-link" href="/admin/territories" data-testid="admin-settings-territories">Διαχείριση πόλεων & υποδομών <span aria-hidden="true">↗</span></a>
            <a class="admin-settings-link" href="/admin/groups">Ομάδες & τμήματα <span aria-hidden="true">↗</span></a>
          </section>
        {/if}
        {#if canStaff || canUsers}
          <section class="admin-panel" id="access-settings">
            <p class="admin-eyebrow">ΡΟΛΟΙ & ΠΡΟΣΒΑΣΗ</p><h2>Χρήστες & αναθέσεις</h2>
            <p>Πρόσβαση σε οργανισμούς, πόλεις, τοποθεσίες και ομάδες με επαληθευμένο δικαίωμα.</p>
            {#if canStaff}<a class="admin-settings-link" href="/admin/staff">Προσωπικό & προπονητές <span aria-hidden="true">↗</span></a>{/if}
            {#if canUsers}<a class="admin-settings-link" href="/admin/users">Λογαριασμοί & δικαιώματα <span aria-hidden="true">↗</span></a>{/if}
          </section>
        {/if}
        <section class="admin-panel" id="personal-settings">
          <p class="admin-eyebrow">ΠΡΟΣΩΠΙΚΑ</p><h2>Προφίλ & ασφάλεια</h2>
          <p>Τα στοιχεία του δικού σας λογαριασμού δεν εμφανίζονται στον πίνακα εργασιών.</p>
          <a class="admin-settings-link" href="/admin/profile">Άνοιγμα προσωπικού προφίλ <span aria-hidden="true">↗</span></a>
        </section>
      </div>
    </div>
  </AdminFrame>
{:else}
  <AdminSessionStatus unavailable={$auth.status==='unavailable'} busy={$auth.busy}
    error={$auth.error} onRetry={()=>void auth.bootstrap()} onLogout={()=>void auth.logout()}/>
{/if}
<style>
  .admin-settings-home{display:grid;gap:24px;max-width:1300px}
  .admin-settings-home header{display:grid;gap:6px}
  .admin-settings-home header h1{margin:0}
  .admin-settings-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px;align-items:start}
  .admin-settings-grid .admin-panel{margin:0;display:grid;gap:12px}
  .admin-settings-grid h2{margin:0}
  .admin-settings-grid p{margin:0;line-height:1.55;color:#d0d4d2}
  .admin-settings-link{display:flex;align-items:center;justify-content:space-between;gap:12px;
    min-height:48px;padding:12px 16px;border:1px solid #a47b30;
    color:#ffcb5b;text-decoration:none;font-weight:750;background:#1b1912}
  .admin-settings-link:focus-visible{outline:3px solid #ffcf60;outline-offset:2px}
  .admin-settings-link:hover{border-color:#ffcf60;background:#2b2315}
  .admin-settings-note{font-size:12px!important;color:#b7b8b6!important}
  @media(max-width:800px){.admin-settings-grid{grid-template-columns:1fr}}
</style>
