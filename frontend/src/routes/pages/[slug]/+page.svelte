<script lang="ts">
  import PublicShell from '../../../lib/public/PublicShell.svelte';
  import WebsitePageBlocks from '../../../lib/public/WebsitePageBlocks.svelte';
  import type { PublicPageData } from '../../../lib/public/types';
  import type { PublishedWebsitePage } from '../../../lib/public/website-pages';
  import { websitePagePath } from '../../../lib/public/website-pages';
  import { publicCanonicalHref } from '../../../lib/public/public-urls';
  let {data}: {data: PublicPageData & {published: PublishedWebsitePage}} = $props();
  const content = $derived(data.published.page);
  const canonicalPath = $derived(websitePagePath(data.published.slug, data.organization));
</script>
<svelte:head>
  <title>{content.seo_title || content.title} | ΛΕΥΤΕΡΙΑ FC</title>
  <meta name="description" content={content.seo_description || content.summary} />
  <meta property="og:type" content="website"/>
  <meta property="og:title" content={content.seo_title || content.title}/>
  <meta property="og:description" content={content.seo_description || content.summary}/>
  <meta property="og:url" content={publicCanonicalHref(canonicalPath)}/>
</svelte:head>
<PublicShell site={data.site} branding={data.branding} organization={data.organization}
  navigationGroups={data.navigationGroups} {canonicalPath}>
  <article class="website-page">
    <header class="website-page-header">
      <p class="public-eyebrow">{data.organization === 'lefteria_fc' ? 'ΠΡΩΤΗ ΟΜΑΔΑ' : 'ΑΚΑΔΗΜΙΑ'}</p>
      <h1>{content.title}</h1>
      {#if content.summary}<p>{content.summary}</p>{/if}
    </header>
    <WebsitePageBlocks blocks={content.blocks} />
  </article>
</PublicShell>
<style>
.website-page{margin:auto;max-width:1080px;padding:clamp(26px,5vw,70px) clamp(16px,3vw,48px) 90px}
.website-page-header{margin-bottom:32px;border-bottom:1px solid #58503a;padding-bottom:26px}
.website-page-header h1{font:italic 900 clamp(38px,6vw,78px)/.97 'Fira Sans Extra Condensed',sans-serif;margin:12px 0}
.website-page-header>p:last-child{max-width:72ch;line-height:1.6}
</style>