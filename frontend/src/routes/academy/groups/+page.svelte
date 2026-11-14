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

