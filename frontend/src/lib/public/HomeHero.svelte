<script lang="ts">
  import { onMount } from 'svelte';
  import { greekDate } from './content';
  import type { PublicGroup, PublicSite } from './types';
  let { site, navigationGroups = [] }: { site: PublicSite | null; navigationGroups?: PublicGroup[] } = $props();
  let index = $state(0); let paused = $state(false); let visible = $state(true); let reduced = $state(false);
  let region: HTMLElement;
  const club = $derived(site?.club);
  const team = $derived(site?.teams[0]);
  const hero = 'https://static.prod-images.emergentagent.com/jobs/bf6c3d8c-bb69-4407-a80f-3421fc7ad126/images/89d7357a5d918ed9f0adf953c27f04db5e7a7c5f78814f7bd3c449710aab9c29.jpeg';
  const backgrounds = $derived([navigationGroups.find(group => group.banner_url)?.banner_url || hero, team?.banner_url || club?.stadium_image_url || hero]);
  const today = new Date().toISOString().slice(0, 10);
  const nextMatch = $derived(site?.fixtures.filter(fixture => fixture.status === 'Scheduled' && fixture.match_date >= today).sort((a, b) => a.match_date.localeCompare(b.match_date))[0]);
  onMount(() => {
    const motion = window.matchMedia('(prefers-reduced-motion: reduce)');
    const updateMotion = () => { reduced = motion.matches; };
    updateMotion(); motion.addEventListener('change', updateMotion);
    const observer = new IntersectionObserver(entries => { visible = entries.some(entry => entry.isIntersecting); });
    observer.observe(region);
    const timer = window.setInterval(() => { if (!paused && !reduced && visible && document.visibilityState === 'visible') index = (index + 1) % 2; }, 7000);
    return () => { window.clearInterval(timer); observer.disconnect(); motion.removeEventListener('change', updateMotion); };
  });
</script>
<section class="public-home-hero" bind:this={region} aria-roledescription="καρουζέλ" aria-label="Ακαδημία και πρώτη ομάδα" onfocusin={() => { paused = true; }}>
  {#each backgrounds as image, position}<div class:active={index === position} class="hero-photo" style:background-image={`url("${image.replaceAll('"', '%22')}")`} aria-hidden="true"></div>{/each}
  <div class="hero-copy" aria-live={paused || reduced ? 'polite' : 'off'}><p class="public-eyebrow">{index === 0 ? club?.name || 'ΛΕΥΤΕΡΙΑ FC' : 'ΠΡΩΤΗ ΟΜΑΔΑ'}</p>
    {#if index === 0}<h1>Η ΠΟΛΗ ΜΑΣ.<br /><em>ΤΑ ΠΑΙΔΙΑ ΜΑΣ.</em><br />ΤΟ ΜΕΛΛΟΝ ΤΟΥΣ.</h1>{:else}<h1>ΤΑ ΧΡΩΜΑΤΑ ΜΑΣ.<br /><em>Η ΠΕΡΗΦΑΝΙΑ ΜΑΣ.</em></h1>{/if}
    {#if club?.description}<p class="hero-description">{club.description}</p>{/if}
    <div class="hero-actions"><a class="public-button" href={index === 0 ? '/academy' : '/team'}>{index === 0 ? 'ΔΕΣ ΤΗΝ ΑΚΑΔΗΜΙΑ' : 'ΓΝΩΡΙΣΕ ΤΗΝ ΟΜΑΔΑ'} <span aria-hidden="true">↗</span></a><a class="public-button public-button-outline" href={index === 0 ? '/team' : '/team?tab=schedule'}>{index === 0 ? 'Η ΠΡΩΤΗ ΟΜΑΔΑ' : 'ΠΡΟΓΡΑΜΜΑ ΑΓΩΝΩΝ'} <span aria-hidden="true">↗</span></a></div>
  </div>
  {#if nextMatch}<aside class="hero-fixture"><p class="public-eyebrow">ΕΠΟΜΕΝΟΣ ΑΓΩΝΑΣ</p><p>{nextMatch.competition}</p><time datetime={nextMatch.match_date}>{greekDate(nextMatch.match_date)}{nextMatch.match_time ? ` · ${nextMatch.match_time}` : ''}</time><div><strong>{nextMatch.home_team}</strong><span>—</span><strong>{nextMatch.away_team}</strong></div>{#if nextMatch.venue}<p>{nextMatch.venue}</p>{/if}<a href="/team?tab=schedule">ΟΛΟ ΤΟ ΠΡΟΓΡΑΜΜΑ ↗</a></aside>{/if}
  <div class="hero-bottom"><p>{club?.motto || 'ΜΙΑ ΟΜΑΔΑ. ΕΝΑ ΟΝΕΙΡΟ.'}</p><div class="hero-controls"><button aria-label="Προηγούμενη διαφάνεια" onclick={() => { paused = true; index = (index + 1) % 2; }}>←</button><span>{index + 1} / 2</span><button aria-label="Επόμενη διαφάνεια" onclick={() => { paused = true; index = (index + 1) % 2; }}>→</button><button aria-label={paused ? 'Συνέχιση αυτόματης εναλλαγής' : 'Παύση αυτόματης εναλλαγής'} aria-pressed={paused} onclick={() => { paused = !paused; }}>{paused ? '▷' : 'Ⅱ'}</button></div></div>
</section>
