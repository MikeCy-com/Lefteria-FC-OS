<script lang="ts">
  import PublicShell from '../../../lib/public/PublicShell.svelte';
  import PublicPersonProfile from '../../../lib/public/PublicPersonProfile.svelte';
  import type { StaffPageData } from '../../../lib/public/sections-types';
  let { data }: { data: StaffPageData } = $props();
</script>
<svelte:head><title>{data.staff.name} | ΛΕΥΤΕΡΙΑ FC</title><meta name="description" content={`Γνώρισε τον ${data.staff.name}, ${data.staff.role}, Λευτεριά FC.`} /></svelte:head>
<PublicShell site={data.site} branding={data.branding} organization="lefteria_fc" navigationGroups={data.navigationGroups}><PublicPersonProfile staff={data.staff} /></PublicShell>
