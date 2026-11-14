<script lang="ts">
  import type { Snippet } from 'svelte';
  import PublicIcon from './PublicIcon.svelte';
  import type { PublicIconName } from './icon-types';
  let { title, icon, testid, children }: { title: string; icon: PublicIconName; testid: string; children: Snippet } = $props();
</script>
<section class="source-legal-section" data-testid={testid}><header><PublicIcon name={icon} size={22} /><h2>{title}</h2></header><div class="source-legal-copy">{@render children()}</div></section>
