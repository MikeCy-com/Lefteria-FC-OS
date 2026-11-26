<script lang="ts">
  import {onMount} from 'svelte';
  import type {MobileAuthController} from '../controller.ts';
  import type {MobileRole} from '../navigation.ts';
  import {canOpenPrivateChat} from '../invitation-actions.ts';
  let {controller,role,onOpen}:{
    controller:MobileAuthController;role:MobileRole;
    onOpen:()=>void|Promise<void>;
  }=$props();
  let count=$state<number|null>(null);
  let status=$state<'loading'|'ready'|'error'>('loading');
  let alive=false,request=0;
  async function refresh(){
    if(!alive)return;
    const marker=++request;
    count=null;status='loading';
    try{
      const result=role==='fan'?await controller.getPendingFamilyInvitationCount():
        canOpenPrivateChat(role)?await controller.getPendingPrivateInvitationCount():0;
      if(!alive||marker!==request)return;
      if(!Number.isSafeInteger(result)||result<0||result>(role==='fan'?1:100))
        throw new Error('Invalid invitation count.');
      count=result;status='ready';
    }catch{if(alive&&marker===request){count=null;status='error';}}
  }
  onMount(()=>{
    alive=true;void refresh();
    function visibility(){
      if(document.visibilityState==='hidden'){request++;count=null;status='loading';}
      else void refresh();
    }
    document.addEventListener('visibilitychange',visibility);
    return()=>{alive=false;request++;document.removeEventListener('visibilitychange',visibility);};
  });
</script>
{#if role==='fan'||canOpenPrivateChat(role)}
  <section class="invitation-actions" aria-label="Εκκρεμείς προσκλήσεις" data-testid="mobile-invitation-actions">
    <header>
      <h2>{role==='fan'?'Οικογενειακές προσκλήσεις':'Προσκλήσεις συνομιλίας'}</h2>
      <button type="button" disabled={status==='loading'} onclick={()=>void refresh()}>Ανανέωση</button>
    </header>
    {#if status==='loading'}<p role="status">Έλεγχος προσκλήσεων…</p>
    {:else if status==='error'}<p role="alert">Οι προσκλήσεις δεν επαληθεύτηκαν.</p>
    {:else}<p><strong>{count}</strong> {count===1?'εκκρεμής πρόσκληση':'εκκρεμείς προσκλήσεις'} προς αποδοχή ή απόρριψη.</p>{/if}
    <button class="destination" type="button" onclick={()=>void onOpen()}>
      {role==='fan'?'Οικογενειακές συνδέσεις στο προφίλ':'Ιδιωτικές συνομιλίες'} <span aria-hidden="true">›</span>
    </button>
    <p class="hint">Δεν προσμετρώνται στις μη αναγνωσμένες ανακοινώσεις ή μηνύματα.</p>
  </section>
{/if}
<style>
  .invitation-actions{margin:12px 14px;padding:14px;border:1px solid var(--lf-line);border-radius:12px;background:var(--lf-surface);color:var(--lf-text);font-family:var(--lf-body)}
  header{display:flex;justify-content:space-between;align-items:center;flex-wrap:wrap;gap:8px}
  h2{font-family:var(--lf-heading);font-size:19px;margin:0}p{font-size:14px;line-height:1.5}
  strong{color:var(--lf-gold-light);font-variant-numeric:tabular-nums}
  button{min-height:44px;padding:8px 10px;border:1px solid var(--lf-brand-edge);background:var(--lf-elevated);color:var(--lf-gold-light);border-radius:9px;cursor:pointer}
  button:disabled{opacity:.5;cursor:default}button:focus-visible{outline:2px solid var(--lf-gold);outline-offset:2px}
  .destination{width:100%;display:flex;justify-content:space-between;align-items:center;gap:8px;text-align:left}
  .hint{font-size:12px;color:var(--lf-muted);margin-bottom:0}
</style>
