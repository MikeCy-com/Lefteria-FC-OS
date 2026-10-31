<script lang="ts">
  import { getContext } from 'svelte';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import { browser } from '$app/env';
  import MobileShell from '../../../lib/mobile/components/MobileShell.svelte';
  import MobileProfile from '../../../lib/mobile/components/MobileProfile.svelte';
  import MobileCalendar from '../../../lib/mobile/components/MobileCalendar.svelte';
  import OrganizationSwitcher from '../../../lib/mobile/components/OrganizationSwitcher.svelte';
  import { MOBILE_AUTH_CONTEXT, type MobileAuthController } from '../../../lib/mobile/controller.ts';
  import { ROLE_NAMES, TAB_NAMES, allowedTabs, isMobileRole, tabFromLocation, type MobileRole, type MobileTab } from '../../../lib/mobile/navigation.ts';

  const controller = getContext<MobileAuthController>(MOBILE_AUTH_CONTEXT);
  let composing = $state(false);
  const user = $derived($controller.user);
  const role = $derived(user && isMobileRole(user.role) ? user.role : null);
  const activeTab = $derived(role ? tabFromLocation(page.url, role) : 'home');
  $effect(() => { if (browser && $controller.status === 'anonymous') void goto('/app/login', { replace: true }); });
  $effect(() => { void role; composing = false; });

  async function openTab(tab: MobileTab) {
    composing = false;
    await goto(`/app?tab=${tab}`, { reset: true });
  }
  async function changeRole(nextRole: MobileRole) {
    if (await controller.switchRole(nextRole)) await openTab('home');
  }
  async function openProfile() {
    if (role === 'fan' || await controller.switchRole('fan')) await openTab('profile');
  }
  function logout() { controller.logout(); }
</script>

<svelte:head><title>{TAB_NAMES[activeTab]} | Λευτεριά FC</title><meta name="robots" content="noindex" /></svelte:head>

{#if $controller.status === 'authenticated' && user && role}
  <MobileShell {role} {activeTab} availableRoles={user.current_role_available ? $controller.availableRoles : $controller.availableRoles.filter((option) => option === 'fan')} userName={user.name}
    avatarUrl={user.avatar_url} busy={$controller.busy}
    onTabChange={openTab} onRoleChange={changeRole}
    onNotifications={async () => { await openTab('notifications'); }} onProfile={openProfile}
    onCreatePost={() => { composing = true; }}>
    {#if $controller.error && $controller.error !== $controller.organizationError && !(activeTab === 'profile' && role === 'fan' && user.current_role_available && !composing)}<p class="error" role="alert">{$controller.error}</p>{/if}
    {#if !user.current_role_available}
      <section class="content-card" aria-labelledby="revoked-title" data-testid="revoked-role">
        <h1 id="revoked-title">Ο ρόλος δεν είναι πλέον διαθέσιμος</h1>
        <p>Η πρόσβαση με αυτόν τον ρόλο έχει αφαιρεθεί.</p>
        {#if $controller.availableRoles.includes('fan')}
          <button class="primary" type="button" disabled={$controller.busy} onclick={() => void changeRole('fan')} data-testid="recover-fan">Συνέχεια ως φίλαθλος</button>
        {/if}
        <button class="secondary" type="button" disabled={$controller.busy} onclick={logout}>Αποσύνδεση</button>
      </section>
    {:else}
      {#if role === 'management'}
        {#if $controller.organization}
          <OrganizationSwitcher organizations={$controller.organization.available_organizations}
            selectedId={$controller.organization.organization_id} requiresSelection={$controller.organization.requires_organization_selection}
            busy={$controller.busy} error={$controller.organizationError}
            onSelect={async (id) => { await controller.selectOrganization(id); }} />
        {:else if $controller.organizationError}
          <section class="content-card"><p class="error" role="alert">{$controller.organizationError}</p>
            <button class="secondary" type="button" disabled={$controller.busy} onclick={() => void controller.refreshOrganization()}>Δοκιμή ξανά</button>
          </section>
        {/if}
      {/if}
      {#if activeTab === 'profile' && role === 'fan' && !composing}
        <MobileProfile {user} busy={$controller.busy} error={$controller.error}
          playerOptions={$controller.playerOptions} playerOptionsStatus={$controller.playerOptionsStatus} playerOptionsError={$controller.playerOptionsError}
          onLoadPlayerOptions={() => controller.loadPlayerOptions()}
          onSaveFavoriteTeam={(value) => controller.updateFavoriteTeam(value)}
          onSaveProfile={(changes) => controller.updateProfile(changes)}
          onUploadAvatar={(file) => controller.uploadAvatar(file)} onLogout={logout} />
      {:else if activeTab === 'calendar' && role !== 'fan' && !composing}
        {#if role === 'management' && (!$controller.organization?.organization_id || $controller.organization.requires_organization_selection)}
          <section class="content-card" data-testid="calendar-organization-required"><h1>Πρόγραμμα</h1><p>Επίλεξε οργανισμό για να δεις το πρόγραμμά του.</p></section>
        {:else}
          {#key `${user.id}:${role}:${role === 'management' ? $controller.organization?.organization_id : ''}`}
            <MobileCalendar {role} calendar={$controller.calendar} status={$controller.calendarStatus} error={$controller.calendarError}
              loadingMore={$controller.calendarLoadingMore} busy={$controller.busy}
              responses={$controller.calendarResponses} responsesKey={$controller.calendarResponsesKey}
              responsesStatus={$controller.calendarResponsesStatus} responsesError={$controller.calendarResponsesError}
              responsesLoadingMore={$controller.calendarResponsesLoadingMore}
              onLoad={(month, playerId) => controller.loadCalendar(month, playerId)}
              onLoadMore={() => controller.loadMoreCalendar()}
              onRespond={(item, status) => controller.submitAvailability(item, status)}
              onLoadResponses={(item, append) => controller.loadCalendarResponses(item, append)}
              onCloseResponses={() => controller.clearCalendarResponses()} />
          {/key}
        {/if}
      {:else if activeTab === 'more' && !composing}
        <section class="content-card" aria-labelledby="more-title"><h1 id="more-title">{TAB_NAMES.more}</h1>
          <div class="more-links">
            {#each allowedTabs(role).filter((tab) => !['home', 'more', 'profile'].includes(tab)) as tab (tab)}
              <button class="secondary" type="button" disabled={$controller.busy} onclick={() => void openTab(tab)}>{TAB_NAMES[tab]}</button>
            {/each}
            <button class="secondary" type="button" disabled={$controller.busy} onclick={() => void openProfile()}>Το προφίλ μου</button>
            <button class="secondary" type="button" disabled={$controller.busy} onclick={logout}>Αποσύνδεση</button>
          </div>
        </section>
      {:else}
        <section class="content-card development" aria-labelledby="development-title" data-testid="under-developments">
          <p class="context">{ROLE_NAMES[role] + ($controller.organization?.organization_name ? ` · ${$controller.organization.organization_name}` : '')}</p>
          <h1 id="development-title">{composing ? 'Δημιουργία ανάρτησης' : TAB_NAMES[activeTab]}</h1>
          <p class="development-label">Under Developments</p>
          {#if composing}<button class="secondary" type="button" onclick={() => { composing = false; }}>Πίσω</button>{/if}
        </section>
      {/if}
    {/if}
  </MobileShell>
{:else}
  <div class="app-status">
    {#if $controller.status === 'unavailable'}
      <p role="alert">{$controller.error}</p>
      <button class="secondary" type="button" disabled={$controller.busy} onclick={() => void controller.bootstrap()}>Δοκιμή ξανά</button>
      <button class="secondary" type="button" disabled={$controller.busy} onclick={logout}>Αποσύνδεση</button>
    {:else}<p role="status">Έλεγχος σύνδεσης…</p>{/if}
  </div>
{/if}

<style>
  .content-card { margin:0 0 16px; padding:20px; border:1px solid #292b2c; border-radius:8px; background:linear-gradient(135deg,#17191a,#101112); }
  .content-card h1 { max-width:none; margin:0 0 14px; font-family:'Fira Sans Extra Condensed',sans-serif; font-size:28px; line-height:1.15; }
  .content-card p { color:#a6aaad; }
  .content-card .context { margin:0 0 8px; color:#ffca46; font-size:12px; }
  .development { margin-top:16px; min-height:180px; }
  .development-label { font-size:16px; }
  .error { margin:0 0 16px; padding:12px; border:1px solid #ef444455; border-radius:6px; background:#ef444411; color:#fca5a5!important; overflow-wrap:anywhere; }
  button { min-height:44px; padding:10px 16px; border:1px solid #383c3e; border-radius:6px; font:inherit; cursor:pointer; }
  .primary { margin:8px 8px 0 0; border-color:#f5a623; background:#f5a623; color:#080808; }
  .secondary { background:#151718; color:#ffca46; }
  button:disabled { opacity:.55; cursor:wait; }
  button:focus-visible { outline:2px solid #ffca46; outline-offset:3px; }
  .more-links { display:grid; gap:9px; }
  .more-links button { text-align:left; }
  .app-status { display:grid; align-content:center; justify-items:center; gap:12px; min-height:100dvh; padding:24px; background:#080808; color:#fff; font-family:'Fira Sans',sans-serif; }
  .app-status p { max-width:32rem; text-align:center; }
</style>
