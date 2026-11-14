<script lang="ts">
  import PublicShell from '../lib/public/PublicShell.svelte';
  import HomeHero from '../lib/public/HomeHero.svelte';
  import HomeDestinations from '../lib/public/HomeDestinations.svelte';
  import HomeNews from '../lib/public/HomeNews.svelte';
  import SidelineBanner from '../lib/public/SidelineBanner.svelte';
  import Fixtures from '../lib/public/Fixtures.svelte';
  import Unavailable from '../lib/public/Unavailable.svelte';
  import type { PublicHomeData } from '../lib/public/types';
  let { data }: { data: PublicHomeData } = $props();
</script>
<svelte:head><title>ΛΕΥΤΕΡΙΑ FC | Η ΕΠΙΣΗΜΗ ΙΣΤΟΣΕΛΙΔΑ</title><meta name="description" content={data.site?.club?.description || 'Η επίσημη ιστοσελίδα της Λευτεριάς FC. Πρώτη ομάδα, ακαδημία, αγώνες και νέα.'} /><meta property="og:title" content="ΛΕΥΤΕΡΙΑ FC" /><meta property="og:type" content="website" /></svelte:head>
<PublicShell site={data.site} branding={data.branding} organization={data.organization} navigationGroups={data.navigationGroups}>
  <div data-testid="home-page"><HomeHero site={data.site} navigationGroups={data.navigationGroups} />
    {#if !data.available}<Unavailable />{:else}<HomeDestinations site={data.site} groups={data.navigationGroups} /><SidelineBanner />{/if}
    <HomeNews articles={data.news?.items ?? []} available={data.newsAvailable} />
    {#if data.available}
      <section class="public-section section-dark"><div class="public-section-heading"><h2>ΑΓΩΝΕΣ & ΑΠΟΤΕΛΕΣΜΑΤΑ</h2><a href="/team?tab=schedule">ΤΟ ΠΡΟΓΡΑΜΜΑ ↗</a></div>{#if data.site?.fixtures.length}<Fixtures fixtures={data.site.fixtures.slice(0,4)} />{:else}<p class="public-empty">Δεν έχουν καταχωριστεί αγώνες.</p>{/if}</section>
    {/if}
  </div>
</PublicShell>
