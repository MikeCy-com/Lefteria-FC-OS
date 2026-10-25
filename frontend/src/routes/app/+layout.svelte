<script lang="ts">
  import { onMount, setContext } from 'svelte';
  import { browser } from '$app/env';
  import { createMobileAuthController, MOBILE_AUTH_CONTEXT } from '../../lib/mobile/controller.ts';
  import { browserMobileSessionStorage } from '../../lib/mobile/session.ts';
  import type { Snippet } from 'svelte';

  let { children }: { children: Snippet } = $props();
  const controller = createMobileAuthController({
    baseUrl: browser ? window.location.origin : '',
    storage: browserMobileSessionStorage()
  });
  setContext(MOBILE_AUTH_CONTEXT, controller);
  onMount(() => {
    void controller.bootstrap();
    return () => controller.dispose();
  });
</script>

{@render children()}
