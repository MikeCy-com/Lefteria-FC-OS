<script lang="ts">
  import PublicImage from './PublicImage.svelte';
  import { academyGroupLink } from './navigation';
  import type { PublicGroup } from './types';
  import type { SectionStaff } from './sections-types';

  let { group, coaches = [] }: { group: PublicGroup; coaches?: SectionStaff[] } = $props();
</script>

<a class="academy-card" href={academyGroupLink(group)} data-testid="academy-group-card">
  <div class="card-visual">
    {#if group.banner_url}
      <PublicImage src={group.banner_url} alt="" />
    {:else}
      <span class="group-monogram" aria-hidden="true">{group.name}</span>
    {/if}
    <span class="card-arrow" aria-hidden="true">↗</span>
  </div>
  <div class="card-content">
    <p class="public-eyebrow">{group.age_range || 'ΑΚΑΔΗΜΙΑ'}{group.season ? ` · ${group.season}` : ''}</p>
    <h3>{group.name}</h3>
    {#if group.description}<p class="card-description">{group.description}</p>{/if}
    {#if group.city || group.location_name}
      <p class="card-location">{[group.city, group.location_name].filter(Boolean).join(' · ')}</p>
    {/if}
    {#if coaches.length}
      <p class="card-coaches" data-testid="academy-public-coach">ΠΡΟΠΟΝΗΤΗΣ: {coaches.map(coach => coach.name).join(' · ')}</p>
    {/if}
    {#if group.training_schedule}<p class="card-training">{group.training_schedule}</p>{/if}
    <span class="card-action">ΓΝΩΡΙΣΕ ΤΟ ΤΜΗΜΑ <span aria-hidden="true">→</span></span>
  </div>
</a>

<style>
  .card-coaches,.card-training{font-size:13px;line-height:1.55;color:var(--lf-color-muted,#cacaca);margin:16px 0;overflow-wrap:anywhere}
  .card-coaches{color:var(--lf-color-accent,#e7c47f);font-weight:700}
  .card-training{border-left:2px solid var(--lf-color-accent,#e7b644);padding-left:11px;white-space:pre-line}
</style>
