<script lang="ts">
  import type {WebAdminSession} from './controller.ts';
  import {ADMIN_ROLE_NAMES,isAdminRole} from './permissions.ts';
  import {adminRoleActions,adminRoleIntro} from './admin-role-workflows.ts';

  let {user}:{user:WebAdminSession['user']}=$props();
  const role=$derived(isAdminRole(user.role)?ADMIN_ROLE_NAMES[user.role]:'Προσωπικό');
  const actions=$derived(adminRoleActions(user));
  const assigned=$derived(user.organizations.filter(org=>
    org.id==='lefteria_fc'||org.id==='lefteria_fc_academy'));
</script>

<section class="admin-role-workboard" data-testid="admin-role-workboard"
  aria-label="Επιτρεπόμενες εργασίες" data-role={user.role}>
  <header class="admin-workboard-header">
    <div>
      <p class="admin-eyebrow">ΚΕΝΤΡΟ ΕΡΓΑΣΙΩΝ · {role}</p>
      <h2>Τι χρειάζεται να κάνεις</h2>
      <p class="admin-muted">{adminRoleIntro(user.role)}</p>
    </div>
    <a href="/admin/profile#access" data-testid="admin-workboard-access">
      Οι αναθέσεις μου
    </a>
  </header>
  {#if assigned.length>0}
    <p class="admin-workboard-scopes">
      <strong>Ενεργοί οργανισμοί:</strong>
      {assigned.map(org=>org.name).join(' · ')}
    </p>
  {:else}
    <p role="status" class="admin-workboard-notice">
      Δεν έχει αποδοθεί ενεργός οργανισμός. Ζήτησε εξουσιοδότηση από τον Super Admin.
    </p>
  {/if}
  {#if actions.length>0}
    <div class="admin-workboard-actions" data-testid="admin-role-action-list">
      {#each actions as action (action.id)}
        <a class="admin-workboard-action" href={action.href}
          data-testid={'admin-role-action-'+action.id}>
          <span class="admin-workboard-area">
            {action.area==='academy'?'ΑΚΑΔΗΜΙΑ':
             action.area==='finance'?'ΟΙΚΟΝΟΜΙΚΑ':
             action.area==='content'?'ΠΕΡΙΕΧΟΜΕΝΟ':
             action.area==='team'?'ΑΓΩΝΙΣΤΙΚΟ':'ΛΕΙΤΟΥΡΓΙΕΣ'}
          </span>
          <strong>{action.title}</strong>
          <span class="admin-workboard-description">{action.description}</span>
          <span class="admin-workboard-open">Άνοιγμα <span aria-hidden="true">›</span></span>
        </a>
      {/each}
    </div>
    <p class="admin-workboard-footnote">
      Εμφανίζονται μόνο ενεργά εργαλεία σύμφωνα με τον ρόλο και τους οργανισμούς
      της τρέχουσας συνεδρίας. Κάθε ενέργεια επανελέγχεται στον διακομιστή.
      Δεν εμφανίζονται υποθετικά υπόλοιπα, αιτήσεις ή ειδοποιήσεις.
    </p>
  {:else}
    <div class="admin-workboard-notice" role="status" data-testid="admin-role-no-assigned-actions">
      Δεν υπάρχουν ακόμη ενεργές λειτουργίες για αυτόν τον συνδυασμό ρόλου και δικαιωμάτων.
      Έλεγξε τις αναθέσεις ή επικοινώνησε με τον Super Admin.
    </div>
  {/if}
</section>

<style>
  .admin-role-workboard{display:grid;gap:14px;margin:0 0 24px}
  .admin-workboard-header{display:flex;align-items:end;justify-content:space-between;
    flex-wrap:wrap;gap:14px}
  .admin-workboard-header h2{margin:0 0 6px;font-size:clamp(22px,2.6vw,29px)}
  .admin-workboard-header .admin-eyebrow{margin:0 0 6px}
  .admin-workboard-header .admin-muted{margin:0;max-width:68ch;font-size:13px}
  .admin-workboard-header>a{font-size:13px;min-height:42px;display:inline-flex;
    align-items:center;padding:8px 12px;border:1px solid var(--admin-gold,#f6bb38);
    border-radius:8px;text-decoration:none;color:var(--admin-gold,#f6bb38)}
  .admin-workboard-scopes{margin:0;font-size:12px;color:var(--admin-muted,#b4bab3)}
  .admin-workboard-scopes strong{color:inherit;font-weight:750}
  .admin-workboard-actions{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,240px),1fr));gap:12px}
  .admin-workboard-action{display:flex;flex-direction:column;gap:7px;min-height:168px;
    padding:17px;border:1px solid var(--admin-border,#3c3a33);border-radius:12px;
    color:inherit!important;text-decoration:none!important;background:var(--admin-surface,#151515);
    transition:border-color .16s,transform .16s}
  .admin-workboard-action:hover,.admin-workboard-action:focus-visible{border-color:var(--admin-gold,#f6bb38)}
  @media(prefers-reduced-motion:no-preference){
    .admin-workboard-action:hover{transform:translateY(-2px)}
  }
  .admin-workboard-area{font-size:10px;letter-spacing:.14em;color:var(--admin-gold,#f6bb38);
    font-weight:800}
  .admin-workboard-action strong{font-size:17px;line-height:1.3;color:inherit}
  .admin-workboard-description{flex:1;font-size:12px;line-height:1.55;color:var(--admin-muted,#b4bab3)}
  .admin-workboard-open{display:flex;justify-content:space-between;align-items:center;
    gap:8px;font-size:12px;font-weight:700;color:var(--admin-gold,#f6bb38)}
  .admin-workboard-open span{font-size:20px;line-height:1}
  .admin-workboard-footnote{font-size:11px;margin:0;color:var(--admin-muted,#aeb2b3)}
  .admin-workboard-notice{padding:16px;border:1px dashed var(--admin-border,#555);
    color:var(--admin-muted,#b4bab3);border-radius:9px;font-size:13px}
  @media(max-width:640px){.admin-workboard-actions{grid-template-columns:1fr}
    .admin-workboard-action{min-height:146px}}
</style>
