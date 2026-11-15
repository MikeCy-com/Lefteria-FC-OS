<script lang="ts">
  // Temporary, local-only SvelteKit preview. Not a login or authorization fixture.
  import { page } from '$app/state';
  import MobileShell from '../../lib/mobile/components/MobileShell.svelte';
  import MobileRoleHome from '../../lib/mobile/components/MobileRoleHome.svelte';
  import MobileParentHome from '../../lib/mobile/components/MobileParentHome.svelte';
  import { isMobileRole, type MobileRole } from '../../lib/mobile/navigation.ts';
  const selected = $derived(page.url.searchParams.get('role'));
  const role = $derived(isMobileRole(selected) ? selected : 'fan');
  const noop = () => {};
  const noopLoad = () => false;
</script>

<svelte:head>
  <title>Isolated component visual preview · {role}</title>
  <meta name="robots" content="noindex,nofollow" />
</svelte:head>
<MobileShell {role} activeTab="home" availableRoles={[role]} userName="Δοκιμαστικός χρήστης"
  onTabChange={noop} onRoleChange={noop} onNotifications={noop}
  onProfile={noop} onCreatePost={noop}>
  {#if role === 'parent'}
    <MobileParentHome identityId="visual-only" parentName="Δοκιμαστικός γονέας"
      family={null} familyStatus="loading" familyError={null}
      calendar={null} calendarStatus="loading" calendarError={null}
      onLoadFamily={noopLoad} onLoadCalendar={noopLoad} onChildren={noop}
      onCalendar={noop} onEducation={noop} onMessages={noop} onFinancials={noop}/>
  {:else}
    <MobileRoleHome {role} identityId="visual-only" userName="Δοκιμαστικός χρήστης"
      organizationId={role === 'management' ? null : undefined}
      calendar={null} calendarStatus="loading" calendarError={null}
      onLoadCalendar={noopLoad} onNavigate={noop}/>
  {/if}
</MobileShell>
