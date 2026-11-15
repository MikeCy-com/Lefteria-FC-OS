<script lang="ts">
  import PublicShell from '../../../lib/public/PublicShell.svelte';
  import PublicPersonProfile from '../../../lib/public/PublicPersonProfile.svelte';
  import type { StaffPageData } from '../../../lib/public/sections-types';
  import { personPublicLink, publicCanonicalHref, publicSocialImage } from '../../../lib/public/public-urls';
  let { data }: { data: StaffPageData } = $props();
  const canonical = $derived(publicCanonicalHref(personPublicLink('staff',data.staff)));
  const socialImage = $derived(publicSocialImage(data.staff.image_url));
</script>
<svelte:head>
  <title>{data.staff.name} | ΛΕΥΤΕΡΙΑ FC</title>
  <meta name="description" content={`Γνώρισε τον ${data.staff.name}, ${data.staff.role}, Λευτεριά FC.`} />
  <meta property="og:type" content="profile" />
  <meta property="og:title" content={data.staff.name+' | ΛΕΥΤΕΡΙΑ FC'} />
  <meta property="og:description" content={data.staff.role+' · Λευτεριά FC'} />
  <meta property="og:url" content={canonical} />
  <meta name="twitter:card" content={socialImage ? 'summary_large_image' : 'summary'} />
  {#if socialImage}<meta property="og:image" content={socialImage} />{/if}
</svelte:head>
<PublicShell site={data.site} branding={data.branding} organization="lefteria_fc" navigationGroups={data.navigationGroups}><PublicPersonProfile staff={data.staff} returnTeamId={data.returnTeamId} /></PublicShell>
