<script lang="ts">
  import './app.css';
  import { onMount } from 'svelte';
  import { appInstall } from '../lib/mobile/app-install';
  onMount(() => appInstall.start(window));
  import '../lib/accessibility.css';
  let { children } = $props();
</script>

{@render children()}
