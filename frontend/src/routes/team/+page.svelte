<script lang="ts">
  import PublicShell from '../../lib/public/PublicShell.svelte';
  import PublicImage from '../../lib/public/PublicImage.svelte';
  import Fixtures from '../../lib/public/Fixtures.svelte';
  import Unavailable from '../../lib/public/Unavailable.svelte';
  import type { PublicPageData } from '../../lib/public/types';
  import { page } from '$app/state';
  let { data }: { data: PublicPageData } = $props();
  const tab = $derived(page.url.searchParams.get('tab') === 'results' ? 'results' : page.url.searchParams.get('tab') === 'schedule' ? 'schedule' : 'overview');
  const fixtures = $derived((data.site?.fixtures ?? []).filter(item => tab === 'results' ? item.status === 'Completed' : tab === 'schedule' ? item.status !== 'Completed' : true));
</script>
<svelte:head><title>ΠΡΩΤΗ ΟΜΑΔΑ | ΛΕΥΤΕΡΙΑ FC</title><meta name="description" content="Η πρώτη ομάδα της Λευτεριάς FC. Αγώνες, πρόγραμμα και αποτελέσματα." /></svelte:head>
<PublicShell site={data.site} organization="lefteria_fc" navigationGroups={data.navigationGroups}><section class="public-page-heading"><p class="public-eyebrow">ΤΑ ΧΡΩΜΑΤΑ ΜΑΣ. Η ΠΕΡΗΦΑΝΙΑ ΜΑΣ.</p><h1>ΠΡΩΤΗ ΟΜΑΔΑ</h1><nav class="public-subnav" aria-label="Πρώτη ομάδα"><a href="/team" aria-current={tab === 'overview' ? 'page' : undefined}>Η ΟΜΑΔΑ</a><a href="/team?tab=schedule" aria-current={tab === 'schedule' ? 'page' : undefined}>ΠΡΟΓΡΑΜΜΑ</a><a href="/team?tab=results" aria-current={tab === 'results' ? 'page' : undefined}>ΑΠΟΤΕΛΕΣΜΑΤΑ</a></nav></section>
  {#if !data.available}<Unavailable />{:else}<section class="public-section">{#if tab === 'overview'}{#each data.site?.teams ?? [] as team (team.id)}<article class="public-team-overview">{#if team.banner_url}<PublicImage src={team.banner_url} alt="" />{/if}<div><p class="public-eyebrow">{team.level || 'ΛΕΥΤΕΡΙΑ FC'}</p><h2>{team.name}</h2>{#if team.description}<p class="preserve-lines">{team.description}</p>{/if}</div></article>{/each}{/if}<div class="section-heading"><h2>{tab === 'results' ? 'ΑΠΟΤΕΛΕΣΜΑΤΑ' : 'ΑΓΩΝΕΣ & ΠΡΟΓΡΑΜΜΑ'}</h2></div>{#if fixtures.length}<Fixtures {fixtures} />{:else}<p class="public-empty">Δεν έχουν καταχωριστεί αγώνες σε αυτή την ενότητα.</p>{/if}</section>{/if}
</PublicShell>
