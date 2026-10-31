<script lang="ts">
  import PublicShell from '../../lib/public/PublicShell.svelte';
  import NewsCards from '../../lib/public/NewsCards.svelte';
  import Unavailable from '../../lib/public/Unavailable.svelte';
  import { newsLink } from '../../lib/public/content';
  import type { PublicNews, PublicNewsScope, PublicPageData } from '../../lib/public/types';
  let { data }: { data: PublicPageData & { newsScope: PublicNewsScope; news: PublicNews | null; newsAvailable: boolean; page: number; category: string } } = $props();
  const categories = ['Νέα', 'Αποτελέσματα', 'Μεταγραφές', 'Ακαδημία'];
</script>
<svelte:head><title>ΝΕΑ & ΑΝΑΚΟΙΝΩΣΕΙΣ | ΛΕΥΤΕΡΙΑ FC</title><meta name="description" content="Τα νέα και οι ανακοινώσεις της Λευτεριάς FC και της ακαδημίας." /></svelte:head>
<PublicShell site={data.site} organization={data.organization} navigationGroups={data.navigationGroups}><section class="public-page-heading"><p class="public-eyebrow">ΜΑΘΕ ΤΑ ΝΕΑ ΜΑΣ</p><h1>ΝΕΑ & ΑΝΑΚΟΙΝΩΣΕΙΣ</h1><nav class="public-subnav" aria-label="Νέα ανά οργανισμό"><a href={newsLink(null, data.category)} aria-current={data.newsScope === null ? 'page' : undefined}>ΟΛΑ</a><a href={newsLink('lefteria_fc', data.category)} aria-current={data.newsScope === 'lefteria_fc' ? 'page' : undefined}>ΠΡΩΤΗ ΟΜΑΔΑ</a><a href={newsLink('lefteria_fc_academy', data.category)} aria-current={data.newsScope === 'lefteria_fc_academy' ? 'page' : undefined}>ΑΚΑΔΗΜΙΑ</a></nav><nav class="public-subnav" aria-label="Κατηγορίες ειδήσεων"><a href={newsLink(data.newsScope)} aria-current={!data.category ? 'page' : undefined}>ΟΛΕΣ ΟΙ ΚΑΤΗΓΟΡΙΕΣ</a>{#each categories as category}<a href={newsLink(data.newsScope, category)} aria-current={data.category === category ? 'page' : undefined}>{category}</a>{/each}{#if data.category && !categories.includes(data.category)}<a href={newsLink(data.newsScope, data.category)} aria-current="page">{data.category}</a>{/if}</nav></section>
  {#if !data.newsAvailable}<Unavailable />{:else}<section class="public-section">{#if data.news?.items.length}<NewsCards articles={data.news.items} newsScope={data.newsScope} />{:else}<p class="public-empty">Δεν υπάρχουν δημοσιευμένα νέα.</p>{/if}<nav class="public-pagination" aria-label="Σελίδες ειδήσεων">{#if data.page > 1}<a href={newsLink(data.newsScope, data.category, data.page - 1)}>← ΠΡΟΗΓΟΥΜΕΝΗ</a>{/if}<span>ΣΕΛΙΔΑ {data.page}</span>{#if data.news && data.page * 12 < Number(data.news.total)}<a href={newsLink(data.newsScope, data.category, data.page + 1)}>ΕΠΟΜΕΝΗ →</a>{/if}</nav></section>{/if}
</PublicShell>
