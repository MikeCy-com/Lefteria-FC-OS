<script lang="ts">
  import PublicShell from './PublicShell.svelte';
  import PublicImage from './PublicImage.svelte';
  import WebsitePageBlocks from './WebsitePageBlocks.svelte';
  import type {WebsiteBlock} from './website-pages';
  import { greekDate } from './content';
  import { catalogTitles, catalogScopeLabel, catalogListHref, eventTypeLabel, eventDateTime, eventPrice } from './PublicCatalog';
  import type { CatalogData } from '../server/public-catalog';
  let { data }: { data: CatalogData & {editorialBlocks?:WebsiteBlock[]} } = $props();
  const descriptions = { events: 'Οι στιγμές που φέρνουν την οικογένεια της Λευτεριάς πιο κοντά.', partners: 'Οι άνθρωποι και οι οργανισμοί που μοιράζονται το όραμά μας.', sponsors: 'Δίπλα στην ομάδα. Μέρος της διαδρομής μας.', seasons: 'Οι αγώνες, οι ομάδες και οι στιγμές κάθε σεζόν.' };
  const title = $derived(catalogTitles[data.kind]);
</script>
<svelte:head><title>{title} | ΛΕΥΤΕΡΙΑ FC</title><meta name="description" content={descriptions[data.kind]} /></svelte:head>
<PublicShell site={data.site} branding={data.branding} organization={data.organization} navigationGroups={data.navigationGroups}>
  <section class="catalog" data-testid={`public-${data.kind}-catalog`}>
    <header class="catalog-heading"><p class="public-eyebrow">Η ΟΙΚΟΓΕΝΕΙΑ ΤΗΣ ΛΕΥΤΕΡΙΑΣ</p><h1>{title}</h1><p>{descriptions[data.kind]}</p></header>
    {#if data.kind==='sponsors' && data.editorialBlocks?.length}
      <div class="catalog-cms-region" data-testid="sponsors-cms-region" aria-label="Δημοσιευμένες ενότητες χορηγών"><WebsitePageBlocks blocks={data.editorialBlocks}/></div>
    {/if}
    {#if data.kind === 'events'}
      <nav class="event-chip-filters" aria-label="Φίλτρα εκδηλώσεων" data-testid="events-original-chip-filters">
        <div class="event-chip-row" data-testid="filter-scope">
          <span class="event-chip-heading">ΟΜΑΔΑ</span>
          <a href={catalogListHref('events',null,data.when,data.eventType)} aria-current={data.scope === null ? 'page' : undefined} data-testid="filter-scope-all">ΟΛΑ</a>
          <a href={catalogListHref('events','first_team',data.when,data.eventType)} aria-current={data.scope === 'first_team' ? 'page' : undefined} data-testid="filter-scope-first-team">ΠΡΩΤΗ ΟΜΑΔΑ</a>
          <a href={catalogListHref('events','academy',data.when,data.eventType)} aria-current={data.scope === 'academy' ? 'page' : undefined} data-testid="filter-scope-academy">ΑΚΑΔΗΜΙΑ</a>
        </div>
        <div class="event-chip-row" data-testid="filter-when">
          <span class="event-chip-heading">ΧΡΟΝΟΣ</span>
          <a href={catalogListHref('events',data.scope,'upcoming',data.eventType)} aria-current={data.when === 'upcoming' ? 'page' : undefined} data-testid="filter-when-upcoming">ΠΡΟΣΕΧΕΙΣ</a>
          <a href={catalogListHref('events',data.scope,'past',data.eventType)} aria-current={data.when === 'past' ? 'page' : undefined} data-testid="filter-when-past">ΠΑΡΕΛΘΟΝΤΕΣ</a>
          <a href={catalogListHref('events',data.scope,'all',data.eventType)} aria-current={data.when === 'all' ? 'page' : undefined} data-testid="filter-when-all">ΟΛΕΣ</a>
        </div>
        {#if data.eventTypes.length}
          <div class="event-chip-row" data-testid="filter-type">
            <span class="event-chip-heading">ΤΥΠΟΣ</span>
            <a href={catalogListHref('events',data.scope,data.when,'all')} aria-current={data.eventType === 'all' ? 'page' : undefined} data-testid="filter-type-all">ΟΛΟΙ</a>
            {#each data.eventTypes as value (value)}
              <a href={catalogListHref('events',data.scope,data.when,value)} aria-current={data.eventType === value ? 'page' : undefined} data-testid={'filter-type-'+value}>{eventTypeLabel(value)}</a>
            {/each}
          </div>
        {/if}
      </nav>
    {/if}
    <form method="GET"
      action={data.kind === 'sponsors' && data.fixedScope === 'academy' ? '/sponsors/academy'
        : data.kind === 'sponsors' && data.fixedScope === 'first_team' ? '/sponsors/first-team'
        : `/${data.kind}`}
      class="catalog-filters" aria-label="Φίλτρα ενότητας">
      {#if data.fixedScope}
        <p class="catalog-locked-scope" data-testid="catalog-locked-organization">
          {data.fixedScope === 'academy' ? 'ΛΕΥΤΕΡΙΑ FC ΑΚΑΔΗΜΙΑ' : 'ΛΕΥΤΕΡΙΑ FC · ΠΡΩΤΗ ΟΜΑΔΑ'}
        </p>
      {:else}
        <label>Ενότητα<select name="scope" value={data.scope ?? 'all'}><option value="all">Όλος ο σύλλογος</option><option value="first_team">Πρώτη ομάδα</option><option value="academy">Ακαδημία</option></select></label>
      {/if}
      {#if data.kind === 'events'}<label>Πότε<select name="when" value={data.when}><option value="upcoming">Προσεχείς</option><option value="past">Παλαιότερες</option><option value="all">Όλες οι εκδηλώσεις</option></select></label><label>Τύπος<select name="type" value={data.eventType}><option value="all">Όλοι οι τύποι</option>{#each data.eventTypes as eventType}<option value={eventType}>{eventTypeLabel(eventType)}</option>{/each}</select></label>{/if}
      <button class="public-button" type="submit">ΠΡΟΒΟΛΗ ↗</button>
    </form>
    <div class="catalog-results-heading"><p>{data.scope === 'academy' ? 'ΑΚΑΔΗΜΙΑ' : data.scope === 'first_team' ? 'ΠΡΩΤΗ ΟΜΑΔΑ' : 'ΟΛΟΣ Ο ΣΥΛΛΟΓΟΣ'}</p><span>{data.cards.length} {data.cards.length === 1 ? 'καταχώριση' : 'καταχωρίσεις'}</span></div>
    {#if data.cards.length}<div class="catalog-grid">{#each data.cards as item (`${item.organization}:${item.id}`)}<a class="catalog-card" class:catalog-sponsor={data.kind === 'sponsors'} class:catalog-season={data.kind === 'seasons'} href={item.href}>
      <div class="catalog-card-image">{#if item.image}<PublicImage src={item.image} alt="" />{:else}<span>{data.kind === 'seasons' ? item.title : 'ΛΕΥΤΕΡΙΑ FC'}</span>{/if}<span class="catalog-card-scope">{item.scopeLabel || catalogScopeLabel(item.organization)}</span>{#if item.event?.is_featured}<span class="catalog-card-featured" data-testid="published-featured-event">ΕΠΙΛΕΓΜΕΝΗ ΕΚΔΗΛΩΣΗ</span>{/if}</div>
      <div class="catalog-card-body"><p class="public-eyebrow">{data.kind === 'events' ? eventTypeLabel(item.label) : item.label}{item.meta && data.kind !== 'events' ? ` · ${greekDate(item.meta)}` : ''}</p><h2>{item.title}</h2>{#if item.summary}<p class="catalog-card-summary">{item.summary}</p>{/if}{#if item.event}<dl class="event-card-facts">
        {#if item.meta}<div><dt>Πότε</dt><dd><time datetime={item.meta}>{eventDateTime(item.meta)}</time> <small>Κύπρος</small></dd></div>{/if}
        {#if item.event.location}<div><dt>Τοποθεσία</dt><dd>{item.event.location}</dd></div>{/if}
        {#if item.event.price_eur !== null}<div><dt>Είσοδος</dt><dd>{eventPrice(item.event.price_eur)}</dd></div>{/if}
        {#if item.event.capacity != null}<div data-testid="published-event-capacity"><dt>Θέσεις συνολικά</dt><dd>{item.event.capacity}</dd></div>{/if}
        {#if item.event.status === 'cancelled' || item.event.status === 'completed'}<div><dt>Κατάσταση</dt><dd>{item.event.status === 'cancelled' ? 'Ακυρώθηκε' : 'Ολοκληρώθηκε'}</dd></div>{/if}
      </dl>{/if}<span class="catalog-card-action">{data.kind === 'seasons' ? 'ΑΝΑΚΑΛΥΨΕ ΤΗ ΣΕΖΟΝ' : 'ΠΕΡΙΣΣΟΤΕΡΑ'} <span aria-hidden="true">↗</span></span></div>
    </a>{/each}</div>{:else}<div class="catalog-empty"><h2>ΔΕΝ ΥΠΑΡΧΟΥΝ ΚΑΤΑΧΩΡΙΣΕΙΣ</h2><p>{data.kind === 'events' ? 'Δεν έχουν δημοσιευτεί εκδηλώσεις για τα επιλεγμένα φίλτρα.' : 'Δεν έχουν δημοσιευτεί καταχωρίσεις σε αυτή την ενότητα.'}</p>{#if !data.fixedScope && (data.scope || data.kind === 'events')}<a href={`/${data.kind}${data.kind === 'events' ? '?when=all' : ''}`}>Προβολή όλων ↗</a>{/if}</div>{/if}
  </section>
</PublicShell>

<style>
  .catalog-locked-scope {padding:10px 14px;border:1px solid #a88935;color:#f6bb38;font-weight:800;min-height:44px;display:flex;align-items:center;}
  .event-card-facts { display:grid; gap:10px; margin:18px 0; padding:16px 0; border-block:1px solid #65522d; font-size:13px; }
  .event-card-facts>div { display:grid; grid-template-columns:80px minmax(0,1fr); gap:10px; }
  .event-card-facts dt { color:var(--public-gold); font-weight:600; }
  .event-card-facts dd { margin:0; color:#eee; overflow-wrap:anywhere; }
  .event-card-facts small { color:#aaa; white-space:nowrap; }

  .catalog-card-featured{position:absolute;inset:auto auto 12px 12px;max-width:calc(100% - 24px);
    font:800 11px/1.3 'Fira Sans',sans-serif;letter-spacing:.04em;
    color:#111;background:#f6bb38;padding:8px 11px;z-index:1;overflow-wrap:anywhere}
  .event-card-facts>div{min-width:0}
  .event-card-facts dd{overflow-wrap:anywhere}
  .event-chip-filters{display:grid;gap:15px;border-top:1px solid #4b3c21;border-bottom:1px solid #4b3c21;padding:20px clamp(12px,2vw,24px);background:#080808}
  .event-chip-row{display:flex;align-items:center;flex-wrap:wrap;gap:9px}
  .event-chip-heading{min-width:88px;color:#a9a9a9;letter-spacing:.07em;font-weight:800;font-size:12px}
  .event-chip-row a{border:1px solid #504a3e;border-radius:999px;padding:10px 17px;min-height:44px;display:inline-flex;align-items:center;justify-content:center;color:#d4d4d4;background:#141414;text-decoration:none;font-size:13px;font-weight:800}
  .event-chip-row a:hover,.event-chip-row a:focus-visible{border-color:#f6bb38;color:#f6bb38}
  .event-chip-row a:focus-visible{outline:3px solid #f6bb38;outline-offset:2px}
  .event-chip-row a[aria-current="page"]{background:#f6bb38;border-color:#f6bb38;color:#111}
  @media(max-width:650px){.event-chip-row{gap:7px}.event-chip-heading{width:100%;min-width:unset}.event-chip-row a{font-size:12px;padding:9px 12px}}
</style>
