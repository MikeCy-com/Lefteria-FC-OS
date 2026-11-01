<script lang="ts">
  let { name, logo, clubName = '', clubLogo }: { name: string; logo?: string | null; clubName?: string; clubLogo?: string | null } = $props();
  let failed = $state(false);
  const normalize = (value: string) => value.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLocaleLowerCase('el-GR');
  const ours = $derived(normalize(name).includes('lefteria') || normalize(name).includes('λευτερια') || !!clubName && normalize(name) === normalize(clubName));
  const src = $derived(logo || (ours ? clubLogo || '/club/logo512.png' : null));
  $effect(() => { void src; failed = false; });
</script>
<div class="public-team-crest" class:public-team-initials={!src || failed}>
  {#if src && !failed}<img {src} alt="" loading="lazy" onerror={() => { failed = true; }} />{:else}<span>{(name || '?').slice(0,3).toLocaleUpperCase('el-GR')}</span>{/if}
</div>
