<script lang="ts">
  import type {WebAdminSession} from './controller.ts';
  import {hasAdminPermission} from './permissions.ts';
  import AdminFrame from './AdminFrame.svelte';
  import AdminClubMetrics from './AdminClubMetrics.svelte';
  import AdminRoleWorkboard from './AdminRoleWorkboard.svelte';
  import {adminRoleActions} from './admin-role-workflows.ts';
  import {adminOrganizationsFor} from './admin-organization.ts';

  let {session,busy=false,onLogout,onRefresh}:{
    session:WebAdminSession;busy?:boolean;onLogout:()=>void;onRefresh:()=>void;
  }=$props();
  const user=$derived(session.user);
  const availableTasks=$derived(adminRoleActions(user));
  const canSeeOverviewMetrics=$derived(
    ['teams:read','academy:read','news:read'].some(permission=>
      hasAdminPermission(user.permissions,permission)) ||
    (['manager','super_admin'].includes(user.role) &&
      hasAdminPermission(user.permissions,'marketing:read') &&
      user.organizations.some(org=>org.id==='lefteria_fc_academy'))
  );
</script>

<AdminFrame {user} {busy} {onLogout}>
  <div class="admin-dashboard-top" data-testid="admin-dashboard-heading">
    <h1>Επισκόπηση</h1>
    <button type="button" class="admin-secondary" disabled={busy}
      onclick={onRefresh} data-testid="admin-dashboard-refresh">Ανανέωση</button>
  </div>
  <AdminRoleWorkboard {user}/>
  {#if canSeeOverviewMetrics}
    <section class="admin-dashboard-metrics" aria-label="Επιβεβαιωμένα στοιχεία οργανισμών">
      {#each adminOrganizationsFor(user) as organization (organization)}
        <AdminClubMetrics {user} {organization}/>
      {/each}
    </section>
  {:else if availableTasks.length===0}
    <section class="admin-panel" data-testid="admin-no-modules">
      <p class="admin-muted">Δεν υπάρχουν διαθέσιμα στοιχεία επισκόπησης για τον ρόλο σας.</p>
    </section>
  {/if}
</AdminFrame>

<style>
  .admin-dashboard-top{display:flex;align-items:center;justify-content:space-between;
    gap:12px;flex-wrap:wrap;margin:0 0 18px}
  .admin-dashboard-top h1{margin:0}
  .admin-dashboard-top button{min-height:40px}
  .admin-dashboard-metrics{display:grid;gap:16px}
</style>
