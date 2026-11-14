<script lang="ts">
  import { getContext } from 'svelte';
  import { goto } from '$app/navigation';
  import { browser } from '$app/env';
  import { ADMIN_AUTH_CONTEXT, type AdminAuthController } from '../../../lib/admin/controller.ts';
  import FoundingAdminSetup from '../../../lib/admin/FoundingAdminSetup.svelte';
  import AdminSignIn from '../../../lib/admin/AdminSignIn.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';

  const controller = getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  $effect(() => { if (browser && $controller.status === 'authenticated') void goto('/admin', { replace: true }); });
</script>

<svelte:head><title>Σύνδεση διαχείρισης | Λευτεριά FC</title></svelte:head>
{#if $controller.status === 'anonymous'}
  <AdminSignIn busy={$controller.busy} error={$controller.error} onLogin={controller.login} />
  <FoundingAdminSetup />
{:else}
  <AdminSessionStatus unavailable={$controller.status === 'unavailable'} busy={$controller.busy} error={$controller.error}
    onRetry={() => void controller.bootstrap()} onLogout={() => void controller.logout()} />
{/if}
