<script lang="ts">
  import {onMount} from 'svelte';
  import type {MobileAuthController} from '../controller.ts';
  import type {MobileRole} from '../navigation.ts';
  import type {GroupChatRoom} from '../group-chat.ts';
  import {validPrivateText,type PrivateContact,type PrivateConversation,
    type PrivateMessage} from '../private-chat.ts';
  import MobileHoldAction from './MobileHoldAction.svelte';
  import MobileMessageActionMenu from './MobileMessageActionMenu.svelte';

  let {controller,actorId,role}:{controller:MobileAuthController;actorId:string;role:MobileRole}=$props();
  let rooms=$state<GroupChatRoom[]>([]);
  let roomKey=$state('');
  let contacts=$state<PrivateContact[]>([]);
  let contactId=$state('');
  let conversations=$state<PrivateConversation[]>([]);
  let selected=$state<PrivateConversation|null>(null);
  let messages=$state<PrivateMessage[]>([]);
  let nextOffset=$state<number|null>(null);
  let draft=$state('');
  let menuMessage=$state<PrivateMessage|null>(null);
  let menuConversation=$state<PrivateConversation|null>(null);
  let busy=$state(false),loading=$state(true),messageLoading=$state(false),contactLoading=$state(false);
  let error=$state<string|null>(null),messageError=$state<string|null>(null);
  let sequence=0,alive=true;
  const chosenRoom=$derived(rooms.find(r=>r.organization_id+':'+r.group_id===roomKey));
  const allowedRole=$derived(role==='parent'||role==='coach'||role==='management');

  onMount(()=>{
    alive=true;void refresh();
    return ()=>{alive=false;sequence++;};
  });
  function fault(e:unknown){
    return e instanceof Error&&e.message.length<=240?e.message:'Η υπηρεσία δεν είναι διαθέσιμη.';
  }
  async function refresh(){
    const request=++sequence;
    busy=false;loading=true;error=null;selected=null;messages=[];nextOffset=null;
    menuConversation=null;menuMessage=null;
    try{
      const [inbox,directory]=await Promise.all([
        controller.getPrivateConversations(),controller.getChatRooms()
      ]);
      if(!alive||request!==sequence)return;
      conversations=inbox;rooms=directory;
      if(!directory.some(room=>room.organization_id+':'+room.group_id===roomKey))
        roomKey=directory.length?directory[0].organization_id+':'+directory[0].group_id:'';
      if(chosenRoom)void loadContacts(chosenRoom);
    }catch(e){if(alive&&request===sequence){error=fault(e);conversations=[];rooms=[];}}
    finally{if(alive&&request===sequence)loading=false;}
  }
  async function loadContacts(room:GroupChatRoom){
    const key=room.organization_id+':'+room.group_id;
    contactLoading=true;contacts=[];contactId='';
    try{
      const found=await controller.getPrivateContacts(room);
      if(!alive||roomKey!==key)return;
      contacts=found;
    }catch(e){if(alive&&roomKey===key)error=fault(e);}
    finally{if(alive&&roomKey===key)contactLoading=false;}
  }
  function changeRoom(){
    if(chosenRoom)void loadContacts(chosenRoom);
    else{contacts=[];contactId='';}
  }
  async function invite(){
    const room=chosenRoom,target=contactId;
    if(!room||!target||busy)return;
    busy=true;error=null;
    try{
      await controller.invitePrivateContact(room,target);
      if(!alive)return;
      conversations=await controller.getPrivateConversations();
      if(!alive)return;
      contactId='';
    }catch(e){if(alive)error=fault(e);}
    finally{if(alive)busy=false;}
  }
  async function respond(item:PrivateConversation,accept:boolean){
    if(busy||item.status!=='pending'||item.is_initiator)return;
    busy=true;error=null;
    try{
      await controller.respondPrivateInvitation(item.id,accept);
      if(!alive)return;
      conversations=await controller.getPrivateConversations();
    }catch(e){if(alive)error=fault(e);}
    finally{if(alive)busy=false;}
  }
  async function openConversation(item:PrivateConversation){
    if(busy||item.status!=='accepted')return;
    const request=++sequence;
    selected=item;messages=[];nextOffset=null;messageError=null;
    menuMessage=null;menuConversation=null;messageLoading=true;
    try{
      const page=await controller.getPrivateMessages(item.id,0);
      if(!alive||request!==sequence||selected?.id!==item.id)return;
      messages=page.messages;nextOffset=page.next_offset;
    }catch(e){if(alive&&request===sequence)messageError=fault(e);}
    finally{if(alive&&request===sequence)messageLoading=false;}
  }
  async function reloadMessages(more=false){
    const item=selected;
    if(!item||messageLoading||busy)return;
    const request=++sequence;
    messageLoading=true;messageError=null;
    try{
      const page=await controller.getPrivateMessages(item.id,more?(nextOffset??0):0);
      if(!alive||request!==sequence||selected?.id!==item.id)return;
      messages=more?[...messages,...page.messages]:page.messages;
      nextOffset=page.next_offset;
    }catch(e){if(alive&&request===sequence)messageError=fault(e);}
    finally{if(alive&&request===sequence)messageLoading=false;}
  }
  async function send(){
    const item=selected,body=draft.trim();
    if(!item||busy||messageLoading||!validPrivateText(body))return;
    busy=true;messageError=null;
    try{
      const sent=await controller.sendPrivateMessage(item.id,body);
      if(!alive||selected?.id!==item.id)return;
      draft='';
      messages=[sent,...messages.filter(m=>m.id!==sent.id)];
      const page=await controller.getPrivateMessages(item.id,0);
      if(!alive||selected?.id!==item.id)return;
      messages=page.messages;nextOffset=page.next_offset;
      conversations=await controller.getPrivateConversations();
    }catch(e){if(alive&&selected?.id===item.id)messageError=fault(e);}
    finally{if(alive)busy=false;}
  }
  async function mutate(item:PrivateMessage|null,action:'edit'|'delete',content?:string):Promise<boolean>{
    const conv=selected;
    if(!conv||!item||item.sender_id!==actorId||item.is_deleted||busy||messageLoading)return false;
    busy=true;messageError=null;
    try{
      const changed=action==='edit'
        ?await controller.editPrivateMessage(conv.id,item.id,item.version,content??'')
        :await controller.deletePrivateMessage(conv.id,item.id,item.version);
      if(!alive||selected?.id!==conv.id)return false;
      messages=messages.map(m=>m.id===changed.id?changed:m);
      menuMessage=null;
      return true;
    }catch(e){if(alive)messageError=fault(e);return false;}
    finally{if(alive)busy=false;}
  }
  async function hide():Promise<boolean>{
    const conv=menuConversation??selected;
    if(!conv||busy)return false;
    busy=true;messageError=null;
    try{
      const confirmed=await controller.hidePrivateConversationForMe(conv.id);
      if(!confirmed||!alive)return false;
      ++sequence;selected=null;messages=[];nextOffset=null;menuConversation=null;
      conversations=await controller.getPrivateConversations();
      return true;
    }catch(e){if(alive)messageError=fault(e);return false;}
    finally{if(alive)busy=false;}
  }
</script>

<section class="private-layout" data-testid="mobile-private-chat" aria-label="Ιδιωτικά μηνύματα">
  <div class="heading">
    <div><p class="eyebrow">LEFTERIA FC · APP</p><h2>Ιδιωτικές συνομιλίες</h2></div>
    <button type="button" class="outline" disabled={busy||loading} onclick={()=>void refresh()}>Ανανέωση</button>
  </div>
  <p class="hint">Μόνο γονείς, προπονητές και υπεύθυνοι με ενεργή κοινή ομάδα. Απαιτείται αποδοχή πριν την ανταλλαγή μηνυμάτων. Δεν επιτρέπονται ιδιωτικές συνομιλίες με λογαριασμούς παικτών.</p>
  {#if !allowedRole}<p role="alert">Η λειτουργία δεν είναι διαθέσιμη σε αυτόν τον ρόλο.</p>
  {:else}
    {#if loading}<p role="status">Φόρτωση ιδιωτικών συνομιλιών…</p>
    {:else}
      <section class="invite" aria-label="Νέα πρόσκληση">
        <h3>Νέα ιδιωτική συνομιλία</h3>
        <label for="private-group">Κοινή ομάδα</label>
        <select id="private-group" bind:value={roomKey} disabled={busy} onchange={changeRoom}>
          {#each rooms as room (room.organization_id+room.group_id)}
            <option value={room.organization_id+':'+room.group_id}>{room.name}</option>
          {/each}
        </select>
        <label for="private-contact">Παραλήπτης</label>
        <select id="private-contact" bind:value={contactId} disabled={busy||contactLoading||!contacts.length}>
          <option value="">Επιλογή επαφής</option>
          {#each contacts as contact (contact.id)}
            <option value={contact.id}>{contact.name} · {contact.role==='parent'?'Γονέας':contact.role==='coach'?'Προπονητής':'Διοίκηση'}</option>
          {/each}
        </select>
        {#if contactLoading}<p class="hint" role="status">Έλεγχος διαθέσιμων επαφών…</p>{/if}
        <button type="button" class="gold" disabled={busy||!contactId||contactLoading}
          onclick={()=>void invite()}>Αποστολή πρόσκλησης</button>
      </section>
      <section class="inbox" aria-label="Προσκλήσεις και συζητήσεις">
        <h3>Συνομιλίες και προσκλήσεις</h3>
        {#if conversations.length===0}<p class="hint">Δεν υπάρχουν ακόμη ιδιωτικές συνομιλίες.</p>{/if}
        <div class="conversation-list">
          {#each conversations as conv (conv.id)}
            <div class:current={selected?.id===conv.id} class="conversation">
              {#if conv.status==='accepted'}
                <MobileHoldAction enabled={!busy}
                  label={'Ενέργειες ιδιωτικής συνομιλίας με '+conv.partner_name}
                  onOpen={()=>{menuConversation=conv;menuMessage=null;}}>
                  <div role="button" tabindex="0" class="conversation-target"
                    onclick={()=>void openConversation(conv)}
                    onkeydown={e=>{if(e.key==='Enter'||e.key===' '){e.preventDefault();void openConversation(conv);}}}>
                    <strong>{conv.partner_name}</strong><small>Αποδεκτή · Ιδιωτική</small>
                  </div>
                </MobileHoldAction>
              {:else}
                <div class="conversation-target">
                  <strong>{conv.partner_name}</strong>
                  <small>{conv.is_initiator?'Αναμονή αποδοχής':'Νέα αίτηση συνομιλίας'}</small>
                </div>
                {#if !conv.is_initiator}
                  <div class="decisions">
                    <button type="button" class="gold" disabled={busy} onclick={()=>void respond(conv,true)}>Αποδοχή</button>
                    <button type="button" class="outline" disabled={busy} onclick={()=>void respond(conv,false)}>Απόρριψη</button>
                  </div>
                {/if}
              {/if}
            </div>
          {/each}
        </div>
      </section>
    {/if}
    {#if selected}
      <section class="thread" aria-label={'Ιδιωτική συνομιλία με '+selected.partner_name}>
        <header>
          <h3>{selected.partner_name}</h3>
          <button type="button" class="outline" disabled={busy||messageLoading} onclick={()=>void reloadMessages()}>Ανανέωση</button>
        </header>
        {#if nextOffset!==null}
          <button type="button" class="outline older" disabled={busy||messageLoading}
            onclick={()=>void reloadMessages(true)}>Παλαιότερα μηνύματα</button>
        {/if}
        {#if messageLoading}<p role="status">Φόρτωση μηνυμάτων…</p>{/if}
        <ol class="messages" aria-live="polite">
          {#each [...messages].reverse() as message (message.id)}
            <li class:own={message.sender_id===actorId} class:deleted={message.is_deleted}>
              <MobileHoldAction enabled={message.sender_id===actorId&&!message.is_deleted&&!busy&&!messageLoading}
                onOpen={()=>{menuMessage=message;menuConversation=null;}} label="Ενέργειες μηνύματος">
                <div class="message-head"><strong>{message.sender_id===actorId?'Εσύ':message.sender_name}</strong>
                  <time datetime={message.created_at}>{new Date(message.created_at).toLocaleString('el-CY',{dateStyle:'short',timeStyle:'short'})}</time>
                </div>
                <p>{message.is_deleted?'Το μήνυμα διαγράφηκε':message.content}</p>
                {#if message.edited_at&&!message.is_deleted}<small>Επεξεργάστηκε</small>{/if}
              </MobileHoldAction>
            </li>
          {/each}
        </ol>
        <form onsubmit={e=>{e.preventDefault();void send();}}>
          <label for="private-message-input">Νέο ιδιωτικό μήνυμα</label>
          <input id="private-message-input" bind:value={draft} maxlength="10000"
            disabled={busy||messageLoading} placeholder="Γράψε ένα μήνυμα…" />
          <button type="submit" class="gold" disabled={busy||messageLoading||!validPrivateText(draft.trim())}>Αποστολή</button>
        </form>
        {#if messageError}<p role="alert" class="error">{messageError}</p>{/if}
      </section>
    {/if}
    {#if error}<p class="error" role="alert">{error}</p>{/if}
  {/if}
</section>

{#if menuMessage && selected}
  <MobileMessageActionMenu
    context={{kind:'message',role,actorId,senderId:menuMessage.sender_id,
      isDeleted:menuMessage.is_deleted,isParticipant:true,canReply:true,
      canEdit:true,canDelete:true,canReact:false,canSeeReaders:false}}
    content={menuMessage.content}
    onClose={()=>{menuMessage=null;}}
    onEdit={text=>mutate(menuMessage,'edit',text)}
    onDelete={()=>mutate(menuMessage,'delete')}
  />
{/if}
{#if menuConversation && menuConversation.status==='accepted'}
  <MobileMessageActionMenu
    context={{kind:'conversation',role,conversationKind:'private',
      isParticipant:true,canHideForSelf:true}}
    onClose={()=>{menuConversation=null;}}
    onHideChat={hide}
  />
{/if}

<style>
  .private-layout{padding:16px 14px 28px;color:var(--lf-text);font-family:var(--lf-body);font-size:var(--lf-type-body)}
  .heading,.thread header{display:flex;justify-content:space-between;align-items:center;gap:12px}
  .eyebrow{color:var(--lf-gold);font-weight:700;font-size:10px;letter-spacing:.15em;margin:0 0 5px}
  h2{font-family:var(--lf-heading);font-size:25px;margin:0}
  h3{font-family:var(--lf-heading);font-size:17px;margin:4px 0 13px}
  .hint{color:var(--lf-muted);font-size:12px;line-height:1.55;margin:9px 0 16px}
  .invite,.inbox,.thread{background:var(--lf-surface);border:1px solid var(--lf-line);border-radius:14px;padding:14px;margin:12px 0}
  label{display:block;font-weight:600;font-size:12px;margin:9px 0 5px}
  select,input{width:100%;background:#1d2021;color:#fff;border:1px solid #525252;border-radius:9px;min-height:44px;padding:10px;font:inherit}
  .gold,.outline{min-height:44px;border-radius:8px;padding:8px 12px;cursor:pointer;font:inherit}
  .gold{background:var(--lf-gold);color:#121212;border:1px solid var(--lf-gold);font-weight:700;margin-top:10px}
  .outline{background:transparent;border:1px solid var(--lf-brand-edge);color:var(--lf-gold-light)}
  button:disabled{opacity:.55;cursor:default}
  .conversation-list{display:grid;gap:8px}
  .conversation{border:1px solid #393b3b;background:#212323;padding:9px 11px;border-radius:11px}
  .conversation.current{border-color:var(--lf-gold)}
  .conversation-target{padding:8px 0;min-height:40px;cursor:pointer}
  .conversation-target strong,.conversation-target small{display:block}
  .conversation-target strong{color:var(--lf-gold-light)}
  .conversation-target small{color:var(--lf-muted);font-size:11px;margin-top:4px}
  .conversation-target:focus-visible{outline:2px solid var(--lf-gold)}
  .decisions{display:flex;gap:8px}.decisions .gold{margin:0}
  .older{width:100%;margin:8px 0}
  .messages{list-style:none;display:flex;flex-direction:column;gap:9px;padding:0;max-height:55vh;overflow:auto}
  .messages li{align-self:flex-start;width:min(94%,360px);border:1px solid #343638;border-radius:12px;background:#202325;padding:10px 12px;overflow-wrap:anywhere}
  .messages li.own{align-self:flex-end;border-color:#7d581f;background:#2a2319}
  .messages li.deleted{opacity:.7}
  .messages li p{margin:7px 0 0;white-space:pre-wrap}
  .message-head{display:flex;align-items:center;justify-content:space-between;gap:8px}
  .message-head strong{font-size:11px;color:var(--lf-gold-light)}
  .message-head time{font-size:10px;color:var(--lf-muted)}
  .messages small{font-size:10px;color:var(--lf-muted)}
  form .gold{width:100%}
  .error{border:1px solid #853c35;background:#2b1c1a;color:#ffcebf;border-radius:8px;padding:10px;font-size:12px}
</style>
