<script lang="ts">
  import PublicShell from '../../../lib/public/PublicShell.svelte';
  import PublicImage from '../../../lib/public/PublicImage.svelte';
  import { academyGroupLink } from '../../../lib/public/navigation';
  import type { PublicPageData } from '../../../lib/public/types';
  let { data }: { data: PublicPageData } = $props();
  const groups = $derived((data.site?.academy_groups ?? []).filter(group => group.organization_id === 'lefteria_fc_academy'));
</script>

<svelte:head><title>ΗΛΙΚΙΑΚΑ ΤΜΗΜΑΤΑ | ΛΕΥΤΕΡΙΑ FC</title><meta name="description" content="Τα ηλικιακά τμήματα της ακαδημίας Λευτεριά FC, οι χώροι και τα προγράμματα προπονήσεών τους." /></svelte:head>
<PublicShell site={data.site} branding={data.branding} organization="lefteria_fc_academy" navigationGroups={data.navigationGroups}>
  <section class="academy-intro">
    <nav class="breadcrumbs" aria-label="Διαδρομή"><a href="/">Αρχική</a><span aria-hidden="true">/</span><a href="/academy">Ακαδημία</a><span aria-hidden="true">/</span><span aria-current="page">Ηλικιακά τμήματα</span></nav>
    <p class="public-eyebrow">ΑΚΑΔΗΜΙΑ ΛΕΥΤΕΡΙΑ FC</p><h1>ΜΙΚΡΑ ΒΗΜΑΤΑ.<br /><em>ΜΕΓΑΛΑ ΟΝΕΙΡΑ.</em></h1>
    <p class="intro-copy">Γνώρισε τα τμήματά μας και βρες τις πληροφορίες προπονήσεων και αγώνων για κάθε ηλικιακή ομάδα.</p>
    <nav class="academy-section-links" aria-label="Ακαδημία"><a href="/academy">Η ακαδημία μας</a><a href="/academy/philosophy">Η φιλοσοφία μας</a><a href="/contact">Επικοινωνία</a></nav>
  </section>
  <section class="academy-directory" aria-labelledby="groups-heading">
    <div class="directory-heading"><h2 id="groups-heading">ΤΑ ΤΜΗΜΑΤΑ ΜΑΣ</h2><span>{groups.length} {groups.length === 1 ? 'τμήμα' : 'τμήματα'}</span></div>
    <div class="academy-cards">{#each groups as group (group.id)}
      <a class="academy-card" href={academyGroupLink(group)} data-testid="academy-group-card">
        <div class="card-visual">{#if group.banner_url}<PublicImage src={group.banner_url} alt="" />{:else}<span class="group-monogram" aria-hidden="true">{group.name}</span>{/if}<span class="card-arrow" aria-hidden="true">↗</span></div>
        <div class="card-content"><p class="public-eyebrow">{group.age_range || 'ΑΚΑΔΗΜΙΑ'}{group.season ? ` · ${group.season}` : ''}</p><h3>{group.name}</h3>
          {#if group.description}<p class="card-description">{group.description}</p>{/if}
          {#if group.city || group.location_name}<p class="card-location">{[group.city, group.location_name].filter(Boolean).join(' · ')}</p>{/if}
          <span class="card-action">ΓΝΩΡΙΣΕ ΤΟ ΤΜΗΜΑ <span aria-hidden="true">→</span></span>
        </div>
      </a>
    {:else}<p class="public-empty">Δεν έχουν καταχωριστεί ηλικιακά τμήματα.</p>{/each}</div>
  </section>
</PublicShell>

<style>
  .academy-intro { padding:34px clamp(22px,5vw,76px) 46px; background:linear-gradient(110deg,#17140e,#090909 75%); border-bottom:1px solid #ffffff18; }
  .breadcrumbs { display:flex; flex-wrap:wrap; gap:12px; color:#aaa; font-size:13px; margin-bottom:38px; }
  .breadcrumbs a { color:inherit; text-decoration:none; } .breadcrumbs a:hover { color:var(--public-gold); }
  .academy-intro h1 { font-family:'Fira Sans Extra Condensed',sans-serif; font-size:clamp(42px,5.5vw,78px); line-height:1.02; letter-spacing:-.025em; margin:12px 0 22px; }
  .academy-intro em { font-style:normal; color:var(--public-gold); }
  .intro-copy { max-width:620px; color:#ccc; font-size:17px; line-height:1.7; }
  .academy-section-links { display:flex; flex-wrap:wrap; gap:12px 28px; margin-top:30px; }
  .academy-section-links a { color:#eee; text-decoration:none; border-bottom:1px solid #ffffff40; padding-bottom:8px; min-height:40px; }
  .academy-directory { padding:36px clamp(22px,5vw,76px) 72px; }
  .directory-heading { display:flex; justify-content:space-between; align-items:center; gap:18px; margin-bottom:28px; }
  .directory-heading h2 { font-family:'Fira Sans Extra Condensed',sans-serif; font-size:28px; margin:0; } .directory-heading>span { color:#aaa; font-size:14px; }
  .academy-cards { display:grid; grid-template-columns:repeat(3,minmax(0,1fr)); gap:24px; }
  .academy-card { display:block; text-decoration:none; color:#fff; background:#141414; border:1px solid #ffffff18; transition:border-color .18s; }
  .academy-card:hover { border-color:var(--public-gold); } .academy-card:focus-visible { outline:3px solid var(--public-gold); outline-offset:5px; }
  .card-visual { position:relative; overflow:hidden; aspect-ratio:1.6; display:grid; place-items:center; background:linear-gradient(135deg,#252015,#111); }
  .card-visual :global(img) { width:100%; height:100%; object-fit:cover; }
  .group-monogram { font-family:'Fira Sans Extra Condensed',sans-serif; font-size:clamp(38px,5vw,64px); font-weight:800; color:var(--public-gold); padding:20px; overflow-wrap:anywhere; }
  .card-arrow { position:absolute; right:14px; bottom:14px; width:38px; height:38px; display:grid; place-items:center; background:var(--public-gold); color:#111; font-size:24px; }
  .card-content { padding:24px; } .card-content h3 { font-family:'Fira Sans Extra Condensed',sans-serif; font-size:32px; line-height:1.1; margin:10px 0 14px; }
  .card-description { color:#ccc; line-height:1.65; font-size:15px; } .card-location { color:#aaa; margin-top:18px; font-size:14px; }
  .card-action { display:flex; align-items:center; justify-content:space-between; gap:15px; border-top:1px solid #ffffff1c; padding-top:18px; margin-top:22px; font-size:13px; font-weight:700; color:var(--public-gold); }
  @media(max-width:1100px) { .academy-cards { grid-template-columns:repeat(2,minmax(0,1fr)); } }
  @media(max-width:600px) { .academy-cards { grid-template-columns:1fr; } .academy-intro { padding-top:24px; } .breadcrumbs { margin-bottom:28px; } .directory-heading h2 { font-size:25px; } }
</style>
