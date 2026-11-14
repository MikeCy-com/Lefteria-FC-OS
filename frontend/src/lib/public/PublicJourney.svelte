<script lang="ts">
  import PublicShell from './PublicShell.svelte';
  import type { PublicPageData, PublicOrganization } from './types';

  let { data, eyebrow, title, description, status, primaryHref, primaryLabel,
    secondaryHref = '/contact', secondaryLabel = 'ΕΠΙΚΟΙΝΩΝΙΑ', organization = 'lefteria_fc', partnerReferral = null }: {
      data: PublicPageData;
      eyebrow: string;
      title: string;
      description: string;
      status: string;
      primaryHref: string;
      primaryLabel: string;
      secondaryHref?: string;
      secondaryLabel?: string;
      organization?: PublicOrganization;
      partnerReferral?: string | null;
    } = $props();
</script>

<PublicShell site={data.site} branding={data.branding} {organization} navigationGroups={data.navigationGroups}>
  <div class="source-journey" data-testid="public-journey-page">
    <section aria-labelledby="journey-title" class="journey-content">
      <p class="journey-eyebrow">{eyebrow}</p>
      <h1 id="journey-title">{title}</h1>
      <p class="journey-description">{description}</p>
      {#if partnerReferral}
        <p class="journey-referral" data-testid="partner-referral-badge">
          <span aria-hidden="true">↗</span> Παραπομπή μέσω συνεργάτη: <strong>{partnerReferral}</strong>
        </p>
      {/if}
      <div class="journey-notice" role="status" data-testid="journey-capability-status">
        <strong>ΕΝΗΜΕΡΩΣΗ ΥΠΗΡΕΣΙΑΣ</strong>
        <p>{status}</p>
      </div>
      <div class="journey-actions">
        <a class="journey-primary" href={primaryHref} data-testid="journey-primary">{primaryLabel}<span aria-hidden="true">→</span></a>
        <a class="journey-secondary" href={secondaryHref} data-testid="journey-secondary">{secondaryLabel}<span aria-hidden="true">↗</span></a>
      </div>
    </section>
  </div>
</PublicShell>

