<script lang="ts">
  import {onMount} from 'svelte';
  import type {MobileAuthController} from '../controller.ts';
  import type {ActivityFilter,NotificationActivity} from '../notification-activity.ts';
  let {controller,privateAllowed,onOpen,onRefresh}:{controller:MobileAuthController;
    privateAllowed:boolean;onOpen:(item:NotificationActivity)=>Promise<void>;onRefresh:()=>void}=$props();
  const filters:{id:ActivityFilter;label:string}[]=[
    {id:'all',label:'Όλα'},{id:'announcement',label:'Ανακοινώσεις'},
    {id:'group_message',label:'Ομάδες'},{id:'private_message',label:'Ιδιωτικά'}];
  let filter=$state<ActivityFilter>('all'),items=$state<NotificationActivity[]>([]);
  let offset=$state(0),next=$state<number|null>(null),busy=$state(true),error=$state(false);
  let alive=false,request=0;
  async function load(kind:ActivityFilter,page:number){
    if(!alive||kind==='private_message'&&!privateAllowed)return;
    const marker=++request;
    filter=kind;offset=page;items=[];next=null;busy=true;error=false;
    try{
      const result=await controller.getNotificationActivity(kind,page);
      if(!alive||marker!==request)return;
      items=result.items;next=result.next_offset;
      onRefresh();
    }catch{
      if(alive&&marker===request){items=[];next=null;error=true;onRefresh();}
    }finally{if(alive&&marker===request)busy=false;}
  }
  onMount(()=>{
    alive=true;
    function visibility(){
      if(document.visibilityState==='hidden'){request++;items=[];next=null;busy=true;}
      else void load(filter,0);
    }
    visibility();
    document.addEventListener('visibilitychange',visibility);
    return()=>{alive=false;request++;document.removeEventListener('visibilitychange',visibility);};
  });
  function label(item:NotificationActivity){
    return item.kind==='announcement'?'Ανακοίνωση':item.kind==='group_message'?'Ομαδικό μήνυμα':'Ιδιωτικό μήνυμα';
  }
</script>
<section class="activity" aria-labelledby="activity-title" data-testid="mobile-notification-activity">
  <header><h2 id="activity-title">Μη αναγνωσμένες ενημερώσεις</h2>
    <button type="button" disabled={busy} onclick={()=>void load(filter,0)}>Ανανέωση</button>
  </header>
  <div class="filters" role="group" aria-label="Φίλτρο ενημερώσεων">
    {#each filters.filter(f=>privateAllowed||f.id!=='private_message') as f (f.id)}
      <button type="button" class:active={filter===f.id} aria-pressed={filter===f.id}
        onclick={()=>void load(f.id,0)}>{f.label}</button>
    {/each}
  </div>
  <p class="hint">Οι ενημερώσεις ανοίγουν την αντίστοιχη ανακοίνωση ή συνομιλία. Η λίστα δεν καταγράφει ανάγνωση.</p>
  {#if busy}<p role="status">Έλεγχος ενημερώσεων…</p>
  {:else if error}<p role="alert">Δεν ήταν δυνατή η επαλήθευση των ενημερώσεων.</p>
  {:else if !items.length}<p role="status">Δεν υπάρχουν μη αναγνωσμένες ενημερώσεις σε αυτή τη σελίδα.</p>
  {:else}
    <ol>{#each items as item (item.kind+':'+item.entity_id)}
      <li><button type="button" class="entry" onclick={()=>void onOpen(item)}>
        <span class="category">{label(item)}</span>
        <strong>{item.title??(item.kind==='private_message'?'Νέο ιδιωτικό μήνυμα':'Νέο μήνυμα ομάδας')}</strong>
        {#if item.group_name}<span>{item.group_name}</span>{/if}
        <time datetime={item.occurred_at}>{new Date(item.occurred_at).toLocaleString('el-CY',{dateStyle:'medium',timeStyle:'short'})}</time>
        <span class="category">Άνοιγμα ›</span>
      </button></li>
    {/each}</ol>
  {/if}
  {#if !busy&&!error&&(offset>0||next!==null)}
    <nav aria-label="Σελίδες ενημερώσεων">
      <button type="button" disabled={offset===0} onclick={()=>void load(filter,Math.max(0,offset-30))}>Προηγούμενη</button>
      <span>Σελίδα {Math.floor(offset/30)+1}</span>
      <button type="button" disabled={next===null} onclick={()=>{if(next!==null)void load(filter,next);}}>Επόμενη</button>
    </nav>
  {/if}
</section>
<style>
  .activity{margin:12px 14px;padding:14px;border:1px solid var(--lf-line);border-radius:12px;background:var(--lf-surface);color:var(--lf-text);font-family:var(--lf-body)}
  header,nav{display:flex;justify-content:space-between;align-items:center;gap:8px;flex-wrap:wrap}
  h2{font-family:var(--lf-heading);font-size:19px;margin:0}p{font-size:14px;line-height:1.5}
  .filters{display:flex;flex-wrap:wrap;gap:6px;margin-top:12px}
  button{min-height:44px;padding:8px 10px;border:1px solid var(--lf-brand-edge);background:var(--lf-elevated);color:var(--lf-gold-light);border-radius:9px;cursor:pointer}
  button.active{background:var(--lf-gold);color:#151515}button:disabled{opacity:.5;cursor:default}
  button:focus-visible{outline:2px solid var(--lf-gold);outline-offset:2px}
  ol{list-style:none;margin:12px 0;padding:0;display:grid;gap:8px}
  .entry{display:grid;gap:5px;width:100%;text-align:left;border-color:var(--lf-line);color:var(--lf-text)}
  .category{color:var(--lf-gold-light);font-size:12px}time,.hint{color:var(--lf-muted);font-size:12px}
</style>
