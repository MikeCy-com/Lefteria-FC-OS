<script lang="ts">
  import {getContext} from 'svelte';
  import {browser} from '$app/env';
  import {goto} from '$app/navigation';
  import {ADMIN_AUTH_CONTEXT,type AdminAuthController} from '../../../lib/admin/controller.ts';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';
  import {hasAdminPermission} from '../../../lib/admin/permissions.ts';
  import {adminSectionsFor} from '../../../lib/admin/module-catalog.ts';
  const auth=getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  $effect(()=>{if(browser&&$auth.status==='anonymous')void goto('/admin/login',{replace:true});});
  const granted=$derived($auth.session?.user.organizations ?? []);
  const organizations=$derived(granted.filter(o=>o.id==='lefteria_fc'||o.id==='lefteria_fc_academy'));
  const canClub=$derived(Boolean($auth.session && hasAdminPermission($auth.session.user.permissions,'settings:read') && granted.length));
  const canTerritories=$derived(Boolean($auth.session && ['manager','super_admin'].includes($auth.session.user.role) &&
    (hasAdminPermission($auth.session.user.permissions,'academy:read') && granted.some(x=>x.id==='lefteria_fc_academy') ||
     hasAdminPermission($auth.session.user.permissions,'teams:read') && granted.some(x=>x.id==='lefteria_fc'))));
  const canStaff=$derived(Boolean($auth.session && ['manager','super_admin'].includes($auth.session.user.role) &&
    (hasAdminPermission($auth.session.user.permissions,'academy:write') && granted.some(x=>x.id==='lefteria_fc_academy') ||
     hasAdminPermission($auth.session.user.permissions,'teams:write') && granted.some(x=>x.id==='lefteria_fc'))));
  const canUsers=$derived($auth.session?.user.role==='super_admin' &&
    hasAdminPermission($auth.session.user.permissions,'users:read'));
  const canOnboard=$derived(Boolean($auth.session && adminSectionsFor($auth.session.user)
    .some(section=>section.modules.some(module=>module.id==='onboarding'&&module.status==='live'))));
</script>
<svelte:head><title>Ρυθμίσεις | Λευτεριά FC OS</title></svelte:head>
{#if $auth.status==='authenticated' && $auth.session}
  <AdminFrame user={$auth.session.user} active="settings" busy={$auth.busy}
    onLogout={()=>void auth.logout()}>
    <div class="admin-settings-home" data-testid="admin-settings-home">
      <header>
        <h1>Ρυθμίσεις συλλόγου</h1>
        <p class="admin-muted">Ενιαίο κέντρο διαχείρισης ταυτότητας, επικοινωνίας, υποδομών, προσβάσεων και διασυνδέσεων.
          Επιλέξτε την αντίστοιχη ενότητα για τις πραγματικές, προστατευμένες ρυθμίσεις.</p>
      </header>
      <div class="admin-settings-grid">
        {#if organizations.length}
          <section class="admin-panel" id="organization-settings" data-testid="admin-organization-settings">
            <p class="admin-eyebrow">ΟΡΓΑΝΙΣΜΟΙ · ΠΡΟΣΒΑΣΗ</p>
            <h2>Οι οργανισμοί μου</h2>
            <p>Οι παρακάτω οργανισμοί προέρχονται από την ενεργή, επαληθευμένη συνεδρία. Δεν είναι επιλογές για αλλαγή πρόσβασης.</p>
            <ul class="admin-organization-list" aria-label="Οργανισμοί με εξουσιοδοτημένη πρόσβαση">
              {#each organizations as org (org.id)}
                <li><strong>{org.name}</strong><small>{org.id==='lefteria_fc'?'Πρώτη ομάδα':'Ακαδημία'}</small></li>
              {/each}
            </ul>
            <p class="admin-settings-note">Εσωτερικές παράμετροι ανά οργανισμό (πολιτικές, κύκλοι, πρότυπα) δεν υποστηρίζουν ακόμη αποθήκευση. Δεν επηρεάζονται οι υφιστάμενες ρυθμίσεις.</p>
          </section>
        {/if}
        {#if canClub}
          <section class="admin-panel" id="website-settings">
            <p class="admin-eyebrow">ΙΣΤΟΣΕΛΙΔΑ</p>
            <h2>Ταυτότητα & στοιχεία οργανισμών</h2>
            <p>Λογότυπα, ονόματα, τηλέφωνα, email, διευθύνσεις, πόλεις και κοινωνικά δίκτυα
              της Πρώτης Ομάδας και της Ακαδημίας. Κάθε οργανισμός διατηρεί ξεχωριστό λογαριασμό και στοιχεία.</p>
            <a class="admin-settings-link" href="/admin/club" data-testid="admin-settings-club">
              Επεξεργασία λογότυπου & επαφών <span aria-hidden="true">↗</span></a>
            <a class="admin-settings-link" href="/admin/website-pages">
              Δημόσιες σελίδες & παρουσίαση <span aria-hidden="true">↗</span></a>
            <p class="admin-settings-note">Οι αλλαγές καταχωρίζονται ανά εξουσιοδοτημένο οργανισμό και χρησιμοποιούνται από τις δημόσιες προβολές. Δεν αντιγράφονται από την Πρώτη Ομάδα στην Ακαδημία.</p>
          </section>
        {/if}
        {#if canTerritories}
          <section class="admin-panel" id="football-settings">
            <p class="admin-eyebrow">ΟΡΓΑΝΩΣΗ · ΥΠΟΔΟΜΕΣ</p>
            <h2>Πόλεις, τοποθεσίες & εγκαταστάσεις</h2>
            <p>Πραγματικές βάσεις προπόνησης, γήπεδα αγώνων και αντίπαλοι στον επιλεγμένο οργανισμό.
              Στην Ακαδημία αντίπαλοι και γήπεδα είναι κοινά <strong>ανά πόλη</strong>,
              ενώ οι βάσεις προπόνησης αφορούν συγκεκριμένη τοποθεσία. Η πρώτη ομάδα διαθέτει ανεξάρτητους καταλόγους.</p>
            <a class="admin-settings-link" href="/admin/territories" data-testid="admin-settings-territories">Άνοιγμα διαχείρισης υποδομών <span aria-hidden="true">↗</span></a>
            <a class="admin-settings-link" href="/admin/groups">Ομάδες & τμήματα <span aria-hidden="true">↗</span></a>
          </section>
        {/if}
        {#if canStaff || canUsers}
          <section class="admin-panel" id="access-settings">
            <p class="admin-eyebrow">ΡΟΛΟΙ & ΠΡΟΣΒΑΣΗ</p><h2>Χρήστες & αναθέσεις</h2>
            <p>Πρόσβαση σε οργανισμούς, πόλεις, τοποθεσίες και ομάδες με επαληθευμένο δικαίωμα.</p>
            {#if canStaff}<a class="admin-settings-link" href="/admin/staff">Προσωπικό & προπονητές <span aria-hidden="true">↗</span></a>{/if}
            {#if canUsers}
              <a class="admin-settings-link" href="/admin/users">Χρήστες & δικαιώματα <span aria-hidden="true">↗</span></a>
              <a class="admin-settings-link" href="/admin/users?new=1"
                data-testid="admin-settings-new-user">Νέος χρήστης & ρόλος <span aria-hidden="true">＋</span></a>
            {/if}
            <p class="admin-settings-note">Ο Super Admin μπορεί να αλλάζει ρόλο ή κατάσταση λογαριασμού από τους Χρήστες &amp; δικαιώματα. Πριν από αλλαγή ρόλου πρέπει να αφαιρεθούν οι υφιστάμενες αναθέσεις πόλης, τοποθεσίας, ομάδας και προσωπικού, καθώς και οι ενεργές βάρδιες POS. Η αλλαγή ανακαλεί τις ενεργές συνεδρίες.</p>
          </section>
        {/if}
        {#if canOnboard}
          <section class="admin-panel" id="help-settings" data-testid="admin-settings-onboarding">
            <p class="admin-eyebrow">ΠΡΩΤΗ ΧΡΗΣΗ · ΛΕΙΤΟΥΡΓΙΕΣ</p>
            <h2>Οδηγός διαχειριστή</h2>
            <p>Οδηγίες ανά ρόλο για ομάδες, αθλητές, αναθέσεις, οικονομικά και την εφαρμογή.
              Οι ενέργειες παραμένουν περιορισμένες στα επαληθευμένα δικαιώματά σας.</p>
            <a class="admin-settings-link" href="/admin/onboarding" data-testid="admin-settings-guide">Οδηγός πρώτης χρήσης <span aria-hidden="true">↗</span></a>
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
  .admin-settings-home{display:grid;gap:20px;width:100%;min-width:0}
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
  .admin-organization-list{list-style:none;padding:0;margin:0;display:grid;gap:8px}
  .admin-organization-list li{display:flex;justify-content:space-between;gap:12px;flex-wrap:wrap;
    padding:12px;border:1px solid #4d452f;background:#161816}
  .admin-organization-list strong{font-weight:750}
  .admin-organization-list small{color:#eac168;font-size:12px}
  @media(max-width:800px){.admin-settings-grid{grid-template-columns:1fr}}
</style>
