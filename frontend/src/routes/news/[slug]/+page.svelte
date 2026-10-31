<script lang="ts">
  import PublicShell from '../../../lib/public/PublicShell.svelte';
  import PublicImage from '../../../lib/public/PublicImage.svelte';
  import MarkdownContent from '../../../lib/public/MarkdownContent.svelte';
  import Unavailable from '../../../lib/public/Unavailable.svelte';
  import { greekDate, newsLink } from '../../../lib/public/content';
  import type { PublicArticle, PublicNewsScope, PublicPageData } from '../../../lib/public/types';
  let { data }: { data: PublicPageData & { newsScope: PublicNewsScope; article: PublicArticle | null; articleAvailable: boolean } } = $props();
</script>
<svelte:head><title>{data.article?.title || 'Νέα'} | ΛΕΥΤΕΡΙΑ FC</title><meta name="description" content={data.article?.excerpt || 'Νέα της Λευτεριάς FC.'} /><meta property="og:type" content="article" />{#if data.article?.image_url}<meta property="og:image" content={data.article.image_url} />{/if}</svelte:head>
<PublicShell site={data.site} organization={data.organization} navigationGroups={data.navigationGroups}>{#if data.articleAvailable && data.article}<article class="public-article">{#if data.article.image_url}<PublicImage class="article-cover" src={data.article.image_url} alt="" eager />{/if}<div class="article-body"><a class="public-back" href={newsLink(data.newsScope)}>← ΠΙΣΩ ΣΤΑ ΝΕΑ</a><p class="public-eyebrow">{data.article.category}</p><h1>{data.article.title}</h1>{#if data.article.created_at}<time datetime={data.article.created_at}>{greekDate(data.article.created_at)}</time>{/if}{#if data.article.excerpt}<p class="article-excerpt">{data.article.excerpt}</p>{/if}<MarkdownContent content={data.article.content} /></div></article>{:else}<Unavailable />{/if}</PublicShell>
