<script lang="ts">
  import { onDestroy, tick } from 'svelte';
  import { createApiClient } from '../api/client';
  import { publicNavigation, publicSearch, type PublicNavigationItem } from './navigation';
  import type { PublicArticle, PublicGroup, PublicOrganization } from './types';
  import Icon from './PublicIcon.svelte';
  import { lockPageScroll } from './dialog-scroll';
  let { groups = [], organization = 'lefteria_fc', navigation = publicNavigation }: { groups?: PublicGroup[]; organization?: PublicOrganization; navigation?: readonly PublicNavigationItem[] } = $props();
  let dialog: HTMLDialogElement; let input: HTMLInputElement;
  let query = $state(''); let news = $state<PublicArticle[]>([]); let loading = $state(false); let failed = $state(false);
  let revision = 0;
  let request: AbortController | null = null;
  let unlockScroll: (() => void) | null = null;
  const results = $derived(publicSearch(query, groups, news, { organization, navigation }));
  async function open() {
    if (!dialog.open) { dialog.showModal(); unlockScroll = lockPageScroll(document); }
    await tick(); input.focus();
    request?.abort();
    const controller = new AbortController(); request = controller;
    const current = ++revision; const sourceOrganization = organization;
    loading = true; failed = false;
    try {
      const response = await createApiClient({ baseUrl: window.location.origin }).GET('/api/public/news',
        { credentials: 'omit', params: { query: { limit: 100, organization_id: sourceOrganization } },
          signal: AbortSignal.any([controller.signal, AbortSignal.timeout(10000)]) });
      if (current !== revision || sourceOrganization !== organization) return;
      if (!response.response.ok || !response.data) throw new Error('Search unavailable.');
      news = response.data.items.filter(article => article.organization_id === sourceOrganization);
    } catch { if (current === revision) { news = []; failed = true; } }
    finally { if (current === revision) { loading = false; request = null; } }
  }
  function releaseScroll() { unlockScroll?.(); unlockScroll = null; }
  function close() { ++revision; request?.abort(); request = null; loading = false; dialog?.close(); releaseScroll(); }
  function backdrop(event: MouseEvent) {
    if (event.target !== dialog) return;
    const box = dialog.getBoundingClientRect();
    if (event.clientX < box.left || event.clientX > box.right || event.clientY < box.top || event.clientY > box.bottom) close();
  }
  onDestroy(() => { ++revision; request?.abort(); releaseScroll(); });
</script>
<button class="public-search-trigger" type="button" onclick={() => void open()} aria-label="Αναζήτηση" aria-haspopup="dialog" data-testid="public-search-open"><Icon name="search" size={19} /><span>Αναζήτηση…</span></button>
<dialog bind:this={dialog} class="public-dialog public-search-dialog" aria-labelledby="public-search-title" aria-describedby="public-search-description" data-testid="public-search-dialog" onclose={() => { ++revision; request?.abort(); request = null; loading = false; releaseScroll(); }} onclick={backdrop}>
  <h2 id="public-search-title">ΑΝΑΖΗΤΗΣΗ</h2>
  <p id="public-search-description" data-testid="public-search-context">{organization === 'lefteria_fc_academy' ? 'Ακαδημία · Τμήματα, νέα και σελίδες της Ακαδημίας' : 'Πρώτη ομάδα · Ρόστερ, αγώνες, νέα και σελίδες του συλλόγου'}</p>
  <label class="sr-only" for="public-search">Αναζήτηση</label><input bind:this={input} id="public-search" bind:value={query} placeholder="Τι αναζητάς;"
    aria-controls="public-search-results" aria-describedby="public-search-description" autocomplete="off"
    data-testid="public-search-input" />
  {#if loading}<p role="status" data-testid="public-search-loading">Φόρτωση νέων…</p>{/if}
  {#if failed}<p role="alert" data-testid="public-search-error">Τα νέα δεν είναι προσωρινά διαθέσιμα στην αναζήτηση.</p>{/if}
  <p class="public-search-count" role="status" aria-live="polite" data-testid="public-search-count">{loading ? 'Αναζήτηση δημοσιευμένων νέων…' : `${results.length} ${results.length === 1 ? 'αποτέλεσμα' : 'αποτελέσματα'}`}</p>
  <nav id="public-search-results" class="public-search-results" aria-label="Αποτελέσματα αναζήτησης" data-testid="public-search-results">{#each results as item (item.path)}<a href={item.path} onclick={close} data-testid={`public-search-result-${item.id}`}>{item.label}<Icon name="external" size={17} /></a>{/each}</nav>
  {#if !loading && !results.length}<p data-testid="public-search-empty">Δεν βρέθηκαν αποτελέσματα.</p>{/if}
  <button type="button" class="public-dialog-close" aria-label="Κλείσιμο αναζήτησης" onclick={close} data-testid="public-search-close"><Icon name="close" size={16} /></button>
</dialog>
