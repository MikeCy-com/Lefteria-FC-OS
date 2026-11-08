<script lang="ts">
  import {onMount} from 'svelte';
  import type {MobileAuthController} from '../controller.ts';
  import type {GroupChatRoom} from '../group-chat.ts';
  import type {AppAnnouncement} from '../app-announcements.ts';
  import {CHAT_REACTION_EMOJIS,type ChatReaction} from '../chat-actions.ts';

  let {controller}:{controller:MobileAuthController}=$props();
  let rooms=$state<GroupChatRoom[]>([]);
  let selected=$state<GroupChatRoom|null>(null);
  let announcements=$state<AppAnnouncement[]>([]);
  let nextOffset=$state<number|null>(null);
  let loading=$state(true),busy=$state(false);
  let error=$state<string|null>(null);
  let reactionFor=$state<string|null>(null);
  let reacting=$state<string|null>(null);
  let requestNumber=0,alive=true;
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
    loading=true;error=null;rooms=[];selected=null;announcements=[];nextOffset=null;
    reactionFor=null;reacting=null;
    try{
      const result=await controller.getChatRooms();
      if(!alive||request!==requestNumber)return;
      rooms=result;
      if(result.length===1)void chooseRoom(result[0]);
    }catch(e){if(alive&&request===requestNumber)error=fault(e);}
    finally{if(alive&&request===requestNumber)loading=false;}
  }
  async function chooseRoom(room:GroupChatRoom){
    const request=++requestNumber;
    selected=room;announcements=[];nextOffset=null;error=null;reactionFor=null;
    busy=true;
    try{
      const result=await controller.getAppAnnouncements(room,0);
      if(!alive||request!==requestNumber||selected!==room)return;
      announcements=result.announcements;nextOffset=result.next_offset;
    }catch(e){if(alive&&request===requestNumber)error=fault(e);}
    finally{if(alive&&request===requestNumber)busy=false;}
  }
  async function loadMore(){
    const room=selected;
    if(!room||nextOffset===null||busy)return;
    const request=++requestNumber;
    busy=true;error=null;
    try{
      const page=await controller.getAppAnnouncements(room,nextOffset);
      if(!alive||request!==requestNumber||selected!==room)return;
      announcements=[...announcements,...page.announcements.filter(
        item=>!announcements.some(previous=>previous.id===item.id))];
      nextOffset=page.next_offset;
    }catch(e){if(alive&&request===requestNumber)error=fault(e);}
    finally{if(alive&&request===requestNumber)busy=false;}
  }
  async function toggleReaction(notice:AppAnnouncement,emoji:ChatReaction){
    const room=selected;
    if(!room||reacting!==null||!announcements.some(a=>a.id===notice.id))return;
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
    <button type="button" class="outline" disabled={busy||loading}
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
          disabled={busy} onclick={()=>void chooseRoom(room)}>
          <span aria-hidden="true">⚽</span> {room.name}
        </button>
      {/each}
    </nav>
  {/if}
  {#if selected}
    <section class="feed" aria-label={'Επίσημες ανακοινώσεις '+selected.name}>
      <h2>{selected.name}</h2>
      {#if busy&&announcements.length===0}<p role="status">Φόρτωση ανακοινώσεων…</p>{/if}
      {#if !busy&&announcements.length===0&&!error}
        <p class="hint" role="status">Δεν έχουν δημοσιευτεί ανακοινώσεις για αυτό το τμήμα.</p>
      {/if}
      <ol>
        {#each announcements as notice (notice.id)}
          <li>
            <div class="notice-top"><span class="official">Επίσημη ανακοίνωση</span>
              <time datetime={notice.published_at}>
                {new Date(notice.published_at).toLocaleString('el-CY',{dateStyle:'medium',timeStyle:'short'})}
              </time>
            </div>
            <h3>{notice.title}</h3>
            <p>{notice.body}</p>
            <div class="reaction-summary" role="group" aria-label="Αντιδράσεις ανακοίνωσης">
              {#each notice.reactions as reaction (reaction.emoji)}
                <button type="button" class:mine={reaction.mine} disabled={reacting!==null}
                  aria-pressed={reaction.mine} aria-label={'Αντίδραση '+reaction.emoji+', '+reaction.count+' μέλη'}
                  onclick={()=>void toggleReaction(notice,reaction.emoji)}>
                  {reaction.emoji} <span>{reaction.count}</span>
                </button>
              {/each}
              <button type="button" class="reaction-add"
                disabled={reacting!==null} aria-expanded={reactionFor===notice.id}
                onclick={()=>reactionFor=reactionFor===notice.id?null:notice.id}>
                <span aria-hidden="true">☺</span> Αντίδραση
              </button>
            </div>
            {#if reactionFor===notice.id}
              <div class="reaction-palette" role="group" aria-label="Επιλογή αντίδρασης">
                {#each CHAT_REACTION_EMOJIS as emoji}
                  <button type="button" disabled={reacting!==null}
                    aria-label={'Επιλογή '+emoji}
                    onclick={()=>void toggleReaction(notice,emoji)}>{emoji}</button>
                {/each}
              </div>
            {/if}
          </li>
        {/each}
      </ol>
      {#if nextOffset!==null}
        <button type="button" class="outline more" disabled={busy} onclick={()=>void loadMore()}>
          Περισσότερες ανακοινώσεις
        </button>
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
  .more{width:100%;margin-top:15px}
  .error{background:#2a1b1a;border:1px solid #7b3e35;color:#ffcdbe;border-radius:8px;padding:11px;font-size:12px}
  button:focus-visible{outline:2px solid var(--lf-gold);outline-offset:2px}
</style>
