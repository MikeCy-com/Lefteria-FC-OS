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
    <div class="group-hero-copy"><nav class="breadcrumbs" aria-label="Διαδρομή"><a href="/academy">Ακαδημία</a><span aria-hidden="true">/</span><a href="/academy/groups">Ηλικιακά τμήματα</a><span aria-hidden="true">/</span><span aria-current="page">{group.name}</span></nav>
      <p class="public-eyebrow">ΑΚΑΔΗΜΙΑ ΛΕΥΤΕΡΙΑ FC{group.season ? ` · ${group.season}` : ''}</p><h1>{group.name}</h1>
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

<style>
  .group-hero { position:relative; min-height:340px; overflow:hidden; background:linear-gradient(115deg,#282015,#090909 80%); }
  .group-hero-image { position:absolute; inset:0; } .group-hero-image :global(img) { width:100%; height:100%; object-fit:cover; opacity:.42; }
  .group-hero-image::after { content:''; position:absolute; inset:0; background:linear-gradient(90deg,#080808ed,#08080840); }
  .group-hero-copy { position:relative; padding:30px clamp(22px,5vw,76px) 42px; max-width:1000px; }
  .breadcrumbs { display:flex; flex-wrap:wrap; gap:12px; font-size:13px; color:#bbb; margin-bottom:40px; }
  .breadcrumbs a { color:inherit; text-decoration:none; } .breadcrumbs a:hover { color:var(--public-gold); }
  .group-hero h1 { font-family:'Fira Sans Extra Condensed',sans-serif; font-size:clamp(56px,8vw,100px); font-weight:800; line-height:1; margin:12px 0; overflow-wrap:anywhere; }
  .group-age { color:var(--public-gold); font-size:20px; margin:0 0 18px; font-weight:700; }
  .group-description { color:#ddd; font-size:17px; line-height:1.7; white-space:pre-line; max-width:650px; }
  .back-groups { display:inline-flex; color:var(--public-gold); font-size:13px; font-weight:700; text-decoration:none; padding:14px 0; margin-top:12px; }
  .group-tabs { display:flex; flex-wrap:wrap; gap:16px 30px; padding:16px clamp(22px,5vw,76px); border-bottom:1px solid #ffffff25; background:#151515; }
  .group-tabs a { color:#eee; text-decoration:none; padding:8px 0; font-family:'Fira Sans Extra Condensed',sans-serif; font-size:18px; font-weight:700; }
  .group-tabs a:hover { color:var(--public-gold); }
  .group-information { display:grid; grid-template-columns:1fr 1fr; gap:40px; padding:50px clamp(22px,5vw,76px); scroll-margin-top:100px; }
  .group-information h2,.group-matches h2 { font-family:'Fira Sans Extra Condensed',sans-serif; font-size:clamp(27px,3vw,40px); line-height:1.1; margin:12px 0 24px; }
  .information-copy { color:#bbb; line-height:1.7; font-size:16px; max-width:440px; }
  .training-panel { background:#151515; border-top:3px solid var(--public-gold); padding:26px; }
  .training-panel h3 { font-family:'Fira Sans Extra Condensed',sans-serif; font-size:21px; margin:0 0 18px; }
  .training-schedule { color:#eee; line-height:1.8; white-space:pre-line; font-size:16px; }
  .training-venue { display:grid; gap:8px; border-top:1px solid #ffffff20; padding-top:20px; margin-top:22px; }
  .training-venue span { color:#aaa; font-size:11px; letter-spacing:.06em; } .training-venue strong { font-size:16px; line-height:1.5; font-weight:500; }
  .training-panel>a { display:flex; justify-content:space-between; gap:20px; margin-top:24px; color:var(--public-gold); font-size:14px; text-decoration:none; padding:10px 0; }
  .group-matches { padding:20px clamp(22px,5vw,76px) 44px; scroll-margin-top:100px; }
  .group-results { background:#111; padding-top:34px; } .empty-group { color:#bbb; padding:22px 0; line-height:1.6; }
  a:focus-visible { outline:2px solid var(--public-gold); outline-offset:5px; }
  @media(max-width:750px) { .group-information { grid-template-columns:1fr; gap:22px; padding-top:36px; } .group-hero-copy { padding-top:24px; } .breadcrumbs { margin-bottom:28px; } .group-tabs { gap:12px 22px; } .group-tabs a { font-size:16px; } }
</style>
