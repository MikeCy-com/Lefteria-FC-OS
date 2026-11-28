<script lang="ts">
  import { page } from '$app/state';
  import { isIndexablePublicOrigin } from '../../../lib/public/seo';
  import PublicNewsStructuredData from '../../../lib/public/PublicNewsStructuredData.svelte';
  import PublicShell from '../../../lib/public/PublicShell.svelte';
  import PublicImage from '../../../lib/public/PublicImage.svelte';
  import MarkdownContent from '../../../lib/public/MarkdownContent.svelte';
  import Unavailable from '../../../lib/public/Unavailable.svelte';
  import { articleLink, greekDate, newsLink } from '../../../lib/public/content';
  import { publicCanonicalHref, publicSocialImage } from '../../../lib/public/public-urls';
  import type { PublicArticle, PublicNewsScope, PublicPageData } from '../../../lib/public/types';
  let { data }: { data: PublicPageData & { newsScope: PublicNewsScope; article: PublicArticle | null; articleAvailable: boolean } } = $props();
  const canonicalPath = $derived(data.articleAvailable && data.article ? articleLink(data.article, data.newsScope) : undefined);
  // Only an available, published article may supply crawler/social metadata.
  const title = $derived(canonicalPath && data.article ? data.article.title : 'Νέα');
  const description = $derived(canonicalPath && data.article ? data.article.excerpt || 'Νέα της Λευτεριάς FC.' : 'Νέα της Λευτεριάς FC.');
  const socialImage = $derived(canonicalPath && data.article ? publicSocialImage(data.article.image_url) : null);
</script>
<svelte:head>
  <title>{title} | ΛΕΥΤΕΡΙΑ FC</title>
  <meta name="description" content={description} />
  {#if canonicalPath}
    <meta property="og:type" content="article" />
    <meta property="og:title" content={title} />
    <meta property="og:description" content={description} />
    <meta property="og:url" content={publicCanonicalHref(canonicalPath)} />
    <meta name="twitter:card" content={socialImage ? 'summary_large_image' : 'summary'} />
    <meta name="twitter:title" content={title} />
    <meta name="twitter:description" content={description} />
    {#if socialImage}
      <meta property="og:image" content={socialImage} />
      <meta name="twitter:image" content={socialImage} />
    {/if}
  {:else}
    <meta name="robots" content="noindex,follow" />
  {/if}
</svelte:head>
<PublicNewsStructuredData article={data.articleAvailable ? data.article : null} scope={data.newsScope}
  indexable={isIndexablePublicOrigin(page.url)} />
<PublicShell site={data.site} branding={data.branding} organization={data.organization} navigationGroups={data.navigationGroups} {canonicalPath}>{#if data.articleAvailable && data.article}<article class="public-article">{#if data.article.image_url}<PublicImage class="article-cover" src={data.article.image_url} alt="" eager />{/if}<div class="article-body"><a class="public-back" href={newsLink(data.newsScope)}>← ΠΙΣΩ ΣΤΑ ΝΕΑ</a><p class="public-eyebrow">{data.article.category}</p><h1>{data.article.title}</h1>{#if data.article.created_at}<time datetime={data.article.created_at}>{greekDate(data.article.created_at)}</time>{/if}{#if data.article.excerpt}<p class="article-excerpt">{data.article.excerpt}</p>{/if}<MarkdownContent content={data.article.content} /></div></article>{:else}<Unavailable />{/if}</PublicShell>
