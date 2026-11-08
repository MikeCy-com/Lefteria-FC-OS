<script lang="ts">
  import {onMount} from 'svelte';
  import type {MobileAuthController} from '../controller.ts';
  import type {GroupChatRoom,GroupChatMessage} from '../group-chat.ts';
  import {validTextMessage} from '../group-chat.ts';

  let {controller,actorId}:{controller:MobileAuthController;actorId:string}=$props();
  let rooms=$state<GroupChatRoom[]>([]);
  let selected=$state<GroupChatRoom|null>(null);
  let messages=$state<GroupChatMessage[]>([]);
  let nextOffset=$state<number|null>(null);
  let draft=$state('');
  let directoryBusy=$state(true),messageBusy=$state(false),sending=$state(false);
  let error=$state<string|null>(null),sendError=$state<string|null>(null);
  let requestNumber=0;
  let alive=true;
  onMount(()=>{
    alive=true;
    void refreshRooms();
    return ()=>{alive=false;requestNumber++;};
  });
  function messageOf(error:unknown):string {
    return error instanceof Error && error.message.length<=240
      ?error.message:'Η υπηρεσία δεν είναι διαθέσιμη.';
  }
  async function refreshRooms(){
    const request=++requestNumber;
    directoryBusy=true;error=null;selected=null;messages=[];nextOffset=null;
    try{
      const result=await controller.getChatRooms();
      if(!alive||request!==requestNumber)return;
      rooms=result;directoryBusy=false;
      if(result.length===1)void chooseRoom(result[0]);
    }catch(caught){if(alive&&request===requestNumber){rooms=[];error=messageOf(caught);}}
    finally{if(alive&&request===requestNumber)directoryBusy=false;}
  }
  async function chooseRoom(room:GroupChatRoom){
    if(sending)return;
    selected=room;messages=[];nextOffset=null;error=null;sendError=null;
    await loadMessages(false);
  }
  async function loadMessages(more:boolean){
    const room=selected;
    if(!room||messageBusy||sending)return;
    const request=++requestNumber;
    messageBusy=true;error=null;
    try{
      const page=await controller.getChatMessages(room,more?(nextOffset??0):0);
      if(!alive||request!==requestNumber)return;
      messages=more?[...messages,...page.messages]:page.messages;
      nextOffset=page.next_offset;
    }catch(caught){if(alive&&request===requestNumber)error=messageOf(caught);}
    finally{if(alive&&request===requestNumber)messageBusy=false;}
  }
  async function send(){
    const room=selected,body=draft.trim();
    if(!room||sending||messageBusy||!validTextMessage(body))return;
    sending=true;sendError=null;
    try{
      const sent=await controller.sendChatMessage(room,body);
      if(!alive||selected!==room)return;
      draft='';
      // The server confirmed persistence; do not fake an optimistic message.
      messages=[sent,...messages.filter(item=>item.id!==sent.id)];
      const page=await controller.getChatMessages(room,0);
      if(!alive||selected!==room)return;
      messages=page.messages;nextOffset=page.next_offset;
    }catch(caught){if(alive)sendError=messageOf(caught);}
    finally{if(alive)sending=false;}
  }
</script>

<section class="chat-layout" aria-labelledby="group-chat-title" data-testid="mobile-group-chat">
  <div class="chat-heading">
    <div><p class="eyebrow">LEFTERIA FC · APP</p><h1 id="group-chat-title">Μηνύματα</h1></div>
    <button type="button" class="outline" disabled={directoryBusy||sending}
      onclick={()=>void refreshRooms()} aria-label="Ανανέωση ομάδων">Ανανέωση</button>
  </div>
  <p class="hint">Ιδιωτικά κανάλια μόνο για τα μέλη της ομάδας ή της Ακαδημίας σου.</p>
  {#if directoryBusy}<p role="status">Φόρτωση ομάδων…</p>
  {:else if !rooms.length && !error}
    <p class="empty" role="status">Δεν βρέθηκαν ενεργές ομάδες για αυτόν τον ρόλο.</p>
  {/if}
  {#if rooms.length}
    <div class="rooms" role="group" aria-label="Επιλογή ομάδας">
      {#each rooms as room (room.organization_id+':'+room.group_id)}
        <button type="button" class:active={selected?.organization_id===room.organization_id&&selected?.group_id===room.group_id}
          aria-pressed={selected?.organization_id===room.organization_id&&selected?.group_id===room.group_id}
          disabled={sending} onclick={()=>void chooseRoom(room)}>
          <span class="room-mark">⚽</span>
          <span><strong>{room.name}</strong><small>{room.kind==='team'?'Πρώτη ομάδα':'Ακαδημία'}</small></span>
        </button>
      {/each}
    </div>
  {/if}
  {#if selected}
    <section class="thread" aria-label={'Συζήτηση '+selected.name}>
      <header><h2>{selected.name}</h2><button type="button" class="outline" disabled={messageBusy||sending}
        onclick={()=>void loadMessages(false)}>Ανανέωση</button></header>
      {#if nextOffset!==null}
        <button type="button" class="outline load-more" disabled={messageBusy||sending}
          onclick={()=>void loadMessages(true)}>Παλαιότερα μηνύματα</button>
      {/if}
      {#if messageBusy}<p role="status">Φόρτωση μηνυμάτων…</p>
      {:else if messages.length===0 && !error}<p class="empty">Δεν υπάρχουν ακόμη μηνύματα.</p>{/if}
      <ol class="messages" aria-live="polite">
        {#each [...messages].reverse() as message (message.id)}
          <li class:own={message.sender_id===actorId}>
            <div class="message-head"><strong>{message.sender_id===actorId?'Εσύ':message.sender_name}</strong>
              <time datetime={message.created_at}>{new Date(message.created_at).toLocaleString('el-CY',{dateStyle:'short',timeStyle:'short'})}</time></div>
            <p>{message.content}</p>
          </li>
        {/each}
      </ol>
      <form onsubmit={event=>{event.preventDefault();void send();}}>
        <label for="group-message-input">Νέο μήνυμα</label>
        <input id="group-message-input" type="text" bind:value={draft} maxlength="10000"
          disabled={sending} placeholder="Γράψε μήνυμα στην ομάδα…" />
        <button class="send" type="submit" disabled={sending||messageBusy||!validTextMessage(draft.trim())}>
          {sending?'Αποστολή…':'Αποστολή'}
        </button>
      </form>
      {#if sendError}<p role="alert" class="error">{sendError}</p>{/if}
    </section>
  {/if}
  {#if error}<p role="alert" class="error">{error}</p>{/if}
</section>

<style>
  .chat-layout{padding:16px 14px 28px;color:var(--lf-text);font-family:var(--lf-body);font-size:var(--lf-type-body)}
  .chat-heading,.thread header{display:flex;align-items:center;justify-content:space-between;gap:12px}
  .chat-heading h1{font-family:var(--lf-heading);font-size:28px;line-height:1.1;margin:0}
  .eyebrow{color:var(--lf-gold);font-weight:700;font-size:10px;letter-spacing:.15em;margin:0 0 5px}
  .hint{color:var(--lf-muted);font-size:12px;margin:12px 0 18px}
  .outline{border:1px solid var(--lf-brand-edge);border-radius:8px;background:transparent;color:var(--lf-gold-light);min-height:44px;padding:7px 12px;cursor:pointer}
  .rooms{display:grid;gap:8px;margin:0 0 20px}
  .rooms button{display:flex;align-items:center;gap:12px;text-align:left;background:var(--lf-elevated);border:1px solid var(--lf-line);color:white;border-radius:12px;min-height:58px;padding:9px 12px;cursor:pointer}
  .rooms button.active{border-color:var(--lf-gold);box-shadow:inset 0 0 0 1px var(--lf-gold)}
  .room-mark{font-size:23px}.rooms strong,.rooms small{display:block}.rooms small{color:var(--lf-muted);font-size:11px;margin-top:4px}
  .thread{border:1px solid var(--lf-line);border-radius:14px;background:var(--lf-surface);padding:12px}
  .thread h2{font-family:var(--lf-heading);font-size:19px;margin:0}
  .load-more{width:100%;margin:10px 0}
  .messages{list-style:none;margin:16px 0;padding:0;display:flex;flex-direction:column;gap:10px;max-height:55vh;overflow:auto}
  .messages li{align-self:flex-start;width:min(94%,360px);border:1px solid #343638;border-radius:12px;background:#202325;padding:10px 12px;overflow-wrap:anywhere}
  .messages li.own{align-self:flex-end;border-color:#7d581f;background:#2a2319}
  .message-head{display:flex;justify-content:space-between;align-items:center;gap:8px}
  .message-head strong{font-size:11px;color:var(--lf-gold-light)}.message-head time{font-size:10px;color:var(--lf-muted)}
  .messages p{margin:7px 0 0;white-space:pre-wrap}
  .thread label{display:block;font-weight:600;font-size:12px;margin:10px 0 7px}
  .thread input{box-sizing:border-box;width:100%;min-height:44px;border:1px solid #56595a;border-radius:8px;background:#0b0d0e;color:#fff;padding:10px;font:inherit}
  .send{min-height:44px;border:0;background:var(--lf-gold);color:#111;border-radius:8px;font-weight:700;padding:10px 18px;margin-top:9px;cursor:pointer}
  .empty{color:var(--lf-muted);text-align:center;padding:15px}
  .error{border:1px solid #8b433d;border-radius:8px;color:#ffd3cd;background:#301918;padding:11px;margin:12px 0}
</style>
