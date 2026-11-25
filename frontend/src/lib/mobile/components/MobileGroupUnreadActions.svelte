<script lang="ts">
  import {onMount} from 'svelte';
  import type {MobileAuthController} from '../controller.ts';
  import type {GroupChatUnreadSummary} from '../group-chat.ts';
  let {controller,onOpen}:{controller:MobileAuthController;onOpen:()=>void|Promise<void>}=$props();
  let summary=$state<GroupChatUnreadSummary|null>(null);
  let status=$state<'loading'|'ready'|'error'>('loading');
  let alive=false,request=0;
  async function refresh(){
    if(!alive)return;
    const marker=++request;
    status='loading';summary=null;
    try{
      const result=await controller.getGroupChatUnreadSummary();
      if(!alive||marker!==request)return;
      summary=result;status='ready';
    }catch{if(alive&&marker===request){summary=null;status='error';}}
  }
  onMount(()=>{
    alive=true;void refresh();
    function visibility(){
      if(document.visibilityState==='hidden'){request++;summary=null;status='loading';}
      else void refresh();
    }
    document.addEventListener('visibilitychange',visibility);
    return()=>{alive=false;request++;document.removeEventListener('visibilitychange',visibility);};
  });
</script>
<section class="unread-actions" aria-label="Ομαδικά μηνύματα" data-testid="mobile-group-unread-actions">
  <header><h2>Ομαδικά μηνύματα</h2>
    <button type="button" disabled={status==='loading'} onclick={()=>void refresh()}>Ανανέωση</button></header>
  {#if status==='loading'}<p role="status">Έλεγχος μηνυμάτων…</p>
  {:else if status==='error'}<p role="alert">Τα μη αναγνωσμένα μηνύματα δεν επαληθεύτηκαν.</p>
  {:else if summary?.enabled}
    <p><strong>{summary.unread_count===999?'999+':summary.unread_count}</strong> μη αναγνωσμένα ομαδικά μηνύματα</p>
  {:else}<p>Οι ειδοποιήσεις μηνυμάτων είναι απενεργοποιημένες στις προτιμήσεις σου.</p>{/if}
  <button type="button" class="destination" onclick={()=>void onOpen()}>Άνοιγμα ομαδικών μηνυμάτων <span aria-hidden="true">›</span></button>
  <p class="hint">Μόνο μηνύματα ενεργών ομάδων. Οι ιδιωτικές συνομιλίες δεν περιλαμβάνονται.</p>
</section>
<style>
  .unread-actions{margin:12px 14px;padding:14px;border:1px solid var(--lf-line);border-radius:12px;background:var(--lf-surface);color:var(--lf-text);font-family:var(--lf-body)}
  header{display:flex;justify-content:space-between;align-items:center;gap:8px;flex-wrap:wrap}h2{font-family:var(--lf-heading);font-size:19px;margin:0}
  p{font-size:14px;line-height:1.5}strong{color:var(--lf-gold-light);font-variant-numeric:tabular-nums}
  button{min-height:44px;padding:8px 10px;border:1px solid var(--lf-brand-edge);background:var(--lf-elevated);color:var(--lf-gold-light);border-radius:9px;cursor:pointer}
  button:disabled{opacity:.5;cursor:default}button:focus-visible{outline:2px solid var(--lf-gold);outline-offset:2px}
  .destination{width:100%;display:flex;justify-content:space-between;gap:8px;text-align:left}.hint{font-size:12px;color:var(--lf-muted);margin-bottom:0}
</style>
