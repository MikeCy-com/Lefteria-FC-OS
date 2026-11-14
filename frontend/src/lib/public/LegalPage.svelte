<script lang="ts">
  import type { Snippet } from 'svelte';
  import PublicShell from './PublicShell.svelte';
  import PublicIcon from './PublicIcon.svelte';
  import type { PublicIconName } from './icon-types';
  import type { PublicPageData } from './types';
  let { data, title, description, icon, testid, intro, children }: { data: PublicPageData; title: string; description: string; icon: PublicIconName; testid: string; intro: Snippet; children: Snippet } = $props();
</script>
<svelte:head><title>{title} · Λευτεριά FC</title><meta name="description" content={description} /></svelte:head>
<PublicShell site={data.site} organization={data.organization} navigationGroups={data.navigationGroups} branding={data.branding}><div data-testid={testid}><section class="source-legal-document"><header class="legal-heading"><p>Νομικά</p><h1>{title}</h1><small>Τελευταία ενημέρωση: Φεβρουάριος 2026</small></header><div class="legal-intro"><PublicIcon name={icon} size={18} /><div>{@render intro()}</div></div>{@render children()}</section></div></PublicShell>
