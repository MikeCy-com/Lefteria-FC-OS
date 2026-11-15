<script lang="ts">
  import type {WebAdminSession} from './controller.ts';
  import {adminSectionsFor} from './module-catalog.ts';
  import {hasAdminPermission} from './permissions.ts';
  import AdminFrame from './AdminFrame.svelte';
  import AdminClubMetrics from './AdminClubMetrics.svelte';
  import {adminOrganizationsFor} from './admin-organization.ts';

  let {session,busy=false,onLogout,onRefresh}:{
    session:WebAdminSession;busy?:boolean;onLogout:()=>void;onRefresh:()=>void;
  }=$props();
  const user=$derived(session.user);
  const sections=$derived(adminSectionsFor(user));
  // Dashboard only surfaces actions that work. Roadmap counts, placeholder
  // modules and the signed-in profile belong outside the working area.
  const modules=$derived(sections.flatMap(section=>section.modules)
    .filter(item=>item.status==='live'&&item.href));
  const has=(id:string)=>modules.some(module=>module.id===id);
  const actions=$derived([
    {id:'groups',label:'Ομάδες & τμήματα',description:'Δημιουργία, ρόστερ, προπονήσεις, αγώνες και ενέργειες αθλητών',href:'/admin/groups'},
    {id:'territories',label:'Πόλεις & τοποθεσίες',description:'Οργάνωση Ακαδημίας, βάσεις, αγωνιστικοί κατάλογοι ανά πόλη',href:'/admin/territories'},
    {id:'communications',label:'Επικοινωνία εφαρμογής',description:'Ανακοινώσεις, συνομιλίες, ειδοποιήσεις και προστατευμένα αρχεία',href:'/admin/communications'},
    {id:'registrations',label:'Εγγραφές Ακαδημίας',description:'Ενιαία λίστα ηλεκτρονικών και χειροκίνητων αιτήσεων',href:'/admin/academy-office'},
    {id:'news',label:'Άρθρα & νέα',description:'Περιεχόμενο και δημοσιευμένα νέα ανά οργανισμό',href:'/admin/news'},
    {id:'hub_editorial',label:'HUB · Εγκρίσεις περιεχομένου',description:'Ελεγχόμενα πρόχειρα και ανθρώπινη έγκριση',href:'/admin/hub'},
    {id:'hub_subscriptions',label:'HUB · Συνδρομές φιλάθλων',description:'Επαληθευμένα συγκεντρωτικά στοιχεία, διαθέσιμα μόνο σε Super Admin',href:'/admin/hub/subscriptions'},
    {id:'payabl',label:'Οικονομικά Ακαδημίας',description:'Τιμολόγια, εισπράξεις και δοκιμαστικό payabl.',href:'/admin/finance'},
    {id:'staff',label:'Προσωπικό & προπονητές',description:'Αναθέσεις σε πραγματικές ομάδες',href:'/admin/staff'},
    {id:'users',label:'Χρήστες & δικαιώματα',description:'Προβολή επαληθευμένων λογαριασμών',href:'/admin/users'},
    {id:'products',label:'POS & κατάστημα',description:'Κατάλογος και σημεία πώλησης',href:'/admin/pos'},
    {id:'leads',label:'CRM · Ενδιαφερόμενοι',description:'Επαληθευμένες εγγραφές ενδιαφέροντος',href:'/admin/leads'},
    {id:'club',label:'Ρυθμίσεις ιστοσελίδας',description:'Ταυτότητα και δημόσια στοιχεία του οργανισμού',href:'/admin/settings'}
  ].filter(item=>has(item.id)));
  const hasOrganization=(id:string)=>user.organizations.some(org=>org.id===id);
  // These are real, authorized entry points. The forms and the backend enforce
  // permissions again; displaying a card never grants a write capability.
  const creationActions=$derived([
    ...(has('groups')&&hasOrganization('lefteria_fc')&&
      hasAdminPermission(user.permissions,'teams:write')&&
      ['manager','super_admin'].includes(user.role)
      ? [{id:'first-team',label:'Νέα ομάδα πρώτης ομάδας',
          href:'/admin/groups?organization_id=lefteria_fc&new=1'}] : []),
    ...(has('groups')&&hasOrganization('lefteria_fc_academy')&&
      hasAdminPermission(user.permissions,'academy:write')&&
      ['manager','super_admin'].includes(user.role)
      ? [{id:'academy-group',label:'Νέο τμήμα Ακαδημίας',
          href:'/admin/groups?organization_id=lefteria_fc_academy&new=1'}] : []),
    ...(has('territories')&&hasOrganization('lefteria_fc_academy')&&
      hasAdminPermission(user.permissions,'academy:write')&&
      user.role==='super_admin'
      ? [{id:'academy-city',label:'Νέα πόλη Ακαδημίας',
          href:'/admin/territories?organization_id=lefteria_fc_academy#create-city'}] : []),
    ...(has('territories')&&hasOrganization('lefteria_fc_academy')&&
      hasAdminPermission(user.permissions,'academy:write')&&
      ['manager','super_admin'].includes(user.role)
      ? [{id:'academy-location',label:'Νέα τοποθεσία',
          href:'/admin/territories?organization_id=lefteria_fc_academy#create-location'}] : []),
    ...(has('users')&&user.role==='super_admin'
      ? [{id:'admin-account',label:'Νέος χρήστης & ρόλος',
          href:'/admin/users?new=1'}] : [])
  ]);

  const canSeeOverviewMetrics=$derived(
    ['teams:read','academy:read','news:read'].some(permission=>
      hasAdminPermission(user.permissions,permission))||
    ['manager','super_admin'].includes(user.role)&&
      hasAdminPermission(user.permissions,'marketing:read')&&
      user.organizations.some(org=>org.id==='lefteria_fc_academy')
  );
</script>

<AdminFrame {user} {busy} {onLogout}>
  <div class="admin-workspace-intro" data-testid="admin-dashboard-heading">
    <p class="admin-eyebrow">ΚΕΝΤΡΟ ΔΙΑΧΕΙΡΙΣΗΣ · ΛΕΥΤΕΡΙΑ FC</p>
    <h1>Πίνακας εργασιών</h1>
    <p class="admin-muted">Επιλέξτε πού θέλετε να εργαστείτε. Οι ομάδες συγκεντρώνουν
      το πρόγραμμα, τους αγώνες, τους αθλητές και τις καταχωρίσεις τους.</p>
  </div>

  {#if creationActions.length}
    <nav class="admin-creation-shortcuts" aria-label="Γρήγορη δημιουργία" data-testid="admin-creation-actions">
      <h2>Γρήγορη δημιουργία</h2>
      <div class="admin-creation-shortcut-grid">
        {#each creationActions as item (item.id)}
          <a href={item.href} data-testid={'admin-new-'+item.id}>
            <span aria-hidden="true">＋</span> {item.label}
          </a>
        {/each}
      </div>
    </nav>
  {/if}

  {#if actions.length}
    <nav class="admin-panel admin-action-dashboard" aria-label="Εργασίες διαχείρισης" data-testid="admin-quick-navigation">
      <div class="admin-action-heading">
        <h2>Οι εργασίες μου</h2>
        <button type="button" class="admin-secondary" disabled={busy} onclick={onRefresh}>Ανανέωση</button>
      </div>
      <div class="admin-action-grid">
        {#each actions as item (item.id)}
          <a href={item.href} class="admin-action-card" data-testid={'admin-dashboard-'+item.id}>
            <strong>{item.label}</strong><small>{item.description}</small>
            <span aria-hidden="true">↗</span>
          </a>
        {/each}
      </div>
    </nav>
  {:else}
    <section class="admin-panel" data-testid="admin-no-modules">
      <h2>Δεν υπάρχουν διαθέσιμες εργασίες</h2>
      <p class="admin-muted">Δεν έχει επαληθευτεί ενεργή ανάθεση για αυτή τη σύνδεση.</p>
    </section>
  {/if}

  {#if canSeeOverviewMetrics}
    <section class="admin-dashboard-metrics" aria-label="Επιβεβαιωμένα στοιχεία οργανισμών">
      <div class="admin-action-heading"><h2>Η εικόνα του συλλόγου</h2></div>
      {#each adminOrganizationsFor(user) as organization (organization)}
        <AdminClubMetrics {user} {organization}/>
      {/each}
    </section>
  {/if}
</AdminFrame>
<style>

  .admin-creation-shortcuts{margin:14px 0 20px}
  .admin-creation-shortcuts h2{font:italic 900 24px/1 'Fira Sans Extra Condensed',sans-serif;margin:0 0 12px}
  .admin-creation-shortcut-grid{display:flex;flex-wrap:wrap;gap:9px}
  .admin-creation-shortcut-grid a{
    display:inline-flex;align-items:center;gap:8px;min-height:44px;
    padding:10px 14px;background:#f6bb38;color:#111!important;
    border:1px solid #ffcc69;border-radius:5px;
    text-decoration:none;font:800 15px 'Fira Sans Extra Condensed',sans-serif;
  }
  .admin-creation-shortcut-grid a:hover{background:#ffcf6a}
  .admin-creation-shortcut-grid a:focus-visible{outline:3px solid #ffd778;outline-offset:3px}
  .admin-action-dashboard{margin:16px 0 24px}
  .admin-action-heading{display:flex;align-items:center;justify-content:space-between;gap:14px;flex-wrap:wrap;margin:0 0 18px}
  .admin-action-heading h2{margin:0;font-style:italic;text-transform:uppercase}
  .admin-action-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px}
  .admin-action-card{position:relative;display:flex;flex-direction:column;gap:9px;min-height:118px;
    padding:17px 34px 17px 18px;border:1px solid #5b503a;text-decoration:none;color:#f0f0ef;
    background:linear-gradient(140deg,#202018,#111415)}
  .admin-action-card strong{font:800 clamp(19px,1.6vw,27px)/1.1 'Fira Sans Extra Condensed',sans-serif}
  .admin-action-card small{color:#c5c8c9;font-size:12px;line-height:1.4}
  .admin-action-card>span{position:absolute;top:12px;right:14px;color:#ffd05d;font-size:23px}
  .admin-action-card:hover{border-color:#ffcf61;background:linear-gradient(140deg,#362817,#111415)}
  .admin-action-card:focus-visible{outline:3px solid #ffca60;outline-offset:3px}
  .admin-dashboard-metrics{margin-top:23px}
  @media(max-width:1170px){.admin-action-grid{grid-template-columns:repeat(2,minmax(0,1fr))}}
  @media(max-width:700px){.admin-action-grid{grid-template-columns:1fr}}
</style>
