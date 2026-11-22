<script lang="ts">
  import type {WebAdminSession} from './controller.ts';
  import {hasAdminPermission} from './permissions.ts';
  import AdminFrame from './AdminFrame.svelte';
  import AdminClubMetrics from './AdminClubMetrics.svelte';
  import {adminOrganizationsFor} from './admin-organization.ts';

  let {session,busy=false,onLogout,onRefresh}:{
    session:WebAdminSession;busy?:boolean;onLogout:()=>void;onRefresh:()=>void;
  }=$props();
  const user=$derived(session.user);
  const organizations=$derived(adminOrganizationsFor(user));
  const canSeeOverviewMetrics=$derived(
    ['teams:read','academy:read','news:read'].some(permission=>
      hasAdminPermission(user.permissions,permission))||
    ['manager','super_admin'].includes(user.role)&&
      hasAdminPermission(user.permissions,'marketing:read')&&
      user.organizations.some(org=>org.id==='lefteria_fc_academy')
  );
</script>

<AdminFrame {user} {busy} {onLogout}>
  <div class="admin-dashboard-header" data-testid="admin-dashboard-heading">
    <h1>Πίνακας εργασιών</h1>
  </div>

  {#if canSeeOverviewMetrics && organizations.length}
    <section class="admin-dashboard-metrics" aria-label="Επιβεβαιωμένα στοιχεία οργανισμών"
      data-testid="admin-dashboard-metrics">
      <h2>Η εικόνα του συλλόγου</h2>
      {#each organizations as organization (organization)}
        <AdminClubMetrics {user} {organization}/>
      {/each}
    </section>
  {:else}
    <section class="admin-panel" data-testid="admin-no-modules">
      <h2>Επισκόπηση</h2>
      <p class="admin-muted">Δεν υπάρχουν διαθέσιμα συγκεντρωτικά στοιχεία για τα τρέχοντα δικαιώματα.</p>
    </section>
  {/if}
</AdminFrame>

<style>
  .admin-dashboard-header{margin:0 0 18px}
  .admin-dashboard-header h1{margin:0;font-size:clamp(21px,2vw,28px)}
  .admin-dashboard-metrics{display:grid;gap:14px;min-width:0}
  .admin-dashboard-metrics h2{margin:0 0 2px;font-size:clamp(16px,1.5vw,21px)}
</style>
