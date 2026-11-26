<script lang="ts">
  import PublicImage from './PublicImage.svelte';
  import { catalogHref } from './PublicCatalog';
  import type { PublicSections } from './sections-types';
  import type { PublicOrganization } from './types';

  let { sponsors = [], organization }: {
    sponsors?: PublicSections['sponsors'];
    organization: PublicOrganization;
  } = $props();

  // Never reuse a Club sponsor just because it shares a name or logo with
  // an Academy sponsor. The server-projected organization is authoritative.
  const matching = $derived(sponsors.filter(item => item.organization_id === organization)
    .sort((a,b) => Number(a.display_order)-Number(b.display_order)||a.name.localeCompare(b.name)));
  const owner = $derived(organization === 'lefteria_fc_academy' ? 'ΑΚΑΔΗΜΙΑΣ' : 'ΠΡΩΤΗΣ ΟΜΑΔΑΣ');
  const directory = $derived(organization === 'lefteria_fc_academy' ? '/sponsors/academy' : '/sponsors/first-team');
</script>

{#if matching.length}
  <section class="organization-sponsor-strip" aria-label={`Χορηγοί ${owner}`}
    data-testid="organization-sponsor-strip" data-organization={organization}>
    <div class="organization-sponsor-heading">
      <div><p>ΟΙ ΧΟΡΗΓΟΙ ΜΑΣ</p><h2>ΧΟΡΗΓΟΙ {owner}</h2></div>
      <a href={directory} data-testid="organization-sponsor-directory">ΟΛΟΙ ΟΙ ΧΟΡΗΓΟΙ <span aria-hidden="true">→</span></a>
    </div>
    <div class="organization-sponsor-grid">
      {#each matching as sponsor (`${sponsor.organization_id}:${sponsor.id}`)}
        <a class="organization-sponsor" href={catalogHref('sponsors',sponsor,sponsor.name) + '&list_scope=' + (organization === 'lefteria_fc_academy' ? 'academy' : 'first_team')} aria-label={`Χορηγοί ${owner}: ${sponsor.name}`}
          data-testid="organization-sponsor-logo">
          {#if sponsor.logo_url}
            <PublicImage src={sponsor.logo_url} alt={sponsor.name}/>
          {:else}
            <strong>{sponsor.name}</strong>
          {/if}
        </a>
      {/each}
    </div>
  </section>
{/if}

<style>
  .organization-sponsor-strip {padding:30px clamp(16px,3vw,44px) 40px;background:var(--lf-color-bg,#0b0c0c);border-top:1px solid var(--lf-color-accent,#69501d);}
  .organization-sponsor-heading {display:flex;align-items:end;justify-content:space-between;flex-wrap:wrap;gap:16px;margin-bottom:20px;}
  .organization-sponsor-heading p {font-size:12px;color:var(--lf-color-accent,#ffbf32);font-weight:700;margin:0 0 3px;}
  .organization-sponsor-heading h2 {font-family:'Fira Sans Extra Condensed',sans-serif;font-size:clamp(25px,2.2vw,34px);font-style:italic;line-height:1.1;margin:0;}
  .organization-sponsor-heading>a {color:var(--lf-color-accent,#ffbf32);font-size:13px;font-weight:700;display:inline-flex;align-items:center;gap:12px;min-height:44px;}
  .organization-sponsor-grid {display:grid;grid-template-columns:repeat(auto-fit,minmax(min(150px,100%),1fr));gap:12px;}
  .organization-sponsor {min-width:0;min-height:105px;display:grid;place-items:center;padding:13px;border:1px solid var(--lf-color-border,#484741);border-radius:5px;background:linear-gradient(140deg,var(--lf-color-surface,#181a1a),var(--lf-color-bg,#080909));}
  .organization-sponsor:hover,.organization-sponsor:focus-visible {border-color:var(--lf-color-accent,#ffbf32);box-shadow:inset 0 -2px var(--lf-color-accent,#ffbf32);}
  .organization-sponsor :global(img) {max-width:100%;max-height:75px;width:auto;height:auto;object-fit:contain;}
  .organization-sponsor strong {color:var(--lf-color-text,#eeeeee);font-weight:800;font-size:16px;text-align:center;overflow-wrap:anywhere;}
  @media(max-width:600px) {
    .organization-sponsor-strip {padding:20px 16px 30px;}
    .organization-sponsor-grid {grid-template-columns:repeat(2,minmax(0,1fr));}
  }
</style>
