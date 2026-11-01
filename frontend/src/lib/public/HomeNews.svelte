<script lang="ts">
  import type { PublicArticle } from './types';
  import NewsCards from './NewsCards.svelte';
  import Icon from './PublicIcon.svelte';
  import Unavailable from './Unavailable.svelte';
  let { articles, available }: { articles: PublicArticle[]; available: boolean } = $props();
  let filter = $state('all');
  const categories = $derived([...new Set(articles.map(article => article.category).filter(Boolean))]);
  const rows = $derived((filter === 'all' ? articles : articles.filter(article => article.category === filter)).slice(0,3));
</script>
<section class="public-news-section" data-testid="public-home-news"><div class="public-section-heading">
  <div><p class="public-eyebrow">ΤΕΛΕΥΤΑΙΑ ΝΕΑ</p><h2>ΝΕΑ & ΑΝΑΚΟΙΝΩΣΕΙΣ</h2></div>
  <div class="public-news-filters">{#each [['all','ΟΛΑ'],...categories.map(category => [category,category])] as [value,label], index}<button type="button" aria-pressed={filter === value} onclick={() => { filter = value; }} data-testid={`public-news-filter-${index}`}>{label}</button>{/each}</div>
  <a href="/news" data-testid="public-news-all">ΔΕΙΤΕ ΟΛΑ ΤΑ ΝΕΑ <Icon name="arrow" size={17} /></a>
</div>{#if available}<NewsCards articles={rows} />{:else}<Unavailable />{/if}</section>
