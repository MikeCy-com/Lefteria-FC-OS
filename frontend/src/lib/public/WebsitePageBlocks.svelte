<script lang="ts">
  import {DEFAULT_BLOCK_LAYOUT,DEFAULT_PAGE_LAYOUT,validBlockLayout,validPageLayout, type WebsiteBlock, type WebsitePageLayout} from './website-pages';
  import PublicImage from './PublicImage.svelte';
  let {blocks,pageLayout}: {blocks: readonly WebsiteBlock[];pageLayout?:WebsitePageLayout|null} = $props();
  // A page only writes the settings it overrides. All remaining values are
  // inherited from the published organization theme on its PublicShell.
  const dimension=(key:keyof typeof DEFAULT_PAGE_LAYOUT,token:string)=>
    validPageLayout(pageLayout) && pageLayout?.[key]!=null
      ? pageLayout[key]+'px'
      : 'var(--lf-layout-'+token+', '+DEFAULT_PAGE_LAYOUT[key]+'px)';
  const blockLayout=(block:WebsiteBlock)=>validBlockLayout(block.layout) && block.layout ? block.layout : DEFAULT_BLOCK_LAYOUT;
  // No raw HTML or arbitrary CSS is ever rendered. All text is escaped by Svelte.
  const safe = (url: string|null) => url && !url.includes('\\') &&
    (url.startsWith('/') && !url.startsWith('//') || /^https:\/\//i.test(url)) ? url : null;
</script>
<div class="website-blocks" data-testid="website-block-layout"
  style={`--cms-max-width:${dimension('max_width','page-max-width')};--cms-gap:${dimension('gap','page-gap')};--cms-side:${dimension('side_padding','page-side-padding')};--cms-page-top:${dimension('margin_top','page-margin-top')};--cms-page-bottom:${dimension('margin_bottom','page-margin-bottom')}`}>
  {#each blocks as block, index (index)}
    <div class="website-block-wrap" data-width={blockLayout(block).width}
      style={`--cms-min-height:${blockLayout(block).min_height}px;--cms-mt:${blockLayout(block).margin_top}px;--cms-mb:${blockLayout(block).margin_bottom}px`}>
    {#if block.type === 'heading'}
      {#if block.level===3}<h3>{block.text}</h3>
      {:else if block.level===4}<h4>{block.text}</h4>
      {:else if block.level===5}<h5>{block.text}</h5>
      {:else if block.level===6}<h6>{block.text}</h6>
      {:else}<h2>{block.text}</h2>{/if}
    {:else if block.type === 'paragraph'}
      <p class="website-text">{block.text}</p>
    {:else if block.type === 'image' && safe(block.url)}
      <figure><PublicImage src={safe(block.url)} alt={block.text} /></figure>
    {:else if block.type === 'button' && safe(block.url)}
      <p><a class="public-button website-cta" href={safe(block.url)} rel="noopener noreferrer" target={safe(block.url)?.startsWith('https:') ? '_blank' : undefined}>{block.text}</a></p>
    {/if}
    </div>
  {/each}
</div>
<style>
.website-blocks{display:grid;grid-template-columns:repeat(6,minmax(0,1fr));gap:var(--cms-gap);max-width:var(--cms-max-width);margin-inline:auto;margin-block:var(--cms-page-top) var(--cms-page-bottom);padding-inline:var(--cms-side);line-height:1.7;min-width:0}
.website-block-wrap{grid-column:span 6;min-width:0;min-height:var(--cms-min-height);margin-top:var(--cms-mt);margin-bottom:var(--cms-mb)}
@media(min-width:768px){.website-block-wrap[data-width="half"]{grid-column:span 3}.website-block-wrap[data-width="third"]{grid-column:span 2}}
@media(max-width:767px){
.website-blocks{grid-template-columns:minmax(0,1fr)}
.website-block-wrap{grid-column:span 1}
.website-blocks h2{font-size:var(--lf-h2-mobile,clamp(28px,3.6vw,48px))}
.website-blocks h3{font-size:var(--lf-h3-mobile,clamp(24px,3vw,36px))}
.website-blocks h4{font-size:var(--lf-h4-mobile,clamp(21px,2.5vw,29px))}
.website-blocks h5{font-size:var(--lf-h5-mobile,clamp(18px,2.1vw,23px))}
.website-blocks h6{font-size:var(--lf-h6-mobile,clamp(16px,1.8vw,20px))}
}
/* Use published role tokens in the originating component, not a second
 * hardcoded font cascade that must be defeated with !important. */
.website-blocks :is(h2,h3,h4,h5,h6){margin:10px 0 0;font-style:italic}
.website-blocks h2{font-family:var(--lf-h2-family,'Fira Sans Extra Condensed',sans-serif);font-size:var(--lf-h2-desktop,clamp(28px,3.6vw,48px));font-weight:var(--lf-h2-weight,850);line-height:var(--lf-h2-line,1.1)}
.website-blocks h3{font-family:var(--lf-h3-family,'Fira Sans Extra Condensed',sans-serif);font-size:var(--lf-h3-desktop,clamp(24px,3vw,36px));font-weight:var(--lf-h3-weight,850);line-height:var(--lf-h3-line,1.1)}
.website-blocks h4{font-family:var(--lf-h4-family,'Fira Sans Extra Condensed',sans-serif);font-size:var(--lf-h4-desktop,clamp(21px,2.5vw,29px));font-weight:var(--lf-h4-weight,850);line-height:var(--lf-h4-line,1.1)}
.website-blocks h5{font-family:var(--lf-h5-family,'Fira Sans Extra Condensed',sans-serif);font-size:var(--lf-h5-desktop,clamp(18px,2.1vw,23px));font-weight:var(--lf-h5-weight,850);line-height:var(--lf-h5-line,1.1)}
.website-blocks h6{font-family:var(--lf-h6-family,'Fira Sans Extra Condensed',sans-serif);font-size:var(--lf-h6-desktop,clamp(16px,1.8vw,20px));font-weight:var(--lf-h6-weight,850);line-height:var(--lf-h6-line,1.1)}
.website-text{white-space:pre-wrap;overflow-wrap:anywhere;margin:0;max-width:78ch}
.website-blocks figure{margin:0}
.website-blocks figure :global(img){max-width:100%;height:auto;border-radius:8px}
.website-cta{display:inline-flex;align-items:center;min-height:46px}
</style>