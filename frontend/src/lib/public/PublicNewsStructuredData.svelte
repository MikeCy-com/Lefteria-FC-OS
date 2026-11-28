<script lang="ts">
  import type { PublicArticle, PublicNewsScope } from './types';
  import { newsStructuredData } from './news-structured-data';
  let { article, scope, indexable }: {
    article: PublicArticle | null; scope: PublicNewsScope; indexable: boolean;
  } = $props();
  const markup = $derived(newsStructuredData(article, scope, indexable));
</script>
<svelte:head>
  {#if markup}
    <!-- Only the dedicated JSON serializer may produce this raw-text element. -->
    {@html markup}
  {/if}
</svelte:head>
