<script lang="ts">
  import PublicImage from './PublicImage.svelte';
  import Icon from './PublicIcon.svelte';
  import { articleLink, greekDate } from './content';
  import type { PublicArticle, PublicNewsScope } from './types';
  let { articles, newsScope = null, quote = false }: { articles: PublicArticle[]; newsScope?: PublicNewsScope; quote?: boolean } = $props();
</script>
<div class="public-news-grid" class:public-news-list={!quote}>
  {#each articles as article (article.id)}<a href={articleLink(article,newsScope)} class="public-news-card" data-testid={`news-${article.id}`}>
    <div class="public-news-image">{#if article.image_url}<PublicImage src={article.image_url} alt={article.title} />{:else}<span>ΛΕΥΤΕΡΙΑ FC</span>{/if}</div>
    <div class="public-news-body"><div><span class="public-news-category">{article.category}</span>{#if article.created_at}<time datetime={article.created_at}>{greekDate(article.created_at)}</time>{/if}</div>
      <h3>{article.title}</h3>{#if article.excerpt}<p>{article.excerpt}</p>{/if}<span class="public-news-read">Διάβασε περισσότερα <Icon name="arrow" size={16} /></span>
    </div>
  </a>{/each}
  {#if !articles.length}<p class="public-empty" data-testid="public-news-empty">Δεν υπάρχουν δημοσιευμένα νέα.</p>{/if}
  {#if quote}<aside class="public-news-quote"><span aria-hidden="true">“</span><blockquote>Τα μεγάλα όνειρα ξεκινούν από μικρούς ποδοσφαιριστές.</blockquote><p>ΛΕΥΤΕΡΙΑ FC</p></aside>{/if}
</div>
