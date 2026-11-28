<script lang="ts">
  import '../lib/public/fonts';
  import './app.css';
  import { onMount } from 'svelte';
  import { page } from '$app/state';
  const posTerminal=$derived(page.url.pathname==='/admin/pos/terminal'||page.url.pathname==='/admin/pos/terminal/');
  import { appInstall } from '../lib/mobile/app-install';
  onMount(() => appInstall.start(window));
  import '../lib/accessibility.css';
  let { children } = $props();
</script>

<svelte:head>
  <link rel="manifest" href={posTerminal?'/pos.webmanifest':'/manifest.webmanifest'}/>
  <meta name="apple-mobile-web-app-title" content={posTerminal?'Lefteria POS':'Lefteria FC OS'}/>
</svelte:head>

{@render children()}
