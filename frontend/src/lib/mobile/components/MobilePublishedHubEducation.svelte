<script lang="ts">
  import {onMount} from 'svelte';
  import type {MobileAuthController} from '../controller.ts';
  import type {PublishedHubArticle} from '../published-education.ts';
  import './mobile-theme.css';

  let {controller,viewerRole}:{controller:MobileAuthController,
    viewerRole:'parent'|'player'}=$props();
  let items=$state<PublishedHubArticle[]>([]);
  let organization=$state<'lefteria_fc'|'lefteria_fc_academy'>('lefteria_fc_academy');
  let busy=$state(true);
  let error=$state<string|null>(null);
  let nextOffset=$state<number|null>(null);
  let alive=false,request=0;

  onMount(()=>{
    alive=true;void refresh();
    return()=>{alive=false;request++;items=[];};
  });
  async function refresh(){
    const current=++request;
    busy=true;error=null;items=[];nextOffset=null;
    try{
      const page=await controller.getPublishedHubEducation(organization,0);
      if(!alive||current!==request||page.audience!==viewerRole)return;
      items=page.items;
      nextOffset=page.items.length===20?20:null;
    }catch{
      if(alive&&current===request)error='Δεν ήταν δυνατή η ασφαλής φόρτωση των επίσημων άρθρων.';
    }finally{if(alive&&current===request)busy=false;}
  }
  async function loadMore(){
    if(busy||nextOffset===null)return;
    const current=++request,offset=nextOffset;
    busy=true;error=null;
    try{
      const page=await controller.getPublishedHubEducation(organization,offset);
      if(!alive||current!==request||page.audience!==viewerRole)return;
      const previous=new Set(items.map(item=>item.id));
      items=[...items,...page.items.filter(item=>!previous.has(item.id))];
      nextOffset=page.items.length===20&&offset<980?offset+20:null;
    }catch{
      if(alive&&current===request)error='Δεν φορτώθηκαν τα επόμενα επίσημα άρθρα.';
    }finally{if(alive&&current===request)busy=false;}
  }
</script>
<section class="published" data-testid="hub-approved-education" aria-labelledby="hub-published-title">
  <header class="heading">
    <div>
      <p class="eyebrow">ΛΕΥΤΕΡΙΑ FC · ΕΓΚΕΚΡΙΜΕΝΟ ΥΛΙΚΟ</p>
      <h2 id="hub-published-title">Επίσημο εκπαιδευτικό περιεχόμενο</h2>
    </div>
    <button type="button" class="action" disabled={busy} onclick={()=>void refresh()}>Ανανέωση</button>
  </header>
  <p class="note">Εδώ εμφανίζονται μόνο άρθρα που έχουν ελεγχθεί και δημοσιευθεί από εξουσιοδοτημένους υπεύθυνους της Ακαδημίας ή του συλλόγου.</p>
  <div class="filters" role="group" aria-label="Οργανισμός περιεχομένου">
    <button type="button" class:chosen={organization==='lefteria_fc_academy'}
      onclick={()=>{organization='lefteria_fc_academy';void refresh();}}>Ακαδημία</button>
    <button type="button" class:chosen={organization==='lefteria_fc'}
      onclick={()=>{organization='lefteria_fc';void refresh();}}>Lefteria FC</button>
  </div>
  {#if busy&&items.length===0}
    <p role="status" class="note">Φόρτωση εγκεκριμένου περιεχομένου…</p>
  {:else if !busy&&items.length===0&&!error}
    <p role="status" class="note">Δεν υπάρχουν ακόμα δημοσιευμένα άρθρα για αυτή την ενότητα.</p>
  {/if}
  {#if items.length}
    <ol class="articles">
      {#each items as item (item.id)}
        <li class="article">
          <div class="meta">
            <span>{viewerRole==='parent'?'ΓΙΑ ΓΟΝΕΙΣ':'ΓΙΑ ΑΘΛΗΤΕΣ'}</span>
            <time datetime={item.published_at}>{new Date(item.published_at).toLocaleDateString('el-CY')}</time>
          </div>
          <h3>{item.title}</h3>
          <p class="body">{item.body}</p>
        </li>
      {/each}
    </ol>
    {#if nextOffset!==null}
      <button type="button" class="action more" disabled={busy}
        onclick={()=>void loadMore()}>Περισσότερα άρθρα</button>
    {/if}
  {/if}
  {#if error}<p class="failure" role="alert">{error}</p>{/if}
</section>
<style>
  .published{padding:20px 18px;margin-bottom:18px;border-radius:14px;background:var(--lf-surface);color:var(--lf-text);border:1px solid var(--lf-line)}
  .heading{display:flex;justify-content:space-between;gap:14px;align-items:start;flex-wrap:wrap}
  .eyebrow{color:var(--lf-gold);font-size:10px;font-weight:700;letter-spacing:.13em}
  h2{font-family:var(--lf-heading);font-size:clamp(23px,5vw,30px);margin:0}
  .note{color:var(--lf-muted);font-size:13px;line-height:1.7}
  .filters{display:flex;gap:8px;flex-wrap:wrap;margin:16px 0}
  .filters button,.action{min-height:44px;padding:10px 14px;background:var(--lf-elevated);color:var(--lf-gold-light);border:1px solid var(--lf-brand-edge);border-radius:9px;cursor:pointer}
  .filters button.chosen{outline:2px solid var(--lf-gold)}
  button:focus-visible{outline:2px solid var(--lf-gold);outline-offset:3px}
  button:disabled{opacity:.5}
  .articles{list-style:none;padding:0;display:grid;gap:12px}
  .article{padding:18px;border-radius:12px;background:var(--lf-elevated);border:1px solid var(--lf-line)}
  .article h3{font-size:20px;margin:12px 0}
  .meta{display:flex;justify-content:space-between;gap:10px;flex-wrap:wrap;color:var(--lf-gold);font-size:11px}
  .body{white-space:pre-wrap;overflow-wrap:anywhere;font-size:14px;line-height:1.7}
  .more{width:100%}
  .failure{color:#ffcdbe;border:1px solid #7b3e35;border-radius:8px;padding:13px}
</style>
