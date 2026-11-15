<script lang="ts">
  import { getContext } from 'svelte';
  import { goto } from '$app/navigation';
  import { browser } from '$app/env';
  import { ADMIN_AUTH_CONTEXT, type AdminAuthController } from '../../lib/admin/controller.ts';
  import AdminWorkspace from '../../lib/admin/AdminWorkspace.svelte';
  import AdminSessionStatus from '../../lib/admin/AdminSessionStatus.svelte';

  const controller = getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  $effect(() => { if (browser && $controller.status === 'anonymous') void goto('/admin/login', { replace: true }); });
</script>

<svelte:head><title>Διαχείριση | Λευτεριά FC</title></svelte:head>
{#if $controller.status === 'authenticated' && $controller.session}
  <AdminWorkspace session={$controller.session} busy={$controller.busy} onLogout={() => void controller.logout()}
    onRefresh={() => void controller.bootstrap()} />
{:else}
  <AdminSessionStatus unavailable={$controller.status === 'unavailable'} busy={$controller.busy} error={$controller.error}
    onRetry={() => void controller.bootstrap()} onLogout={() => void controller.logout()} />
{/if}
