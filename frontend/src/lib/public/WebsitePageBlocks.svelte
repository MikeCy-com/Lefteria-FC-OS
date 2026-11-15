<script lang="ts">
  import type { WebsiteBlock } from './website-pages';
  import PublicImage from './PublicImage.svelte';
  let {blocks}: {blocks: readonly WebsiteBlock[]} = $props();
  // No raw HTML or arbitrary CSS is ever rendered. All text is escaped by Svelte.
  const safe = (url: string|null) => url && !url.includes('\\') &&
    (url.startsWith('/') && !url.startsWith('//') || /^https:\/\//i.test(url)) ? url : null;
</script>
<div class="website-blocks">
  {#each blocks as block, index (index)}
    {#if block.type === 'heading'}
      <h2>{block.text}</h2>
    {:else if block.type === 'paragraph'}
      <p class="website-text">{block.text}</p>
    {:else if block.type === 'image' && safe(block.url)}
      <figure><PublicImage src={safe(block.url)} alt={block.text} /></figure>
    {:else if block.type === 'button' && safe(block.url)}
      <p><a class="public-button website-cta" href={safe(block.url)} rel="noopener noreferrer" target={safe(block.url)?.startsWith('https:') ? '_blank' : undefined}>{block.text}</a></p>
    {/if}
  {/each}
</div>
<style>
.website-blocks{display:grid;gap:clamp(15px,2.4vw,29px);line-height:1.7}
.website-blocks h2{margin:10px 0 0;font:italic 850 clamp(28px,3.6vw,48px)/1.1 'Fira Sans Extra Condensed',sans-serif}
.website-text{white-space:pre-wrap;overflow-wrap:anywhere;margin:0;max-width:78ch}
.website-blocks figure{margin:0}
.website-blocks figure :global(img){max-width:100%;height:auto;border-radius:8px}
.website-cta{display:inline-flex;align-items:center;min-height:46px}
</style>