<script lang="ts">
  import { onDestroy, tick } from 'svelte';
  import { createApiClient } from '../api/client';
  import { publicSearch } from './navigation';
  import type { PublicArticle, PublicGroup } from './types';
  import Icon from './PublicIcon.svelte';
  import { lockPageScroll } from './dialog-scroll';
  let { groups = [] }: { groups?: PublicGroup[] } = $props();
  let dialog: HTMLDialogElement; let input: HTMLInputElement;
  let query = $state(''); let news = $state<PublicArticle[]>([]); let loading = $state(false); let failed = $state(false);
  let revision = 0;
  let unlockScroll: (() => void) | null = null;
  const results = $derived(publicSearch(query, groups, news));
  async function open() {
    if (!dialog.open) { dialog.showModal(); unlockScroll = lockPageScroll(document); }
    await tick(); input.focus();
    const current = ++revision; loading = true; failed = false;
    try {
      const response = await createApiClient({ baseUrl: window.location.origin }).GET('/api/public/news',
        { credentials: 'omit', params: { query: { limit: 100 } }, signal: AbortSignal.timeout(10000) });
      if (current !== revision) return;
      if (!response.response.ok || !response.data) throw new Error('Search unavailable.');
      news = response.data.items;
    } catch { if (current === revision) { news = []; failed = true; } }
    finally { if (current === revision) loading = false; }
  }
  function releaseScroll() { unlockScroll?.(); unlockScroll = null; }
  function close() { ++revision; loading = false; dialog?.close(); releaseScroll(); }
  function backdrop(event: MouseEvent) {
    if (event.target !== dialog) return;
    const box = dialog.getBoundingClientRect();
    if (event.clientX < box.left || event.clientX > box.right || event.clientY < box.top || event.clientY > box.bottom) close();
  }
  onDestroy(() => { ++revision; releaseScroll(); });
</script>
<button class="public-search-trigger" type="button" onclick={() => void open()} aria-label="Αναζήτηση" aria-haspopup="dialog" data-testid="public-search-open"><Icon name="search" size={19} /><span>Αναζήτηση…</span></button>
<dialog bind:this={dialog} class="public-dialog public-search-dialog" aria-labelledby="public-search-title" aria-describedby="public-search-description" data-testid="public-search-dialog" onclose={() => { ++revision; loading = false; releaseScroll(); }} onclick={backdrop}>
  <h2 id="public-search-title">ΑΝΑΖΗΤΗΣΗ</h2><p id="public-search-description">Νέα, ομάδες και σελίδες του συλλόγου</p>
  <label class="sr-only" for="public-search">Αναζήτηση</label><input bind:this={input} id="public-search" bind:value={query} placeholder="Τι αναζητάς;" data-testid="public-search-input" />
  {#if loading}<p role="status" data-testid="public-search-loading">Φόρτωση νέων…</p>{/if}
  {#if failed}<p role="alert" data-testid="public-search-error">Τα νέα δεν είναι προσωρινά διαθέσιμα στην αναζήτηση.</p>{/if}
  <div class="public-search-results" data-testid="public-search-results">{#each results as item (item.path)}<a href={item.path} onclick={close} data-testid={`public-search-result-${item.id}`}>{item.label}<Icon name="external" size={17} /></a>{/each}</div>
  {#if !loading && !results.length}<p data-testid="public-search-empty">Δεν βρέθηκαν αποτελέσματα.</p>{/if}
  <button type="button" class="public-dialog-close" aria-label="Κλείσιμο αναζήτησης" onclick={close} data-testid="public-search-close"><Icon name="close" size={16} /></button>
</dialog>
