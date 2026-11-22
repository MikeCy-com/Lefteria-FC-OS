<script lang="ts">
  import { page } from '$app/state';
  import type { Snippet } from 'svelte';
  import type { PublicClub, PublicGroup, PublicOrganization, PublicSite } from './types';
  import PublicNavigation from './PublicNavigation.svelte';
  import PublicFooter from './PublicFooter.svelte';
  import CookieConsent from './CookieConsent.svelte';
  import { publicCanonicalHref } from './public-urls';
  import { isIndexablePublicOrigin } from './seo';
  import { publishedGlobalDesign, globalDesignCssVars } from './global-design';
  import { embeddedWebsiteSections, embeddedWebsiteSectionId } from './website-pages';
  import WebsitePageBlocks from './WebsitePageBlocks.svelte';
  import './fonts';
  import './public.css';
  import './club-polish.css';
  import './global-design.css';
  import './ui-refresh.css';
  let { site, branding, organization = 'lefteria_fc', navigationGroups = [], canonicalPath, children }: { site: PublicSite | null; branding?: PublicClub | null; organization?: PublicOrganization; navigationGroups?: PublicGroup[]; canonicalPath?: string; children: Snippet } = $props();
  const club = $derived(branding === undefined ? site?.club ?? null : branding);
  const globalTheme=$derived(publishedGlobalDesign(page.data?.websitePages?.[organization]??[],organization));
  const embeddedSections=$derived(embeddedWebsiteSections(page.data?.websitePages?.[organization]??[],organization,page.url.pathname,page.url.searchParams));
</script>
<svelte:head>
  <link rel="canonical" href={publicCanonicalHref(canonicalPath ?? page.url.pathname)} />
  {#if !isIndexablePublicOrigin(page.url)}<meta name="robots" content="noindex,nofollow" />{/if}
</svelte:head>
<div class="public-site" lang="el" data-ui-refresh="2026" data-organization={organization} data-global-design={globalTheme ? "true" : undefined} style={globalTheme ? globalDesignCssVars(globalTheme) : undefined} data-route={page.route?.id ?? page.url?.pathname} data-testid="public-site">
  <PublicNavigation {club} {organization} groups={navigationGroups} />
  <div class="public-content">
    <main id="public-main" class="public-main" tabindex="-1" data-testid="public-main">
      {@render children()}
      {#each embeddedSections as section (section.slug)}
        <section id={embeddedWebsiteSectionId(section.slug)} class="public-section public-embedded-page"
          aria-label={section.page.title} data-testid="public-embedded-page">
          <div class="public-embedded-heading section-heading">
            <h2>{section.page.title}</h2>
            {#if section.page.summary}<p>{section.page.summary}</p>{/if}
          </div>
          <WebsitePageBlocks blocks={section.page.blocks} pageLayout={section.page.layout}/>
        </section>
      {/each}
    </main>
    <PublicFooter {club} {organization} />
  </div>
</div>
<CookieConsent />

<style>
  /* One native section destination per published Pages menu item. */
  .public-embedded-page{scroll-margin-top:calc(var(--public-header,64px) + 18px)}
  .public-embedded-heading{margin:0 auto 24px;max-width:1280px;display:grid;gap:9px}
  .public-embedded-heading h2{display:flex;align-items:center;gap:12px;margin:0;font:800 clamp(25px,2.4vw,34px)/1.15 'Fira Sans Extra Condensed',sans-serif}
  .public-embedded-heading h2::before{content:"";display:block;flex:0 0 5px;height:24px;background:var(--public-gold);transform:skew(-18deg)}
  .public-embedded-heading p{margin:0;max-width:72ch;color:var(--lf-color-muted,#c6c6c6)}
</style>
