<script lang="ts">
  import PublicShell from '../../lib/public/PublicShell.svelte';
  import PublicImage from '../../lib/public/PublicImage.svelte';
  import MarkdownContent from '../../lib/public/MarkdownContent.svelte';
  import Unavailable from '../../lib/public/Unavailable.svelte';
  import type { PublicPageData } from '../../lib/public/types';
  let { data }: { data: PublicPageData } = $props();
</script>
<svelte:head><title>Ο ΣΥΛΛΟΓΟΣ | ΛΕΥΤΕΡΙΑ FC</title><meta name="description" content={data.site?.club?.description || 'Γνώρισε τη Λευτεριά FC.'} /></svelte:head>
<PublicShell site={data.site} organization={data.organization} navigationGroups={data.navigationGroups}><section class="public-page-heading"><p class="public-eyebrow">Η ΙΣΤΟΡΙΑ ΜΑΣ</p><h1>Ο ΣΥΛΛΟΓΟΣ</h1></section>{#if !data.available}<Unavailable />{:else if data.site?.club}<section class="public-section public-club-story"><PublicImage src={data.site.club.logo_url} alt="Το έμβλημα της Λευτεριάς FC" /><div><p class="public-eyebrow">{data.site.club.founded ? `ΙΔΡ. ${data.site.club.founded}` : 'ΛΕΥΤΕΡΙΑ FC'}</p><h2>{data.site.club.greek_name || data.site.club.name}</h2>{#if data.site.club.motto}<p class="club-motto">{data.site.club.motto}</p>{/if}{#if data.site.club.description}<p class="preserve-lines">{data.site.club.description}</p>{/if}{#if data.site.club.history}<MarkdownContent content={data.site.club.history} />{/if}</div></section>{:else}<section class="public-section"><p class="public-empty">Δεν έχουν καταχωριστεί πληροφορίες συλλόγου.</p></section>{/if}</PublicShell>
