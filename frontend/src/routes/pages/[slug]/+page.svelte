<script lang="ts">
  import PublicShell from '../../../lib/public/PublicShell.svelte';
  import WebsitePageBlocks from '../../../lib/public/WebsitePageBlocks.svelte';
  import type { PublicPageData } from '../../../lib/public/types';
  import type { PublishedWebsitePage } from '../../../lib/public/website-pages';
  import { websitePagePath, websitePageParentLink,validPageLayout } from '../../../lib/public/website-pages';
  import { publicCanonicalHref } from '../../../lib/public/public-urls';
  let {data}: {data: PublicPageData & {published: PublishedWebsitePage}} = $props();
  const content = $derived(data.published.page);
  const canonicalPath = $derived(websitePagePath(data.published.slug, data.organization));
  const parentLink = $derived(websitePageParentLink(data.organization, content.menu_parent));
  const localLayout=$derived(validPageLayout(content.layout)?content.layout:null);
  const localPageWidth=$derived(localLayout?.max_width!=null?localLayout.max_width+'px':'var(--lf-layout-page-max-width,1200px)');
  const localPageSide=$derived(localLayout?.side_padding!=null?localLayout.side_padding+'px':'var(--lf-layout-page-side-padding,clamp(20px,4.5vw,64px))');
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
  <article class="website-page" data-organization={data.organization} data-testid="website-published-page"
    style={`--lf-page-content-max-width:${localPageWidth};--lf-page-content-side:${localPageSide}`}>
    <header class="website-page-header">
      <div class="website-page-header-inner">
        <nav class="website-page-breadcrumbs" aria-label="Διαδρομή πλοήγησης" data-testid="website-page-breadcrumbs">
          <a href={parentLink.href} data-testid="website-page-parent-link">{parentLink.label}</a>
          <span aria-hidden="true">/</span>
          <span aria-current="page">{content.title}</span>
        </nav>
        <p class="public-eyebrow">{data.organization === 'lefteria_fc' ? 'ΛΕΥΤΕΡΙΑ FC · ΠΡΩΤΗ ΟΜΑΔΑ' : 'ΛΕΥΤΕΡΙΑ FC · ΑΚΑΔΗΜΙΑ'}</p>
        <h1>{content.title}</h1>
        {#if content.summary}<p class="website-page-summary">{content.summary}</p>{/if}
      </div>
    </header>
    <div class="website-page-content" data-testid="website-published-content">
      <WebsitePageBlocks blocks={content.blocks} pageLayout={content.layout}/>
      <nav class="website-page-return" aria-label="Επιστροφή στην ενότητα">
        <a href={parentLink.href} data-testid="website-page-return">← ΕΠΙΣΤΡΟΦΗ · {parentLink.label}</a>
      </nav>
    </div>
  </article>
</PublicShell>
<style>
  .website-page{
    min-width:0;
    background:linear-gradient(180deg,#090b0c 0%,#101313 100%);
    color:#f8f7f4;
  }
  .website-page-header{
    position:relative;
    isolation:isolate;
    overflow:hidden;
    border-bottom:1px solid #765c2b;
    background:
      radial-gradient(ellipse 70% 120% at 84% 25%,rgb(246 187 56 / 11%),transparent 70%),
      linear-gradient(125deg,#1b1e1c 0%,#070809 78%);
  }
  .website-page-header::before{
    position:absolute;inset:0;pointer-events:none;z-index:-1;
    content:"";opacity:.65;
    background:repeating-linear-gradient(121deg,transparent 0,transparent 145px,rgb(246 187 56 / 4%) 147px,transparent 152px);
  }
  .website-page-header::after{
    position:absolute;right:clamp(-60px,-3vw,-12px);top:-32%;
    width:clamp(95px,16vw,240px);height:155%;z-index:-1;
    content:"";pointer-events:none;transform:skew(-22deg);
    background:linear-gradient(90deg,transparent 0 35%,rgb(246 187 56 / 14%) 35% 44%,transparent 44% 61%,rgb(246 187 56 / 8%) 61% 75%,transparent 75%);
  }
  .website-page-header-inner{
    max-width:1200px;
    margin:0 auto;
    padding:clamp(38px,5vw,82px) clamp(20px,4.5vw,64px) clamp(42px,5vw,78px);
  }
  .website-page-breadcrumbs{
    display:flex;align-items:center;flex-wrap:wrap;column-gap:11px;row-gap:6px;
    max-width:85ch;margin:0 0 clamp(30px,3vw,49px);
    color:#d9d3c8;font-size:13px;line-height:1.5;
    overflow-wrap:anywhere;
  }
  .website-page-breadcrumbs a{
    color:var(--public-gold);font-weight:800;letter-spacing:.035em;
    text-decoration:none;min-height:42px;display:inline-flex;align-items:center;
  }
  .website-page-breadcrumbs a:hover{text-decoration:underline;text-underline-offset:4px}
  .website-page-breadcrumbs a:focus-visible,.website-page-return a:focus-visible{
    outline:3px solid var(--public-gold);outline-offset:4px;
  }
  .website-page-breadcrumbs>span:last-child{font-weight:650;color:#eee;max-width:65ch}
  .website-page-header .public-eyebrow{
    color:var(--public-gold);
    font:800 15px/1.25 'Fira Sans Extra Condensed',sans-serif;
    letter-spacing:.07em;
    margin:0 0 14px;
  }
  .website-page-header h1{
    max-width:1050px;
    margin:0;
    font:italic 900 clamp(36px,5vw,78px)/1 'Fira Sans Extra Condensed',sans-serif;
    text-wrap:balance;overflow-wrap:anywhere;
    text-shadow:0 8px 25px rgb(0 0 0 / 40%);
  }
  .website-page-summary{
    margin:clamp(17px,2vw,28px) 0 0;
    max-width:72ch;
    color:#e4e2de;
    line-height:1.7;
    font-size:clamp(15px,1.2vw,18px);
    overflow-wrap:anywhere;
  }
  .website-page-content{
    max-width:var(--lf-page-content-max-width,1200px);
    margin:0 auto;
    padding:clamp(28px,4.5vw,68px) var(--lf-page-content-side,clamp(20px,4.5vw,64px)) clamp(60px,7vw,108px);
  }
  .website-page-return{
    margin-top:clamp(38px,5vw,70px);
    padding-top:22px;border-top:1px solid rgb(246 187 56 / 25%);
  }
  .website-page-return a{
    display:inline-flex;align-items:center;min-height:48px;
    padding:10px 17px;
    border:1px solid var(--public-gold);border-radius:0 9px 0 7px;
    background:rgb(246 187 56 / 6%);
    color:var(--public-gold);text-decoration:none;
    font:800 15px/1.35 'Fira Sans Extra Condensed',sans-serif;
  }
  .website-page-return a:hover{
    color:#12100a;background:var(--public-gold);border-color:var(--public-gold);
  }
  .website-page[data-organization="lefteria_fc_academy"] .website-page-header{
    background:
      radial-gradient(ellipse 56% 110% at 80% 23%,rgb(246 187 56 / 12%),transparent 70%),
      linear-gradient(121deg,#171a17 0%,#070809 82%);
  }
  @media(max-width:650px){
    .website-page-header-inner{padding:29px 18px 43px;}
    .website-page-breadcrumbs{margin-bottom:27px;}
    .website-page-header h1{font-size:clamp(36px,9vw,54px);}
    .website-page-content{padding:32px 18px 64px;}
    .website-page-return a{width:100%;justify-content:center;white-space:normal;}
  }
  @media(prefers-reduced-motion:reduce){
    .website-page a{transition:none!important;}
  }
</style>
