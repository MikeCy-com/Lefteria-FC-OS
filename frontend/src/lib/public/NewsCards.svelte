<script lang="ts">
  import PublicImage from './PublicImage.svelte';
  import { articleLink, greekDate } from './content';
  import type { PublicArticle, PublicNewsScope } from './types';
  let { articles, newsScope = null }: { articles: PublicArticle[]; newsScope?: PublicNewsScope } = $props();
</script>
<div class="public-news-grid">{#each articles as article (article.id)}<article class="public-news-card"><a href={articleLink(article, newsScope)}><div class="news-image">{#if article.image_url}<PublicImage src={article.image_url} alt="" />{:else}<span>ΛΕΥΤΕΡΙΑ FC</span>{/if}</div><div class="news-card-body"><div class="news-meta"><span>{article.category}</span>{#if article.created_at}<time datetime={article.created_at}>{greekDate(article.created_at)}</time>{/if}</div><h3>{article.title}</h3>{#if article.excerpt}<p>{article.excerpt}</p>{/if}<span class="read-more">ΔΙΑΒΑΣΕ ΠΕΡΙΣΣΟΤΕΡΑ ↗</span></div></a></article>{/each}</div>
