<script lang="ts">
  import { getContext, onDestroy } from 'svelte';
  import { browser } from '$app/env';
  import { beforeNavigate, goto } from '$app/navigation';
  import { ADMIN_AUTH_CONTEXT, type AdminAuthController } from '../../../lib/admin/controller.ts';
  import { createClubEditorController } from '../../../lib/admin/club-controller.ts';
  import ClubEditor from '../../../lib/admin/ClubEditor.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';

  const auth = getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  const confirmDiscard = () => window.confirm('Υπάρχουν μη αποθηκευμένες αλλαγές. Θέλετε να τις απορρίψετε;');
  const editor = createClubEditorController({ baseUrl: browser ? window.location.origin : '',
    initialOrganization: browser ? new URLSearchParams(window.location.search).get('organization_id') : null,
    confirmDiscard, onUnauthenticated: auth.invalidateSession, onForbidden: () => void auth.bootstrap() });
  $effect(() => {
    if ($auth.status === 'authenticated' && $auth.session) void editor.setAuthority($auth.session.user);
    else if ($auth.status === 'anonymous' || $auth.status === 'unavailable') void editor.setAuthority(null);
    if (browser && $auth.status === 'anonymous') void goto('/admin/login', { replace: true });
  });
  beforeNavigate(navigation => {
    if (!browser || !editor.getState().dirty) return;
    // Section links stay in this form and preserve the draft.
    if (navigation.to && navigation.from && navigation.to.url.pathname === navigation.from.url.pathname &&
        navigation.to.url.search === navigation.from.url.search && !navigation.willUnload) return;
    if (navigation.willUnload || !confirmDiscard()) navigation.cancel();
  });
  function logout() { if (!editor.getState().dirty || confirmDiscard()) void auth.logout(); }
  onDestroy(editor.dispose);
</script>

<svelte:head><title>Προφίλ συλλόγου | Διαχείριση Λευτεριά FC</title></svelte:head>
{#if $auth.status === 'authenticated' && $auth.session}
  <AdminFrame user={$auth.session.user} active="club" busy={$auth.busy || $editor.saving} onLogout={logout}><ClubEditor controller={editor} /></AdminFrame>
{:else}
  <AdminSessionStatus unavailable={$auth.status === 'unavailable'} busy={$auth.busy} error={$auth.error}
    onRetry={() => void auth.bootstrap()} onLogout={() => void auth.logout()} />
{/if}
