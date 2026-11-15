<script lang="ts">
  import PublicShell from '../../../lib/public/PublicShell.svelte';
  import PublicPersonProfile from '../../../lib/public/PublicPersonProfile.svelte';
  import type { PlayerPageData } from '../../../lib/public/sections-types';
  let { data }: { data: PlayerPageData } = $props();
</script>
<svelte:head><title>{data.player.name} | ΛΕΥΤΕΡΙΑ FC</title><meta name="description" content={`Το προφίλ και τα καταγεγραμμένα στατιστικά του ${data.player.name}, Λευτεριά FC.`} />{#if data.player.organization_id !== 'lefteria_fc' || data.player.team_type !== 'First Team' || data.player.age === null || !Number.isFinite(Number(data.player.age)) || Number(data.player.age) < 18}<meta name="robots" content="noindex,follow" />{/if}</svelte:head>
<PublicShell site={data.site} branding={data.branding} organization={data.organization} navigationGroups={data.navigationGroups}><PublicPersonProfile player={data.player} gallery={data.gallery} season={data.season} returnTeamId={data.returnTeamId} /></PublicShell>
