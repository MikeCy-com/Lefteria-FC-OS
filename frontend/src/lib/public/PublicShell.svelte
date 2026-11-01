<script lang="ts">
  import type { Snippet } from 'svelte';
  import type { PublicClub, PublicGroup, PublicOrganization, PublicSite } from './types';
  import PublicNavigation from './PublicNavigation.svelte';
  import PublicFooter from './PublicFooter.svelte';
  import './public-views.css';
  import './public.css';
  import './HomepageRefinements.css';
  import './SidelineBanner.css';
  let { site, branding, organization = 'lefteria_fc', navigationGroups = [], children }: { site: PublicSite | null; branding?: PublicClub | null; organization?: PublicOrganization; navigationGroups?: PublicGroup[]; children: Snippet } = $props();
  const club = $derived(branding === undefined ? site?.club ?? null : branding);
</script>
<div class="public-site" lang="el" data-organization={organization} data-testid="public-site">
  <PublicNavigation {club} groups={navigationGroups} />
  <div class="public-content"><main id="public-main" class="public-main" tabindex="-1" data-testid="public-main">{@render children()}</main><PublicFooter {club} /></div>
</div>
