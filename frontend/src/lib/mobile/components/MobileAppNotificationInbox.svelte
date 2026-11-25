<script lang="ts">
  import {onMount} from 'svelte';
  import MobileWebPushControls from './MobileWebPushControls.svelte';
  import type {MobileAuthController} from '../controller.ts';
  import type {AppNotification} from '../notification-inbox.ts';

  let {controller,onOpen,scopeKey,onUnreadChange}:{
    controller:MobileAuthController;
    onOpen:(notice:AppNotification)=>Promise<void>;
    scopeKey:string|null;
    onUnreadChange?:(scope:string|null,count:number)=>void;
  }=$props();
  let items=$state<AppNotification[]>([]);
  let nextOffset=$state<number|null>(null);
  let enabled=$state(true);
  let busy=$state(true);
  let error=$state<string|null>(null);
  let previousOffsets=$state<number[]>([]);
  let currentOffset=$state(0);
  let request=0,alive=false;

  onMount(()=>{
    alive=true;void refresh();
    function refreshVisiblePage(){
      if(document.visibilityState==='visible'&&!busy)
        void loadPage(currentOffset,[...previousOffsets]);
    }
    document.addEventListener('visibilitychange',refreshVisiblePage);
    window.addEventListener('focus',refreshVisiblePage);
    return()=>{
      alive=false;request++;
      document.removeEventListener('visibilitychange',refreshVisiblePage);
      window.removeEventListener('focus',refreshVisiblePage);
    };
  });
  async function loadPage(offset:number,history:number[]){
    const marker=++request,scope=scopeKey;
    // Each page is a fresh server-authorized view. Never append private
    // rows from earlier pages whose audience access may have changed.
    busy=true;error=null;items=[];nextOffset=null;enabled=true;
    try{
      const page=await controller.getAppNotificationInbox(offset);
      if(!alive||marker!==request||scope!==scopeKey)return;
      if(page.next_offset!==null&&page.next_offset<=offset)
        throw new Error('Non-advancing notification page.');
      enabled=page.enabled;
      items=page.enabled?page.notifications:[];
      nextOffset=page.enabled&&page.next_offset!==null&&page.next_offset<=100000
        ?page.next_offset:null;
      previousOffsets=page.enabled?history:[];
      currentOffset=page.enabled?offset:0;
      onUnreadChange?.(scope,page.unread_count);
    }catch{
      if(alive&&marker===request&&scope===scopeKey){
        items=[];nextOffset=null;previousOffsets=[];currentOffset=0;
        error='Δεν ήταν δυνατή η επαλήθευση των ειδοποιήσεων. Δοκίμασε ξανά.';
        onUnreadChange?.(scope,0);
      }
    }finally{if(alive&&marker===request)busy=false;}
  }
  function refresh(){return loadPage(0,[]);}
  function loadMore(){
    if(nextOffset===null||busy)return;
    return loadPage(nextOffset,[...previousOffsets,currentOffset]);
  }
  function loadPrevious(){
    if(busy||!previousOffsets.length)return;
    return loadPage(previousOffsets[previousOffsets.length-1]!,previousOffsets.slice(0,-1));
  }
</script>

<section class="notification-layout" aria-labelledby="app-notifications-title"
  data-testid="mobile-app-notification-inbox">
  <header class="header">
    <div>
      <p class="eyebrow">LEFTERIA FC · APP</p>
      <h1 id="app-notifications-title">Ειδοποιήσεις</h1>
    </div>
    <button type="button" class="outline" disabled={busy}
      onclick={()=>void refresh()}>Ανανέωση</button>
  </header>
  <MobileWebPushControls {controller}/>
  <p class="hint">Ανακοινώσεις για τα τμήματα στα οποία έχεις ενεργή πρόσβαση.
    Η προβολή αυτής της λίστας δεν σημειώνει τις ανακοινώσεις ως διαβασμένες.</p>
  {#if busy&&items.length===0}<p role="status">Φόρτωση ειδοποιήσεων…</p>{/if}
  {#if !enabled}
    <p class="notice" role="status">Οι ενημερώσεις συλλόγου είναι απενεργοποιημένες στις
      προτιμήσεις του προφίλ σου. Μπορείς να διαβάζεις τις ανακοινώσεις από την
      αντίστοιχη καρτέλα.</p>
  {:else if !busy&&items.length===0&&!error}
    <p class="notice" role="status">Δεν υπάρχουν ανακοινώσεις σε αυτή τη σελίδα.</p>
  {/if}
  {#if items.length}
    <ol class="items">
      {#each items as notice (notice.announcement_id)}
        <li class:unread={!notice.seen}>
          <button type="button" class="notification"
            onclick={()=>void onOpen(notice)} aria-label={'Άνοιγμα ανακοίνωσης: '+notice.title}>
            <span class="top">
              <span class="group">{notice.group_name}</span>
              {#if !notice.seen}<span class="unread-label">Μη αναγνωσμένη</span>
              {:else}<span class="seen-label">Διαβάστηκε</span>{/if}
            </span>
            <strong>{notice.title}</strong>
            <time datetime={notice.published_at}>
              {new Date(notice.published_at).toLocaleString('el-CY',
                {dateStyle:'medium',timeStyle:'short'})}
            </time>
            <span class="open-link">Προβολή ανακοίνωσης</span>
          </button>
        </li>
      {/each}
    </ol>
  {/if}
  {#if enabled&&!error&&!busy&&(previousOffsets.length||nextOffset!==null)}
    <nav class="pagination" aria-label="Σελίδες ειδοποιήσεων">
      <button type="button" class="outline" disabled={!previousOffsets.length}
        onclick={()=>void loadPrevious()}>Προηγούμενη</button>
      <span aria-live="polite">Σελίδα {previousOffsets.length+1}</span>
      <button type="button" class="outline" disabled={nextOffset===null}
        onclick={()=>void loadMore()}>Επόμενη</button>
    </nav>
  {/if}
  {#if error}
    <div class="error" role="alert">{error}
      <button type="button" class="outline retry" onclick={()=>void refresh()}>Δοκίμασε ξανά</button>
    </div>
  {/if}
</section>
<style>
  .notification-layout{padding:16px 14px 28px;color:var(--lf-text);
    font-family:var(--lf-body);font-size:var(--lf-type-body)}
  .header{display:flex;justify-content:space-between;align-items:center;gap:12px}
  .eyebrow{font-size:10px;font-weight:700;letter-spacing:.15em;color:var(--lf-gold);margin:0 0 5px}
  h1{font-size:28px;line-height:1.15;margin:0;font-family:var(--lf-heading)}
  .hint,.notice{font-size:12px;color:var(--lf-muted);line-height:1.55;margin:12px 0 17px}
  .outline{min-height:44px;border:1px solid var(--lf-brand-edge);
    background:transparent;color:var(--lf-gold-light);padding:8px 12px;border-radius:9px}
  .items{list-style:none;margin:0;padding:0;display:grid;gap:10px}
  .items li{border:1px solid var(--lf-line);border-radius:12px;background:var(--lf-surface)}
  .items li.unread{border-color:var(--lf-gold);background:var(--lf-elevated)}
  .notification{display:grid;text-align:left;gap:8px;width:100%;padding:14px;
    background:none;color:var(--lf-text);border:0;cursor:pointer;min-height:95px}
  .top{display:flex;gap:8px;justify-content:space-between;align-items:center;flex-wrap:wrap}
  .group{color:var(--lf-gold-light);font-size:11px;font-weight:700}
  .unread-label,.seen-label{font-size:10px;color:var(--lf-gold)}
  .seen-label{color:var(--lf-muted)}
  strong{font-family:var(--lf-heading);font-size:19px}
  time{font-size:11px;color:var(--lf-muted)}
  .open-link{font-size:12px;color:var(--lf-gold-light);text-decoration:underline}
  .pagination{display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:8px;margin-top:16px}
  .pagination span{font-size:12px;color:var(--lf-muted)}
  .retry{display:block;margin-top:10px}
  .error{padding:12px;border:1px solid #7b3e35;background:#2a1b1a;
    color:#ffcdbe;border-radius:8px;font-size:12px}
  button:disabled{opacity:.5;cursor:default}
  button:focus-visible{outline:2px solid var(--lf-gold);outline-offset:2px}
</style>
