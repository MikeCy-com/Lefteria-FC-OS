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
<style>
  .source-legal-document{max-width:896px;margin:auto;padding:56px 24px 64px}.legal-heading{margin-bottom:40px}.legal-heading>p{color:#f5a623;font-size:12px;text-transform:uppercase;margin:0 0 8px}.legal-heading h1{margin:0 0 12px}.legal-heading small{font-size:12px;color:#71717a}.legal-intro{display:flex;gap:12px;align-items:flex-start;padding:16px;border:1px solid #f5a6234d;background:#f5a6230d;margin-bottom:32px;color:#f5a623}.legal-intro>div{min-width:0;color:#d4d4d8;font-size:14px;line-height:1.625}.legal-intro :global(p){margin:0}.legal-intro :global(strong){color:white}.source-legal-document :global(.legal-other){margin-top:48px;padding-top:24px;border-top:1px solid #262626;text-align:center;font-size:14px;color:#f5a623}.source-legal-document :global(.legal-other a:hover){text-decoration:underline}@media(max-width:767px){.source-legal-document{padding:35px 16px 64px}}
</style>
