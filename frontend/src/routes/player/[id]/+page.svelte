<script lang="ts">
  import PublicShell from '../../../lib/public/PublicShell.svelte';
  import PublicPersonProfile from '../../../lib/public/PublicPersonProfile.svelte';
  import type { PlayerPageData } from '../../../lib/public/sections-types';
  let { data }: { data: PlayerPageData } = $props();
</script>
<svelte:head><title>{data.player.name} | ΛΕΥΤΕΡΙΑ FC</title><meta name="description" content={`Το προφίλ και τα καταγεγραμμένα στατιστικά του ${data.player.name}, Λευτεριά FC.`} /></svelte:head>
<PublicShell site={data.site} branding={data.branding} organization="lefteria_fc" navigationGroups={data.navigationGroups}><PublicPersonProfile player={data.player} gallery={data.gallery} season={data.season} /></PublicShell>
