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
  import { embeddedWebsiteSections, embeddedWebsiteSectionId, validPageLayout, type WebsitePageLayout } from './website-pages';
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
  // Only strict, published and validated page dimensions may override the
  // enclosing organization's global defaults for this single CMS section.
  const embeddedDimension=(layout:WebsitePageLayout|null|undefined,key:'max_width'|'side_padding'|'gap',globalToken:string,fallback:number)=>
    validPageLayout(layout)&&layout?.[key]!=null?layout[key]+'px':
      'var(--lf-layout-'+globalToken+','+fallback+'px)';
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
          aria-label={section.page.title} data-testid="public-embedded-page"
          style={`--lf-embedded-page-max-width:${embeddedDimension(section.page.layout,'max_width','page-max-width',1280)};--lf-embedded-page-side:${embeddedDimension(section.page.layout,'side_padding','page-side-padding',16)};--lf-embedded-page-gap:${embeddedDimension(section.page.layout,'gap','page-gap',24)}`}>
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
  <!-- Keep cookie controls inside this organization scope, so published
       palette/typography reach the public consent dialog as well. -->
  <CookieConsent />
</div>

<style>
  /* One native section destination per published Pages menu item. */
  .public-embedded-page{scroll-margin-top:calc(var(--public-header,64px) + 18px)}
  .public-embedded-heading{margin:0 auto var(--lf-embedded-page-gap,var(--lf-layout-page-gap,24px));max-width:var(--lf-embedded-page-max-width,1280px);padding-inline:var(--lf-embedded-page-side,0px);display:grid;gap:var(--lf-layout-card-gap,9px)}
  .public-embedded-heading h2{display:flex;align-items:center;gap:12px;margin:0;font-family:var(--lf-h2-family,'Fira Sans Extra Condensed',sans-serif);font-size:var(--lf-h2-desktop,clamp(25px,2.4vw,34px));font-weight:var(--lf-h2-weight,800);line-height:var(--lf-h2-line,1.15)}
  .public-embedded-heading h2::before{content:"";display:block;flex:0 0 5px;height:24px;background:var(--public-gold);transform:skew(-18deg)}
  .public-embedded-heading p{margin:0;max-width:72ch;color:var(--lf-color-muted,#c6c6c6)}
@media(max-width:767px){.public-embedded-heading h2{font-size:var(--lf-h2-mobile,clamp(25px,2.4vw,34px))}}
</style>
