<script lang="ts">
  import PublicShell from '../lib/public/PublicShell.svelte';
  import HomeHero from '../lib/public/HomeHero.svelte';
  import NewsCards from '../lib/public/NewsCards.svelte';
  import Fixtures from '../lib/public/Fixtures.svelte';
  import Unavailable from '../lib/public/Unavailable.svelte';
  import type { PublicHomeData } from '../lib/public/types';
  let { data }: { data: PublicHomeData } = $props();
</script>
<svelte:head><title>ΛΕΥΤΕΡΙΑ FC | Η ΕΠΙΣΗΜΗ ΙΣΤΟΣΕΛΙΔΑ</title><meta name="description" content={data.site?.club?.description || 'Η επίσημη ιστοσελίδα της Λευτεριάς FC. Πρώτη ομάδα, ακαδημία, αγώνες και νέα.'} /><meta property="og:title" content="ΛΕΥΤΕΡΙΑ FC" /><meta property="og:type" content="website" /></svelte:head>
<PublicShell site={data.site} organization={data.organization} navigationGroups={data.navigationGroups}>
  <HomeHero site={data.site} navigationGroups={data.navigationGroups} />
  {#if !data.available}<Unavailable />{:else}
    <section class="public-destinations" aria-label="Εξερεύνησε τη Λευτεριά"><a href="/academy"><span>01</span><h2>ΑΚΑΔΗΜΙΑ</h2><p>Τα ηλικιακά μας τμήματα</p><span aria-hidden="true">↗</span></a><a href="/team"><span>02</span><h2>ΠΡΩΤΗ ΟΜΑΔΑ</h2><p>Η ομάδα μας</p><span aria-hidden="true">↗</span></a><a href="/team?tab=schedule"><span>03</span><h2>ΠΡΟΓΡΑΜΜΑ</h2><p>Αγώνες και αποτελέσματα</p><span aria-hidden="true">↗</span></a><a href="/news"><span>04</span><h2>ΤΑ ΝΕΑ ΜΑΣ</h2><p>Νέα και ανακοινώσεις</p><span aria-hidden="true">↗</span></a></section>
    <section class="sideline-banner" aria-label="Sideline Society"><img class="sideline-photo" src="/club/sideline-background.webp" alt="" loading="lazy" /><div class="sideline-copy"><img src="/club/wear-the-colors.png" alt="WEAR THE COLORS" /><img src="/club/sideline-society.png" alt="SIDELINE SOCIETY" /><img src="/club/together-on-the-journey.png" alt="TOGETHER ON THE JOURNEY" /><img src="/club/same-passion-different-position.png" alt="SAME PASSION DIFFERENT POSITION" /></div></section>
  {/if}
    <section class="public-section"><div class="section-heading"><div><p class="public-eyebrow">ΤΕΛΕΥΤΑΙΑ ΝΕΑ</p><h2>ΝΕΑ & ΑΝΑΚΟΙΝΩΣΕΙΣ</h2></div><a href="/news">ΟΛΑ ΤΑ ΝΕΑ ↗</a></div>{#if !data.newsAvailable}<Unavailable />{:else if data.news?.items.length}<NewsCards articles={data.news.items} />{:else}<p class="public-empty">Δεν υπάρχουν δημοσιευμένα νέα.</p>{/if}</section>
  {#if data.available}
    <section class="public-section section-dark"><div class="section-heading"><div><p class="public-eyebrow">ΠΡΩΤΗ ΟΜΑΔΑ</p><h2>ΑΓΩΝΕΣ & ΑΠΟΤΕΛΕΣΜΑΤΑ</h2></div><a href="/team?tab=schedule">ΤΟ ΠΡΟΓΡΑΜΜΑ ↗</a></div>{#if data.site?.fixtures.length}<Fixtures fixtures={data.site.fixtures.slice(0, 4)} />{:else}<p class="public-empty">Δεν έχουν καταχωριστεί αγώνες.</p>{/if}</section>
  {/if}
</PublicShell>
