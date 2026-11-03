<script lang="ts">
  import PublicShell from '../../lib/public/PublicShell.svelte';
  import PublicImage from '../../lib/public/PublicImage.svelte';
  import Fixtures from '../../lib/public/Fixtures.svelte';
  import Unavailable from '../../lib/public/Unavailable.svelte';
  import type { PublicPageData } from '../../lib/public/types';
  import { academyGroupLink } from '../../lib/public/navigation';
  let { data }: { data: PublicPageData } = $props();
</script>
<svelte:head><title>ΑΚΑΔΗΜΙΑ | ΛΕΥΤΕΡΙΑ FC</title><meta name="description" content="Η ακαδημία της Λευτεριάς FC. Γνώρισε τα ηλικιακά τμήματα και το πρόγραμμα αγώνων." /></svelte:head>
<PublicShell site={data.site} branding={data.branding} organization="lefteria_fc_academy" navigationGroups={data.navigationGroups}><section class="public-page-heading"><p class="public-eyebrow">ΤΑ ΠΑΙΔΙΑ ΜΑΣ. ΤΟ ΜΕΛΛΟΝ ΤΟΥΣ.</p><h1>Η ΑΚΑΔΗΜΙΑ ΜΑΣ</h1>{#if data.site?.club?.description}<p>{data.site.club.description}</p>{/if}<nav class="public-subnav" aria-label="Ακαδημία"><a href="/academy/groups">ΗΛΙΚΙΑΚΑ ΤΜΗΜΑΤΑ</a><a href="#academy-fixtures">ΑΓΩΝΕΣ</a><a href="/news?organization_id=lefteria_fc_academy">ΝΕΑ ΑΚΑΔΗΜΙΑΣ ↗</a></nav></section>
  {#if !data.available}<Unavailable />{:else}<section class="public-section" id="groups"><div class="section-heading"><h2>ΗΛΙΚΙΑΚΑ ΤΜΗΜΑΤΑ</h2></div><div class="public-group-grid">{#each data.site?.academy_groups ?? [] as group (group.id)}<article id={`group-${encodeURIComponent(group.id)}`} class="public-group-card">{#if group.banner_url}<PublicImage src={group.banner_url} alt="" />{/if}<div><p class="public-eyebrow">{group.age_range || group.season || 'ΑΚΑΔΗΜΙΑ'}</p><h3><a href={academyGroupLink(group)}>{group.name} <span aria-hidden="true">↗</span></a></h3>{#if group.description}<p>{group.description}</p>{/if}{#if group.city || group.location_name}<p class="group-location">{[group.city, group.location_name].filter(Boolean).join(' · ')}</p>{/if}{#if group.training_schedule}<h4>ΠΡΟΓΡΑΜΜΑ ΠΡΟΠΟΝΗΣΕΩΝ</h4><p class="preserve-lines">{group.training_schedule}</p>{/if}<a class="group-open" href={academyGroupLink(group)}>ΓΝΩΡΙΣΕ ΤΟ ΤΜΗΜΑ <span aria-hidden="true">↗</span></a></div></article>{:else}<p class="public-empty">Δεν έχουν καταχωριστεί ηλικιακά τμήματα.</p>{/each}</div></section><section class="public-section section-dark" id="academy-fixtures"><div class="section-heading"><h2>ΑΓΩΝΕΣ ΑΚΑΔΗΜΙΑΣ</h2></div>{#if data.site?.fixtures.length}<Fixtures fixtures={data.site.fixtures} />{:else}<p class="public-empty">Δεν έχουν καταχωριστεί αγώνες ακαδημίας.</p>{/if}</section>{/if}
</PublicShell>

<style>
  .public-group-card h3 a { color:inherit; text-decoration:none; }
  .public-group-card h3 a:hover { color:var(--public-gold); }
  .group-open { display:flex; gap:20px; align-items:center; justify-content:space-between; margin-top:24px; padding-top:16px; border-top:1px solid #ffffff24; color:var(--public-gold); font-weight:700; text-decoration:none; }
  .group-open:focus-visible,.public-group-card h3 a:focus-visible { outline:2px solid var(--public-gold); outline-offset:5px; }
</style>
