<script lang="ts">
  import PublicImage from '../public/PublicImage.svelte';
  import type { PublicOrganization } from '../public/types.ts';
  import { collectPublishedMedia, type PublishedMediaAsset, type MediaSource } from './published-media.ts';
  let { organization, onPick, embedded=false }: {
    organization:PublicOrganization;onPick?:(asset:PublishedMediaAsset)=>void;embedded?:boolean
  }=$props();
  const categories=[
    {value:'all',label:'Όλες'}, {value:'gallery',label:'Φωτογραφίες'},
    {value:'sponsor',label:'Χορηγοί'}, {value:'partner',label:'Συνεργάτες'},
    {value:'event',label:'Εκδηλώσεις'}
  ] as const;
  let assets=$state<PublishedMediaAsset[]>([]);
  let loading=$state(false);
  let problem=$state('');
  let notice=$state('');
  let query=$state('');
  let category=$state<'all'|MediaSource>('all');
  const filtered=$derived(assets.filter(asset=>
    (category==='all'||asset.source===category)&&
    (!query.trim()||(asset.label+' '+asset.source).toLocaleLowerCase('el-GR')
      .includes(query.trim().toLocaleLowerCase('el-GR')))));
  $effect(()=>{
    const owner=organization,controller=new AbortController();
    loading=true;assets=[];problem='';notice='';
    void(async()=>{
      try{
        const response=await fetch('/api/public/sections?organization_id='+owner,{
          credentials:'omit',cache:'no-store',headers:{Accept:'application/json'},
          signal:controller.signal
        });
        if(!response.ok)throw Error('Το δημοσιευμένο υλικό δεν είναι διαθέσιμο.');
        const data:unknown=await response.json();
        const found=collectPublishedMedia(data,owner);
        if(found===null)throw Error('Η συλλογή δεν επαληθεύτηκε.');
        if(!controller.signal.aborted)assets=found;
      }catch(error){
        if(!controller.signal.aborted)problem=error instanceof Error?error.message:'Αποτυχία φόρτωσης εικόνων.';
      }finally{if(!controller.signal.aborted)loading=false;}
    })();
    return()=>controller.abort();
  });
  async function copyUrl(asset:PublishedMediaAsset){
    if(asset.organization_id!==organization)return;
    try{await navigator.clipboard.writeText(asset.url);notice='Η διεύθυνση εικόνας αντιγράφηκε.';}
    catch{notice='Δεν ήταν δυνατή η αντιγραφή της διεύθυνσης.';}
  }
</script>

<section class:embedded class="published-media" data-testid="published-media-library"
  aria-label="Βιβλιοθήκη δημοσιευμένων εικόνων">
  <div class="published-media-filters">
    <label>Αναζήτηση εικόνας <input type="search" placeholder="Αναζήτηση τίτλου…" bind:value={query}/></label>
    <label>Πηγή εικόνων <select bind:value={category}>
      {#each categories as item (item.value)}<option value={item.value}>{item.label}</option>{/each}
    </select></label>
  </div>
  {#if loading}
    <p role="status">Φόρτωση εγκεκριμένων εικόνων…</p>
  {:else if problem}
    <p role="alert" class="media-error">{problem} Δεν παρουσιάζεται ψεύτικη κενή συλλογή.</p>
  {:else}
    <p class="media-count" role="status">{filtered.length} από {assets.length} ήδη δημοσιευμένες εικόνες · {organization==='lefteria_fc'?'Πρώτη ομάδα':'Ακαδημία'}</p>
    {#if !filtered.length}
      <p class="media-empty">Δεν βρέθηκαν εικόνες. Στον Visual Builder μπορείτε επίσης να προσθέσετε δικό σας ασφαλές URL εικόνας.</p>
    {:else}
      <div class="published-media-grid">
        {#each filtered as asset (asset.id)}
          <article class="published-media-card" data-testid="published-media-asset" data-source={asset.source}>
            <div class="published-media-image"><PublicImage src={asset.url} alt={asset.alt}/></div>
            <div class="published-media-copy">
              <p class="media-source">{categories.find(item=>item.value===asset.source)?.label??'Δημοσιευμένο'}</p>
              <h3>{asset.label}</h3>
              <div class="published-media-actions">
                {#if onPick}<button type="button" class="media-select"
                  onclick={()=>{if(asset.organization_id===organization)onPick(asset);}}>Χρήση εικόνας</button>{/if}
                <button type="button" onclick={()=>void copyUrl(asset)}>Αντιγραφή URL</button>
              </div>
            </div>
          </article>
        {/each}
      </div>
    {/if}
  {/if}
  {#if notice}<p role="status" class="media-notice">{notice}</p>{/if}
</section>
<style>
.published-media{display:grid;gap:14px;min-width:0}
.published-media-filters{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px}
.published-media label{display:grid;gap:7px;font-size:12px;font-weight:750;color:#eee}
.published-media :is(input,select){width:100%;min-width:0;border:1px solid #62615b;background:#111516;color:#fff;padding:10px 12px;border-radius:5px;font:400 13px/1.45 'Fira Sans',sans-serif}
.published-media-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(min(185px,100%),1fr));gap:14px}
.published-media-card{min-width:0;overflow:hidden;background:#171a1c;border:1px solid #4f4c43;border-radius:9px;display:flex;flex-direction:column}
.published-media-image{aspect-ratio:1.4;background:linear-gradient(135deg,#24282a,#0e1213);display:grid;place-items:center;overflow:hidden}
.published-media-image :global(img){width:100%;height:100%;object-fit:contain;padding:11px}
.published-media-copy{display:grid;align-content:space-between;gap:9px;padding:13px;flex:1}
.published-media-copy h3{font:750 14px/1.35 'Fira Sans',sans-serif;color:#fff;margin:0;overflow-wrap:anywhere}
.media-source{color:#efca79;font-size:11px;font-weight:800;margin:0}
.published-media-actions{display:flex;gap:8px;flex-wrap:wrap;margin-top:6px}
.published-media-actions button{font:750 12px/1.3 'Fira Sans',sans-serif;min-height:39px;border:1px solid #777266;border-radius:5px;background:#292d2d;color:#fff;padding:8px 10px;cursor:pointer}
.published-media-actions button.media-select{background:#f4be47;border-color:#f4be47;color:#171a1b}
.published-media :is(button,input,select):focus-visible{outline:3px solid #f8ce74;outline-offset:2px}
.media-error{color:#ffa3a3}.media-notice{color:#acdfb1}.media-count,.media-empty{font-size:13px;color:#c6c7c2;margin:0}
.embedded{border-top:1px solid #62573c;padding-top:14px}
.embedded .published-media-grid{grid-template-columns:repeat(auto-fill,minmax(min(150px,100%),1fr))}
@media(max-width:640px){.published-media-filters{grid-template-columns:1fr}.published-media-grid{grid-template-columns:repeat(2,minmax(0,1fr));gap:10px}}
@media(max-width:400px){.published-media-grid{grid-template-columns:1fr}}
</style>
