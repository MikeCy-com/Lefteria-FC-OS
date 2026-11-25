<script lang="ts">
  import {onMount,tick} from 'svelte';
  import type {MobileAuthController} from '../controller.ts';
  import type {GroupChatRoom} from '../group-chat.ts';
  import type {AppAnnouncement} from '../app-announcements.ts';
  import {CHAT_REACTION_EMOJIS,type ChatReaction} from '../chat-actions.ts';
  import MobileAnnouncementBanner from './MobileAnnouncementBanner.svelte';
  import type {AppAnnouncementMedia} from '../app-announcements.ts';

  let {controller,initialOrganization=null,initialGroup=null,
    initialAnnouncementId=null}:{
      controller:MobileAuthController;
      initialOrganization?:string|null;initialGroup?:string|null;
      initialAnnouncementId?:string|null;
    }=$props();
  let rooms=$state.raw<GroupChatRoom[]>([]);
  let selected=$state.raw<GroupChatRoom|null>(null);
  let announcements=$state<AppAnnouncement[]>([]);
  let nextOffset=$state<number|null>(null);
  let previousOffsets=$state<number[]>([]),currentOffset=$state(0);
  let targetMessage=$state<string|null>(null);
  let downloading=$state<string|null>(null);
  let loading=$state(true),busy=$state(false);
  let error=$state<string|null>(null);
  let reactionFor=$state<string|null>(null);
  let reacting=$state<string|null>(null);
  let requestNumber=0,alive=true;
  const acknowledged=new Set<string>();
  const acknowledgmentsInFlight=new Set<string>();

  onMount(()=>{
    alive=true;void loadDirectory();
    return()=>{alive=false;requestNumber++;};
  });
  function fault(error:unknown){
    return error instanceof Error&&error.message.length<=240
      ?error.message:'Η υπηρεσία ανακοινώσεων δεν είναι διαθέσιμη.';
  }
  async function loadDirectory(){
    const request=++requestNumber;
    loading=true;busy=false;error=null;rooms=[];selected=null;announcements=[];nextOffset=null;
    reactionFor=null;reacting=null;downloading=null;targetMessage=null;
    previousOffsets=[];currentOffset=0;
    try{
      const result=await controller.getChatRooms();
      if(!alive||request!==requestNumber)return;
      rooms=result;
      // Finish directory loading before starting a separate feed request.
      loading=false;
      const requested=result.find(room=>
        room.organization_id===initialOrganization&&room.group_id===initialGroup);
      if(requested)void chooseRoom(requested);
      else if(initialAnnouncementId||initialOrganization||initialGroup)
        targetMessage='Η ζητούμενη ανακοίνωση ή το τμήμα δεν είναι διαθέσιμο. Επίλεξε ένα από τα διαθέσιμα τμήματα.';
      else if(result.length===1)void chooseRoom(result[0]);
    }catch(e){if(alive&&request===requestNumber)error=fault(e);}
    finally{if(alive&&request===requestNumber)loading=false;}
  }
  async function chooseRoom(room:GroupChatRoom){
    if(busy||reacting!==null||downloading!==null)return;
    // Raw directory/selection state preserves reference identity. Only a
    // room from the latest authorized directory may initiate a feed request.
    if(!rooms.includes(room))return;
    selected=room;
    return loadFeed(room,0,[],true);
  }
  async function loadFeed(room:GroupChatRoom,offset:number,history:number[],findTarget=false){
    const request=++requestNumber;
    // Do not retain earlier private rows across fresh access checks.
    announcements=[];nextOffset=null;error=null;reactionFor=null;targetMessage=null;
    busy=true;
    const target=room.organization_id===initialOrganization&&room.group_id===initialGroup&&
      initialAnnouncementId&&/^ann_[a-f0-9]{32}$/.test(initialAnnouncementId)
      ?initialAnnouncementId:null;
    try{
      let cursor=offset,trail=[...history];
      // A notification may point to an older page. Limit automatic searching
      // to five authorized requests; never scan an unrelated group.
      for(let attempt=0;attempt<5;attempt++){
        const page=await controller.getAppAnnouncements(room,cursor);
        if(!alive||request!==requestNumber||selected!==room)return;
        if(page.next_offset!==null&&page.next_offset<=cursor)
          throw new Error('Η σελίδα ανακοινώσεων δεν είναι έγκυρη.');
        const next=page.next_offset!==null&&page.next_offset<=100000?page.next_offset:null;
        const found=target!==null&&page.announcements.some(item=>item.id===target);
        if(findTarget&&target&&!found&&next!==null&&attempt<4){
          trail=[...trail,cursor];cursor=next;continue;
        }
        announcements=page.announcements;nextOffset=next;
        previousOffsets=trail;currentOffset=cursor;
        if(target&&!found)
          targetMessage=next!==null
            ?'Η ζητούμενη ανακοίνωση δεν βρέθηκε σε αυτή τη σελίδα. Μπορείς να συνεχίσεις την αναζήτηση.'
            :'Η ζητούμενη ανακοίνωση δεν βρέθηκε. Μπορεί να μην είναι πλέον διαθέσιμη.';
        if(found){
          await tick();
          if(alive&&request===requestNumber&&selected===room)
            document.getElementById('app-announcement-'+target)
              ?.scrollIntoView({behavior:'smooth',block:'center'});
        }
        break;
      }
    }catch(e){
      if(alive&&request===requestNumber){
        announcements=[];nextOffset=null;previousOffsets=[];currentOffset=0;
        error=fault(e);
      }
    }finally{if(alive&&request===requestNumber)busy=false;}
  }
  async function loadMore(){
    const room=selected;
    if(!room||nextOffset===null||busy||reacting!==null||downloading!==null)return;
    return loadFeed(room,nextOffset,[...previousOffsets,currentOffset]);
  }
  async function loadPrevious(){
    const room=selected;
    if(!room||!previousOffsets.length||busy||reacting!==null||downloading!==null)return;
    return loadFeed(room,previousOffsets[previousOffsets.length-1]!,previousOffsets.slice(0,-1));
  }
  function seenOnScreen(node:HTMLElement,notice:AppAnnouncement) {
    const room=selected;
    if(!room)return;
    let visible=false;
    const key=room.organization_id+'/'+room.group_id+'/'+notice.id;
    function acknowledge(){
      if(!alive||!visible||document.visibilityState!=='visible'||
        !document.hasFocus()||selected!==room||
        acknowledged.has(key)||acknowledgmentsInFlight.has(key)||
        !announcements.some(item=>item.id===notice.id))return;
      acknowledgmentsInFlight.add(key);
      void controller.acknowledgeAppAnnouncementRead(room!,notice.id).then(confirmed=>{
        if(confirmed)acknowledged.add(key);
      }).catch(()=>{/* Unverified observations are never marked read. */})
        .finally(()=>acknowledgmentsInFlight.delete(key));
    }
    const observer=new IntersectionObserver(entries=>{
      visible=entries.some(entry=>entry.isIntersecting&&entry.intersectionRatio>=0.75);
      acknowledge();
    },{threshold:0.75});
    observer.observe(node);
    document.addEventListener('visibilitychange',acknowledge);
    window.addEventListener('focus',acknowledge);
    return {destroy(){
      observer.disconnect();
      document.removeEventListener('visibilitychange',acknowledge);
      window.removeEventListener('focus',acknowledge);
    }};
  }
  async function downloadFile(notice:AppAnnouncement,media:AppAnnouncementMedia){
    const room=selected,revision=requestNumber;
    if(!room||media.kind!=='attachment'||busy||reacting!==null||downloading||
      !announcements.some(item=>item.id===notice.id))return;
    downloading=media.id;error=null;
    try{
      const blob=await controller.getAppAnnouncementMedia(room,notice.id,media);
      if(!alive||requestNumber!==revision||selected!==room)return;
      const url=URL.createObjectURL(blob);
      try{
        const link=document.createElement('a');
        link.href=url;link.download=media.file_name;link.rel='noopener noreferrer';
        document.body.appendChild(link);
        link.click();link.remove();
      }finally{
        // A temporary in-memory URL is not a persistent or public media link.
        window.setTimeout(()=>URL.revokeObjectURL(url),30000);
      }
    }catch(e){if(alive&&requestNumber===revision)error=fault(e);}
    finally{if(alive&&requestNumber===revision)downloading=null;}
  }
  async function toggleReaction(notice:AppAnnouncement,emoji:ChatReaction){
    const room=selected;
    if(!room||busy||downloading!==null||reacting!==null||!announcements.some(a=>a.id===notice.id))return;
    const request=requestNumber;
    const next=!notice.reactions.some(r=>r.emoji===emoji&&r.mine);
    reacting=notice.id;error=null;
    try{
      const confirmation=await controller.setAppAnnouncementReaction(room,notice.id,emoji,next);
      if(!alive||request!==requestNumber||selected!==room)return;
      announcements=announcements.map(a=>a.id===notice.id
        ?{...a,reactions:confirmation.reactions}:a);
      reactionFor=null;
    }catch(e){if(alive&&request===requestNumber)error=fault(e);}
    finally{if(alive&&request===requestNumber)reacting=null;}
  }
</script>

<section class="announcements-layout" data-testid="mobile-app-announcements" aria-labelledby="app-announcements-title">
  <header class="heading">
    <div><p class="eyebrow">LEFTERIA FC · APP</p><h1 id="app-announcements-title">Ανακοινώσεις</h1></div>
    <button type="button" class="outline" disabled={busy||loading||reacting!==null||downloading!==null}
      onclick={()=>void loadDirectory()}>Ανανέωση</button>
  </header>
  <p class="hint">Επίσημες ενημερώσεις μόνο για τα τμήματα στα οποία έχεις ενεργή πρόσβαση. Οι ανακοινώσεις δημοσιεύονται από τη διοίκηση, όχι από τη συνομιλία.</p>
  {#if loading}<p role="status">Φόρτωση τμημάτων…</p>
  {:else if rooms.length===0&&!error}<p class="hint" role="status">Δεν υπάρχουν διαθέσιμα τμήματα.</p>
  {/if}
  {#if rooms.length}
    <nav class="groups" aria-label="Ανακοινώσεις ομάδων">
      {#each rooms as room (room.organization_id+':'+room.group_id)}
        <button type="button" class:current={selected?.organization_id===room.organization_id&&selected?.group_id===room.group_id}
          aria-pressed={selected?.organization_id===room.organization_id&&selected?.group_id===room.group_id}
          disabled={busy||reacting!==null||downloading!==null} onclick={()=>void chooseRoom(room)}>
          <span aria-hidden="true">⚽</span> {room.name}
        </button>
      {/each}
    </nav>
  {/if}
  {#if targetMessage}<p class="hint" role="status">{targetMessage}</p>{/if}
  {#if selected}
    <section class="feed" aria-label={'Επίσημες ανακοινώσεις '+selected.name}>
      <h2>{selected.name}</h2>
      {#if busy&&announcements.length===0}<p role="status">Φόρτωση ανακοινώσεων…</p>{/if}
      {#if !busy&&announcements.length===0&&!error}
        <p class="hint" role="status">Δεν υπάρχουν ανακοινώσεις σε αυτή τη σελίδα.</p>
      {/if}
      <ol>
        {#each announcements as notice (notice.id)}
          <li id={'app-announcement-'+notice.id} use:seenOnScreen={notice}>
            <div class="notice-top"><span class="official">Επίσημη ανακοίνωση</span>
              <time datetime={notice.published_at}>
                {new Date(notice.published_at).toLocaleString('el-CY',{dateStyle:'medium',timeStyle:'short'})}
              </time>
            </div>
            <h3>{notice.title}</h3>
            <p>{notice.body}</p>
            {#each notice.media??[] as media (media.id)}
              {#if media.kind==='banner'}
                <MobileAnnouncementBanner {controller} room={selected}
                  announcementId={notice.id} {media}/>
              {:else}
                <button type="button" class="attachment" disabled={busy||reacting!==null||downloading!==null}
                  onclick={()=>void downloadFile(notice,media)}
                  aria-label={'Λήψη αρχείου '+media.file_name}>
                  <span aria-hidden="true">📎</span> {media.file_name}
                  <small>{Math.ceil(media.size/1024)} KB · PDF</small>
                </button>
              {/if}
            {/each}
            <div class="reaction-summary" role="group" aria-label="Αντιδράσεις ανακοίνωσης">
              {#each notice.reactions as reaction (reaction.emoji)}
                <button type="button" class:mine={reaction.mine} disabled={busy||reacting!==null||downloading!==null}
                  aria-pressed={reaction.mine} aria-label={'Αντίδραση '+reaction.emoji+', '+reaction.count+' μέλη'}
                  onclick={()=>void toggleReaction(notice,reaction.emoji)}>
                  {reaction.emoji} <span>{reaction.count}</span>
                </button>
              {/each}
              <button type="button" class="reaction-add"
                disabled={busy||reacting!==null||downloading!==null} aria-expanded={reactionFor===notice.id}
                onclick={()=>reactionFor=reactionFor===notice.id?null:notice.id}>
                <span aria-hidden="true">☺</span> Αντίδραση
              </button>
            </div>
            {#if reactionFor===notice.id}
              <div class="reaction-palette" role="group" aria-label="Επιλογή αντίδρασης">
                {#each CHAT_REACTION_EMOJIS as emoji}
                  <button type="button" disabled={busy||reacting!==null||downloading!==null}
                    aria-label={'Επιλογή '+emoji}
                    onclick={()=>void toggleReaction(notice,emoji)}>{emoji}</button>
                {/each}
              </div>
            {/if}
          </li>
        {/each}
      </ol>
      {#if !busy&&!error&&(previousOffsets.length||nextOffset!==null)}
        <nav class="pagination" aria-label="Σελίδες ανακοινώσεων">
          <button type="button" class="outline" disabled={!previousOffsets.length||reacting!==null||downloading!==null}
            onclick={()=>void loadPrevious()}>Προηγούμενη</button>
          <span aria-live="polite">Σελίδα {previousOffsets.length+1}</span>
          <button type="button" class="outline" disabled={nextOffset===null||reacting!==null||downloading!==null}
            onclick={()=>void loadMore()}>Επόμενη</button>
        </nav>
      {/if}
    </section>
  {/if}
  {#if error}<p role="alert" class="error">{error}</p>{/if}
</section>

<style>
  .announcements-layout{padding:16px 14px 28px;font-family:var(--lf-body);font-size:var(--lf-type-body);color:var(--lf-text)}
  .heading{display:flex;justify-content:space-between;gap:10px;align-items:center}
  .eyebrow{font-size:10px;font-weight:700;letter-spacing:.15em;color:var(--lf-gold);margin:0 0 5px}
  h1,h2,h3{font-family:var(--lf-heading);line-height:1.15}
  h1{font-size:28px;margin:0}h2{font-size:19px;margin:0 0 14px}h3{font-size:21px;margin:12px 0}
  .hint{color:var(--lf-muted);font-size:12px;line-height:1.55;margin:12px 0 18px}
  .outline{min-height:44px;color:var(--lf-gold-light);border:1px solid var(--lf-brand-edge);border-radius:9px;background:transparent;padding:8px 12px;cursor:pointer}
  button:disabled{opacity:.5;cursor:default}
  .groups{display:grid;gap:8px;margin-bottom:16px}
  .groups button{display:flex;align-items:center;gap:9px;text-align:left;padding:11px 14px;min-height:52px;border-radius:11px;background:var(--lf-elevated);color:var(--lf-text);border:1px solid var(--lf-line);cursor:pointer}
  .groups button.current{border-color:var(--lf-gold);box-shadow:inset 0 0 0 1px var(--lf-gold)}
  .feed{background:var(--lf-surface);border:1px solid var(--lf-line);border-radius:14px;padding:13px}
  .feed ol{list-style:none;margin:0;padding:0;display:grid;gap:13px}
  .feed li{padding:15px 13px;background:#202325;border:1px solid #393e3b;border-radius:12px}
  .notice-top{display:flex;gap:12px;justify-content:space-between;align-items:center}
  .official{font-size:10px;color:var(--lf-gold-light);font-weight:700;letter-spacing:.03em}
  time{font-size:10px;color:var(--lf-muted)}
  .feed li p{line-height:1.6;white-space:pre-wrap;overflow-wrap:anywhere;margin:0}
  .reaction-summary,.reaction-palette{display:flex;flex-wrap:wrap;gap:6px;margin-top:10px}
  .reaction-summary button,.reaction-palette button{cursor:pointer;border-radius:20px;
    min-height:40px;min-width:45px;padding:7px 10px;color:var(--lf-text);
    background:var(--lf-elevated);border:1px solid var(--lf-line)}
  .reaction-summary button.mine{background:#3e321d;border-color:var(--lf-gold)}
  .reaction-summary .reaction-add{font-size:12px;min-width:90px}
  .reaction-summary button:disabled,.reaction-palette button:disabled{opacity:.5;cursor:default}
  .reaction-summary span{font-size:11px;color:var(--lf-gold-light)}
  .reaction-palette{padding:8px;border:1px solid var(--lf-brand-edge);border-radius:12px}
  .attachment{display:flex;gap:9px;align-items:center;flex-wrap:wrap;
    margin-top:10px;padding:10px 12px;min-height:44px;border:1px solid var(--lf-brand-edge);
    border-radius:9px;background:var(--lf-elevated);color:var(--lf-gold-light);
    cursor:pointer;max-width:100%;overflow-wrap:anywhere}
  .attachment small{color:var(--lf-muted);font-size:10px}
  .pagination{display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:8px;margin-top:15px}
  .pagination span{font-size:12px;color:var(--lf-muted)}
  .error{background:#2a1b1a;border:1px solid #7b3e35;color:#ffcdbe;border-radius:8px;padding:11px;font-size:12px}
  button:focus-visible{outline:2px solid var(--lf-gold);outline-offset:2px}
</style>
