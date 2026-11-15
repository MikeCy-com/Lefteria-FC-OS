<script lang="ts">
  import PublicShell from '../../../lib/public/PublicShell.svelte';
  import PublicPersonProfile from '../../../lib/public/PublicPersonProfile.svelte';
  import type { PlayerPageData } from '../../../lib/public/sections-types';
  import { publicCanonicalHref, personPublicLink, publicSocialImage } from '../../../lib/public/public-urls';
  let { data }: { data: PlayerPageData } = $props();
  const indexable = $derived(data.player.organization_id === 'lefteria_fc' &&
    data.player.team_type === 'First Team' && data.player.age !== null &&
    Number.isFinite(Number(data.player.age)) && Number(data.player.age) >= 18);
  const canonical = $derived(indexable ? publicCanonicalHref(personPublicLink('player',data.player)) : null);
  const socialImage = $derived(indexable ? publicSocialImage(data.player.image_url) : null);
</script>
<svelte:head>
  <title>{data.player.name} | ΛΕΥΤΕΡΙΑ FC</title>
  <meta name="description" content={indexable
    ? `Το επίσημο προφίλ και τα δημοσιευμένα στατιστικά του ${data.player.name}, Λευτεριά FC.`
    : 'Δημοσιευμένο προφίλ της Ακαδημίας Λευτεριά FC.'} />
  {#if !indexable}<meta name="robots" content="noindex,follow" />{/if}
  {#if indexable && canonical}
    <meta property="og:type" content="profile" />
    <meta property="og:title" content={data.player.name+' | ΛΕΥΤΕΡΙΑ FC'} />
    <meta property="og:description" content={`Επίσημο προφίλ ποδοσφαιριστή: ${data.player.name}.`} />
    <meta property="og:url" content={canonical} />
    <meta name="twitter:card" content={socialImage ? 'summary_large_image' : 'summary'} />
    {#if socialImage}<meta property="og:image" content={socialImage} />{/if}
  {/if}
</svelte:head>
<PublicShell site={data.site} branding={data.branding} organization={data.organization} navigationGroups={data.navigationGroups}><PublicPersonProfile player={data.player} gallery={data.gallery} season={data.season} returnTeamId={data.returnTeamId} /></PublicShell>
