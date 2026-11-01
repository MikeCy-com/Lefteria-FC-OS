<script lang="ts">
  import type { Snippet } from 'svelte';
  import PublicIcon from './PublicIcon.svelte';
  import type { PublicIconName } from './icon-types';
  let { title, icon, testid, children }: { title: string; icon: PublicIconName; testid: string; children: Snippet } = $props();
</script>
<section class="source-legal-section" data-testid={testid}><header><PublicIcon name={icon} size={22} /><h2>{title}</h2></header><div class="source-legal-copy">{@render children()}</div></section>
<style>
  .source-legal-section{margin-bottom:40px}.source-legal-section header{display:flex;align-items:center;gap:12px;margin-bottom:16px;color:#f5a623}.source-legal-section h2{margin:0;color:white;font-size:18px}.source-legal-copy{color:#d4d4d8;font-size:14px;line-height:1.625}.source-legal-copy :global(p){margin:0 0 12px}.source-legal-copy :global(ul){list-style:disc;padding-left:24px;margin:0 0 12px;color:#a1a1aa}.source-legal-copy :global(li){margin-bottom:6px}.source-legal-copy :global(strong){color:#e4e4e7;font-weight:600}.source-legal-copy :global(a){color:#f5a623}.source-legal-copy :global(a:hover){text-decoration:underline}
</style>
