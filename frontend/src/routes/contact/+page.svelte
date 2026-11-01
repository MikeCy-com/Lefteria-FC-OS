<script lang="ts">
  import PublicShell from '../../lib/public/PublicShell.svelte';
  import Unavailable from '../../lib/public/Unavailable.svelte';
  import type { PublicPageData } from '../../lib/public/types';
  let { data }: { data: PublicPageData } = $props();
  const club = $derived(data.site?.club);
</script>
<svelte:head><title>ΕΠΙΚΟΙΝΩΝΙΑ | ΛΕΥΤΕΡΙΑ FC</title><meta name="description" content="Επικοινωνήστε με τη Λευτεριά FC." /></svelte:head>
<PublicShell site={data.site} branding={data.branding} organization={data.organization} navigationGroups={data.navigationGroups}><section class="public-page-heading"><p class="public-eyebrow">ΕΙΜΑΣΤΕ ΕΔΩ ΓΙΑ ΕΣΕΝΑ</p><h1>ΕΠΙΚΟΙΝΩΝΙΑ</h1><nav class="public-subnav" aria-label="Στοιχεία οργανισμού"><a href="/contact?organization_id=lefteria_fc" aria-current={data.organization === 'lefteria_fc' ? 'page' : undefined}>ΛΕΥΤΕΡΙΑ FC</a><a href="/contact?organization_id=lefteria_fc_academy" aria-current={data.organization === 'lefteria_fc_academy' ? 'page' : undefined}>ΑΚΑΔΗΜΙΑ</a></nav></section>{#if !data.available}<Unavailable />{:else}<section class="public-section public-contact">{#if club?.address}<article><p class="public-eyebrow">ΔΙΕΥΘΥΝΣΗ</p><h2>{club.address}</h2><p>{[club.city, club.country].filter(Boolean).join(' · ')}</p></article>{/if}{#if club?.phone}<article><p class="public-eyebrow">ΤΗΛΕΦΩΝΟ</p><h2><a href={`tel:${club.phone}`}>{club.phone}</a></h2></article>{/if}{#if club?.email}<article><p class="public-eyebrow">EMAIL</p><h2><a href={`mailto:${club.email}`}>{club.email}</a></h2></article>{/if}{#if !club?.address && !club?.phone && !club?.email}<p class="public-empty">Δεν έχουν καταχωριστεί στοιχεία επικοινωνίας.</p>{/if}</section>{/if}</PublicShell>
