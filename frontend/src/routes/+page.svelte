<script lang="ts">
  import { page } from '$app/state';
  import PublicShell from '../lib/public/PublicShell.svelte';
  import HomeHero from '../lib/public/HomeHero.svelte';
  import HomeDestinations from '../lib/public/HomeDestinations.svelte';
  import HomeNews from '../lib/public/HomeNews.svelte';
  import SidelineBanner from '../lib/public/SidelineBanner.svelte';
  import HomeAcademySpotlight from '../lib/public/HomeAcademySpotlight.svelte';
  import Fixtures from '../lib/public/Fixtures.svelte';
  import Unavailable from '../lib/public/Unavailable.svelte';
  import '../lib/public/home-reference.css';
  import type { PublicHomeData } from '../lib/public/types';
  import type { WebsiteNavigationData } from '../lib/public/website-pages';
  import { homepageHeroPage, homepageAcademySpotlightPage } from '../lib/public/homepage-editorial';
  let { data }: { data: PublicHomeData } = $props();
  const websitePages = $derived((page.data as {websitePages?: WebsiteNavigationData} | undefined)?.websitePages);
  const academyEditorial = $derived(homepageHeroPage(websitePages?.lefteria_fc_academy ?? [], 'lefteria_fc_academy'));
  const teamEditorial = $derived(homepageHeroPage(websitePages?.lefteria_fc ?? [], 'lefteria_fc'));
  const academySpotlight = $derived(homepageAcademySpotlightPage(websitePages?.lefteria_fc_academy ?? [], 'lefteria_fc_academy'));
</script>
<svelte:head><title>ΛΕΥΤΕΡΙΑ FC | Η ΕΠΙΣΗΜΗ ΙΣΤΟΣΕΛΙΔΑ</title><meta name="description" content={data.site?.club?.description || 'Η επίσημη ιστοσελίδα της Λευτεριάς FC. Πρώτη ομάδα, ακαδημία, αγώνες και νέα.'} /><meta property="og:title" content="ΛΕΥΤΕΡΙΑ FC" /><meta property="og:type" content="website" /></svelte:head>
<PublicShell site={data.site} branding={data.branding} organization={data.organization} navigationGroups={data.navigationGroups}>
  <div class="home-reference" data-testid="home-page"><HomeHero site={data.site} navigationGroups={data.navigationGroups} {academyEditorial} {teamEditorial} />
    <HomeDestinations site={data.site} groups={data.navigationGroups} />
    <section class="home-brand-interval" aria-label="Μία ομάδα, ένα όνειρο" data-testid="home-brand-interval">
      <div class="home-brand-bottom">
        <span class="home-brand-stripe" aria-hidden="true"></span>
        <span class="home-brand-motto">ONE TEAM <span aria-hidden="true">●</span> ONE DREAM</span>
        <span class="home-brand-stripe" aria-hidden="true"></span>
      </div>
    </section>
    {#if !data.available && data.newsAvailable}<div class="home-public-feed-unavailable" role="status" data-testid="home-source-unavailable"><Unavailable /></div>{/if}
    <HomeNews articles={data.news?.items ?? []} available={data.newsAvailable} />

    {#if data.available}
      <section class="public-section section-dark home-matches-section" data-testid="home-matches-section"><div class="public-section-heading"><h2>ΑΓΩΝΕΣ & ΑΠΟΤΕΛΕΣΜΑΤΑ</h2><a href="/team?tab=schedule">ΤΟ ΠΡΟΓΡΑΜΜΑ ↗</a></div>{#if data.site?.fixtures.length}<Fixtures fixtures={data.site.fixtures.slice(0,4)} />{:else}<p class="public-empty">Δεν έχουν καταχωριστεί αγώνες.</p>{/if}</section>
      <HomeAcademySpotlight site={data.site} groups={data.navigationGroups} editorial={academySpotlight} />
      <SidelineBanner />
    {/if}
  </div>
</PublicShell>
