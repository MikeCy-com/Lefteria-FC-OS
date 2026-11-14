<script lang="ts">
  import PublicShell from '../../../lib/public/PublicShell.svelte';
  import PublicImage from '../../../lib/public/PublicImage.svelte';
  import Fixtures from '../../../lib/public/Fixtures.svelte';
  import type { PublicPageData, PublicGroup, PublicFixture } from '../../../lib/public/types';
  let { data }: { data: PublicPageData & { group: PublicGroup; groupFixtures: PublicFixture[] } } = $props();
  const group = $derived(data.group);
  const upcoming = $derived(data.groupFixtures.filter(fixture => fixture.status !== 'Completed'));
  const results = $derived(data.groupFixtures.filter(fixture => fixture.status === 'Completed').reverse());
</script>

<svelte:head><title>{group.name} | ΑΚΑΔΗΜΙΑ ΛΕΥΤΕΡΙΑ FC</title><meta name="description" content={group.description || `Πληροφορίες, προπονήσεις και αγώνες του τμήματος ${group.name} της ακαδημίας Λευτεριά FC.`} /></svelte:head>
<PublicShell site={data.site} branding={data.branding} organization="lefteria_fc_academy" navigationGroups={data.navigationGroups}>
  <section class="group-hero">
    {#if group.banner_url}<div class="group-hero-image"><PublicImage src={group.banner_url} alt="" eager /></div>{/if}
    <div class="group-hero-copy"><p class="public-eyebrow">ΑΚΑΔΗΜΙΑ ΛΕΥΤΕΡΙΑ FC{group.season ? ` · ${group.season}` : ''}</p><h1>{group.name}</h1>
      {#if group.age_range}<p class="group-age">{group.age_range}</p>{/if}
      {#if group.description}<p class="group-description">{group.description}</p>{/if}
      <a class="back-groups" href="/academy/groups">← ΟΛΑ ΤΑ ΤΜΗΜΑΤΑ</a>
    </div>
  </section>
  <nav class="group-tabs" aria-label="Πληροφορίες τμήματος"><a href="#training">ΠΡΟΠΟΝΗΣΕΙΣ</a><a href="#matches">ΑΓΩΝΕΣ</a><a href="#results">ΑΠΟΤΕΛΕΣΜΑΤΑ</a></nav>
  <section class="group-information" id="training" aria-labelledby="training-heading">
    <div><p class="public-eyebrow">ΤΟ ΤΜΗΜΑ ΜΑΣ</p><h2 id="training-heading">ΜΑΖΙ ΣΤΟ ΓΗΠΕΔΟ.</h2><p class="information-copy">Το καταχωρισμένο πρόγραμμα και ο χώρος προπονήσεων για το τμήμα {group.name}.</p></div>
    <div class="training-panel"><h3>ΠΡΟΓΡΑΜΜΑ ΠΡΟΠΟΝΗΣΕΩΝ</h3><p class="training-schedule">{group.training_schedule || 'Δεν έχει καταχωριστεί πρόγραμμα προπονήσεων.'}</p>
      {#if group.location_name || group.city}<div class="training-venue"><span>ΧΩΡΟΣ ΠΡΟΠΟΝΗΣΕΩΝ</span><strong>{[group.location_name, group.city].filter(Boolean).join(' · ')}</strong></div>{/if}
      <a href="/contact">Επικοινωνία με τον σύλλογο <span aria-hidden="true">↗</span></a>
    </div>
  </section>
  <section class="group-matches" id="matches" aria-labelledby="matches-heading"><p class="public-eyebrow">ΤΟ ΠΡΟΓΡΑΜΜΑ ΜΑΣ</p><h2 id="matches-heading">ΑΓΩΝΕΣ ΤΜΗΜΑΤΟΣ</h2>
    {#if upcoming.length}<Fixtures fixtures={upcoming} />{:else}<p class="empty-group">Δεν έχουν καταχωριστεί προσεχείς αγώνες για αυτό το τμήμα.</p>{/if}
  </section>
  <section class="group-matches group-results" id="results" aria-labelledby="results-heading"><h2 id="results-heading">ΑΠΟΤΕΛΕΣΜΑΤΑ</h2>
    {#if results.length}<Fixtures fixtures={results} />{:else}<p class="empty-group">Δεν έχουν καταχωριστεί αποτελέσματα για αυτό το τμήμα.</p>{/if}
  </section>
</PublicShell>

