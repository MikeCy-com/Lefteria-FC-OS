<script lang="ts">
  import type { OrganizationOption, UiAction } from './component-types.ts';
  import MobileIcon from './MobileIcon.svelte';
  import './mobile-theme.css';

  interface Props {
    organizations: readonly OrganizationOption[];
    selectedId: string | null;
    requiresSelection?: boolean;
    busy?: boolean;
    error?: string | null;
    onSelect: (organizationId: string) => UiAction;
  }
  let { organizations, selectedId, requiresSelection = false, busy = false, error = null, onSelect }: Props = $props();
  const id = $props.id();
</script>

<section class="lf-mobile-theme organization-switcher" aria-labelledby={`${id}-title`} aria-busy={busy} data-testid="organization-switcher">
  <h2 id={`${id}-title`}>{requiresSelection ? 'Επίλεξε οργανισμό' : 'Οργανισμός'}</h2>
  <p class="description">Οι πληροφορίες εμφανίζονται μόνο για τον οργανισμό που επιλέγεις.</p>
  <div class="organization-options" class:single={organizations.length === 1} role="group" aria-label="Επιλογή οργανισμού">
    {#each organizations as organization (organization.id)}
      <button type="button" disabled={busy || selectedId === organization.id} aria-pressed={selectedId === organization.id}
        onclick={() => void onSelect(organization.id)} data-testid={`organization-${organization.id}`}>
        <span>{organization.name}</span>{#if selectedId === organization.id}<MobileIcon name="check" size={18} />{/if}
      </button>
    {/each}
  </div>
  {#if busy}<p class="status" role="status">Ενημέρωση οργανισμού…</p>{/if}
  {#if error}<p class="error" role="alert">{error}</p>{/if}
</section>

<style>
  .organization-switcher { padding:16px; border:1px solid var(--lf-line); border-radius:8px; background:linear-gradient(130deg,#171a1b,#0d0f10); }
  h2 { margin:0; font-size:21px; line-height:1.2; }
  .description { margin:6px 0 15px; color:var(--lf-muted); font-size:12px; line-height:1.5; }
  .organization-options { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:8px; }
  .organization-options.single { grid-template-columns:1fr; }
  button { display:flex; align-items:center; justify-content:center; gap:8px; min-height:47px; padding:10px 12px; border:1px solid #343738; border-radius:6px; background:#0e1011; color:#d4d8da; cursor:pointer; }
  button:hover:not(:disabled) { border-color:var(--lf-gold); color:#fff; }
  button[aria-pressed='true'] { border-color:var(--lf-gold); background:#f5a62315; color:var(--lf-gold-light); opacity:1; cursor:default; }
  button span { overflow-wrap:anywhere; font-family:var(--lf-heading); font-size:17px; line-height:1.15; }
  .status { margin:12px 0 0; color:var(--lf-muted); font-size:12px; }
  .error { margin:12px 0 0; color:#fca5a5; font-size:13px; overflow-wrap:anywhere; }
</style>
