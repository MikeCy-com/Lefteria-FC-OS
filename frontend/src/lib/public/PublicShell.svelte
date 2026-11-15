<script lang="ts">
  import { page } from '$app/state';
  import type { Snippet } from 'svelte';
  import type { PublicClub, PublicGroup, PublicOrganization, PublicSite } from './types';
  import PublicNavigation from './PublicNavigation.svelte';
  import PublicFooter from './PublicFooter.svelte';
  import CookieConsent from './CookieConsent.svelte';
  import { publicCanonicalHref } from './public-urls';
  import { isIndexablePublicOrigin } from './seo';
  import './fonts';
  import './public.css';
  import './club-polish.css';
  let { site, branding, organization = 'lefteria_fc', navigationGroups = [], canonicalPath, children }: { site: PublicSite | null; branding?: PublicClub | null; organization?: PublicOrganization; navigationGroups?: PublicGroup[]; canonicalPath?: string; children: Snippet } = $props();
  const club = $derived(branding === undefined ? site?.club ?? null : branding);
</script>
<svelte:head>
  <link rel="canonical" href={publicCanonicalHref(canonicalPath ?? page.url.pathname)} />
  {#if !isIndexablePublicOrigin(page.url)}<meta name="robots" content="noindex,nofollow" />{/if}
</svelte:head>
<div class="public-site" lang="el" data-organization={organization} data-route={page.route?.id ?? page.url?.pathname} data-testid="public-site">
  <PublicNavigation {club} {organization} groups={navigationGroups} />
  <div class="public-content"><main id="public-main" class="public-main" tabindex="-1" data-testid="public-main">{@render children()}</main><PublicFooter {club} /></div>
</div>
<CookieConsent />
