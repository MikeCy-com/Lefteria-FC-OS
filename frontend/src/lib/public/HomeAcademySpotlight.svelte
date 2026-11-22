<script lang="ts">
  import type {PublicGroup,PublicSite} from './types';
  import type {WebsitePageContent} from './website-pages.ts';
  import {publicImages} from './images';
  import Icon from './PublicIcon.svelte';
  let {site,groups=[],editorial=null}:{site:PublicSite|null;groups?:PublicGroup[];editorial?:WebsitePageContent|null}=$props();
  const spotlightWords=$derived(editorial?.blocks[0]?.text.split('·').map(word=>word.trim()) ?? null);
  const sections=$derived((groups.length ? groups : (site?.academy_groups??[]))
    .filter(group => group.organization_id === 'lefteria_fc_academy'));
</script>

<section class="home-academy-spotlight" aria-labelledby="home-academy-title"
  data-testid="home-academy-spotlight">
  <div class="home-academy-image" aria-hidden="true"
    style:background-image={`linear-gradient(90deg,#080a0add 0%,#080a0aa0 50%,#080a0a16 100%),url("${publicImages.hero}")`}></div>
  <div class="home-academy-content">
    <p class="home-academy-eyebrow">ΛΕΥΤΕΡΙΑ FC <span>ACADEMY</span></p>
    {#if spotlightWords}
      <h2 id="home-academy-title" data-testid="home-academy-approved-title">
        {#each spotlightWords as word, index (index)}{#if index>0} <span>·</span> {/if}{word}{/each}
      </h2>
      <p data-testid="home-academy-approved-copy">{editorial?.blocks[1]?.text}</p>
    {:else}
      <h2 id="home-academy-title">ΑΝΑΠΤΥΞΗ <span>·</span> ΠΑΙΔΕΙΑ <span>·</span> ΟΝΕΙΡΑ</h2>
      <p>Κάθε παιδί έχει τη δική του διαδρομή. Γνώρισε τα τμήματα,
        τους ανθρώπους και την ποδοσφαιρική φιλοσοφία της Ακαδημίας μας.</p>
    {/if}
    {#if sections.length}
      <p class="home-academy-groups">{sections.length} {sections.length===1?'τμήμα':'τμήματα'}
        στο πρόγραμμα της Ακαδημίας</p>
    {/if}
  </div>
  <a href="/academy" class="home-academy-link"
    data-testid="home-academy-link" aria-label="Μάθε περισσότερα για την Ακαδημία">
    ΜΑΘΕ ΠΕΡΙΣΣΟΤΕΡΑ <Icon name="arrow" size={18}/>
  </a>
</section>

<style>
  .home-academy-spotlight {
    position:relative;isolation:isolate;min-height:196px;
    padding:28px clamp(18px,3.5vw,56px);
    display:flex;align-items:center;gap:24px;justify-content:space-between;
    border-block:1px solid var(--lf-color-accent,#776044);background:var(--lf-color-bg,#101112);overflow:hidden;
    color:var(--lf-color-text,#ffffff);
  }
  .home-academy-image {
    position:absolute;inset:0;z-index:-1;
    background-position:center 45%;background-size:cover;
    filter:saturate(.88);
  }
  .home-academy-content{max-width:740px;min-width:0;}
  .home-academy-eyebrow {
    color:var(--lf-color-text,#ffffff);font:800 clamp(18px,2.4vw,30px)/1.1 'Fira Sans Extra Condensed',sans-serif;
    font-style:italic;margin:0 0 6px;
  }
  .home-academy-eyebrow span{color:var(--lf-color-accent,#f6bb38);}
  h2{font:800 clamp(20px,2vw,29px)/1.16 'Fira Sans Extra Condensed',sans-serif;
    margin:0 0 9px;color:var(--lf-color-text,#ffffff);}
  h2 span{color:var(--lf-color-accent,#f6bb38)}
  .home-academy-content>p:not(.home-academy-eyebrow) {
    font-size:clamp(13px,1.1vw,16px);max-width:62ch;line-height:1.4;
    margin:0;color:var(--lf-color-text,#e0e0dd);
  }
  .home-academy-content>.home-academy-groups {color:var(--lf-color-accent,#f4c050)!important;font-size:12px!important;margin-top:10px!important;}
  .home-academy-link {
    flex:none;min-height:48px;display:inline-flex;align-items:center;gap:12px;
    justify-content:center;border:1px solid var(--lf-color-accent,#ffc749);border-radius:3px;
    background:linear-gradient(135deg,var(--lf-color-accent,#ffcd52),var(--lf-color-accent,#efad13));color:var(--lf-color-bg,#080909);
    font:800 15px 'Fira Sans Extra Condensed',sans-serif;
    padding:12px 20px;text-decoration:none;
  }
  .home-academy-link:hover{filter:brightness(1.08)}
  .home-academy-link:focus-visible{outline:3px solid var(--lf-color-text,#ffffff);outline-offset:3px}
  @media(max-width:760px){
    .home-academy-spotlight{min-height:260px;align-items:flex-start;flex-direction:column;gap:18px;padding:26px 19px;}
    .home-academy-image{background-position:70% center;}
    .home-academy-link{width:100%;max-width:320px;}
  }
</style>
