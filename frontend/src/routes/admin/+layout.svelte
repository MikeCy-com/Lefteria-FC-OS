<script lang="ts">
  import { onMount, setContext, type Snippet } from 'svelte';
  import { browser } from '$app/env';
  import { ADMIN_AUTH_CONTEXT, createAdminAuthController } from '../../lib/admin/controller.ts';
  import '../../lib/admin/admin.css';

  let { children }: { children: Snippet } = $props();
  const controller = createAdminAuthController({ baseUrl: browser ? window.location.origin : '' });
  setContext(ADMIN_AUTH_CONTEXT, controller);
  onMount(() => {
    void controller.bootstrap();
    const refresh = () => { void controller.bootstrap(); };
    const interval = setInterval(() => controller.expireIfNeeded(), 15_000);
    window.addEventListener('focus', refresh);
    return () => { clearInterval(interval); window.removeEventListener('focus', refresh); controller.dispose(); };
  });
</script>

<svelte:head><meta name="robots" content="noindex, nofollow" /></svelte:head>
<div class="admin-app">{@render children()}</div>
