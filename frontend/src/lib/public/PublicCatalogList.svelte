<script lang="ts">
  import PublicShell from './PublicShell.svelte';
  import PublicImage from './PublicImage.svelte';
  import { greekDate } from './content';
  import { catalogTitles, catalogScopeLabel } from './PublicCatalog';
  import type { CatalogData } from '../server/public-catalog';
  import './PublicCatalog.css';
  let { data }: { data: CatalogData } = $props();
  const descriptions = { events: 'Οι στιγμές που φέρνουν την οικογένεια της Λευτεριάς πιο κοντά.', partners: 'Οι άνθρωποι και οι οργανισμοί που μοιράζονται το όραμά μας.', sponsors: 'Δίπλα στην ομάδα. Μέρος της διαδρομής μας.', seasons: 'Οι αγώνες, οι ομάδες και οι στιγμές κάθε σεζόν.' };
  const title = $derived(catalogTitles[data.kind]);
</script>
<svelte:head><title>{title} | ΛΕΥΤΕΡΙΑ FC</title><meta name="description" content={descriptions[data.kind]} /></svelte:head>
<PublicShell site={data.site} branding={data.branding} organization={data.organization} navigationGroups={data.navigationGroups}>
  <section class="catalog" data-testid={`public-${data.kind}-catalog`}>
    <nav class="catalog-breadcrumb" aria-label="Διαδρομή σελίδας"><a href="/">Αρχική</a><span aria-hidden="true">/</span><span aria-current="page">{title}</span></nav>
    <header class="catalog-heading"><p class="public-eyebrow">Η ΟΙΚΟΓΕΝΕΙΑ ΤΗΣ ΛΕΥΤΕΡΙΑΣ</p><h1>{title}</h1><p>{descriptions[data.kind]}</p></header>
    <form method="GET" action={`/${data.kind}`} class="catalog-filters" aria-label="Φίλτρα ενότητας">
      <label>Ενότητα<select name="scope" value={data.scope ?? 'all'}><option value="all">Όλος ο σύλλογος</option><option value="first_team">Πρώτη ομάδα</option><option value="academy">Ακαδημία</option></select></label>
      {#if data.kind === 'events'}<label>Πότε<select name="when" value={data.when}><option value="upcoming">Προσεχείς</option><option value="past">Παλαιότερες</option><option value="all">Όλες οι εκδηλώσεις</option></select></label><label>Τύπος<select name="type" value={data.eventType}><option value="all">Όλοι οι τύποι</option>{#each data.eventTypes as eventType}<option value={eventType}>{eventType}</option>{/each}</select></label>{/if}
      <button class="public-button" type="submit">ΠΡΟΒΟΛΗ ↗</button>
    </form>
    <div class="catalog-results-heading"><p>{data.scope === 'academy' ? 'ΑΚΑΔΗΜΙΑ' : data.scope === 'first_team' ? 'ΠΡΩΤΗ ΟΜΑΔΑ' : 'ΟΛΟΣ Ο ΣΥΛΛΟΓΟΣ'}</p><span>{data.cards.length} {data.cards.length === 1 ? 'καταχώριση' : 'καταχωρίσεις'}</span></div>
    {#if data.cards.length}<div class="catalog-grid">{#each data.cards as item (`${item.organization}:${item.id}`)}<a class="catalog-card" class:catalog-sponsor={data.kind === 'sponsors'} class:catalog-season={data.kind === 'seasons'} href={item.href}>
      <div class="catalog-card-image">{#if item.image}<PublicImage src={item.image} alt="" />{:else}<span>{data.kind === 'seasons' ? item.title : 'ΛΕΥΤΕΡΙΑ FC'}</span>{/if}<span class="catalog-card-scope">{item.scopeLabel || catalogScopeLabel(item.organization)}</span></div>
      <div class="catalog-card-body"><p class="public-eyebrow">{item.label}{item.meta ? ` · ${greekDate(item.meta)}` : ''}</p><h2>{item.title}</h2>{#if item.summary}<p class="catalog-card-summary">{item.summary}</p>{/if}<span class="catalog-card-action">{data.kind === 'seasons' ? 'ΑΝΑΚΑΛΥΨΕ ΤΗ ΣΕΖΟΝ' : 'ΠΕΡΙΣΣΟΤΕΡΑ'} <span aria-hidden="true">↗</span></span></div>
    </a>{/each}</div>{:else}<div class="catalog-empty"><h2>ΔΕΝ ΥΠΑΡΧΟΥΝ ΚΑΤΑΧΩΡΙΣΕΙΣ</h2><p>{data.kind === 'events' ? 'Δεν έχουν δημοσιευτεί εκδηλώσεις για τα επιλεγμένα φίλτρα.' : 'Δεν έχουν δημοσιευτεί καταχωρίσεις σε αυτή την ενότητα.'}</p>{#if data.scope || data.kind === 'events'}<a href={`/${data.kind}${data.kind === 'events' ? '?when=all' : ''}`}>Προβολή όλων ↗</a>{/if}</div>{/if}
  </section>
</PublicShell>
