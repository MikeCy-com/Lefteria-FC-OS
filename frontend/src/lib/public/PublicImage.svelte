<script lang="ts">
  let { src, alt = '', class: className = '', eager = false }: { src?: string | null; alt?: string; class?: string; eager?: boolean } = $props();
  let failed = $state(false);
  $effect(() => { void src; failed = false; });
</script>
<img src={failed || !src ? '/club/logo512.png' : src} {alt} class={className} data-fallback={failed || !src} loading={eager ? 'eager' : 'lazy'} onerror={() => { failed = true; }} />
