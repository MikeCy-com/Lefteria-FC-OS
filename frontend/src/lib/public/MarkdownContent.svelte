<script lang="ts">
  import { inlineTokens, markdownBlocks } from './content';
  let { content }: { content: string } = $props();
</script>
{#snippet inline(text: string)}{#each inlineTokens(text) as token}{#if token.kind === 'strong'}<strong>{token.text}</strong>{:else if token.kind === 'link'}<a href={token.href} rel="noreferrer" target={token.href?.startsWith('https:') ? '_blank' : undefined}>{token.text}</a>{:else}{token.text}{/if}{/each}{/snippet}
<div class="public-markdown">{#each markdownBlocks(content) as block}{#if block.kind === 'heading'}{#if block.level === 1}<h2>{@render inline(block.text)}</h2>{:else}<h3>{@render inline(block.text)}</h3>{/if}{:else if block.kind === 'list'}<ul>{#each block.items ?? [] as item}<li>{@render inline(item)}</li>{/each}</ul>{:else if block.kind === 'quote'}<blockquote>{@render inline(block.text)}</blockquote>{:else}<p>{@render inline(block.text)}</p>{/if}{/each}</div>
