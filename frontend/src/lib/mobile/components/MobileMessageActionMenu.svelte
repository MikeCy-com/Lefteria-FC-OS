<script lang="ts">
  import {onMount, tick} from 'svelte';
  import {availableChatActions, CHAT_REACTION_EMOJIS, validReaders,
    type ChatReaction, type ChatSurface} from '../chat-actions.ts';

  let {context,content='',onClose,onEdit,onDelete,onReact,onHideChat,onSeenBy}:{
    context:ChatSurface; content?:string; onClose:()=>void;
    onEdit?:(text:string)=>boolean|Promise<boolean>;
    onDelete?:()=>boolean|Promise<boolean>;
    onReact?:(emoji:ChatReaction)=>boolean|Promise<boolean>;
    onHideChat?:()=>boolean|Promise<boolean>;
    onSeenBy?:()=>Promise<unknown>;
  }=$props();
  const available=$derived(availableChatActions(context).filter(a=>
    a==='edit'?!!onEdit:a==='delete'?!!onDelete:a==='react'?!!onReact:
      a==='seen-by'?!!onSeenBy:!!onHideChat));
  let mode=$state<'actions'|'edit'|'react'|'confirm'|'seen-by'>('actions');
  let editText=$state(content);
  let busy=$state(false), error=$state<string|null>(null);
  let readers=$state<{id:string;name:string}[]|null>(null);
  let dialog=$state<HTMLElement>();
  const id=$props.id();

  onMount(()=>{
    const previous=document.activeElement instanceof HTMLElement?document.activeElement:null;
    void tick().then(()=>dialog?.querySelector<HTMLElement>('button:not(:disabled),textarea')?.focus());
    return ()=>previous?.focus();
  });
  function keys(event:KeyboardEvent){
    if(event.key==='Escape' && !busy){event.preventDefault();onClose();return;}
    if(event.key!=='Tab' || !dialog)return;
    const focusable=[...dialog.querySelectorAll<HTMLElement>('button:not(:disabled),textarea:not(:disabled)')];
    if(!focusable.length)return;
    if(event.shiftKey && document.activeElement===focusable[0]){
      event.preventDefault();focusable[focusable.length-1].focus();
    }else if(!event.shiftKey && document.activeElement===focusable[focusable.length-1]){
      event.preventDefault();focusable[0].focus();
    }
  }
  async function execute(action:'edit'|'delete'|'react'|'delete-chat-for-me',emoji?:ChatReaction){
    if(busy || !available.includes(action))return;
    if(action==='edit' && (!editText.trim() || editText.trim().length>10000)){
      error='Γράψε έγκυρο μήνυμα έως 10.000 χαρακτήρες.';return;
    }
    busy=true;error=null;
    try{
      const result=action==='edit'?await onEdit?.(editText.trim()):
        action==='delete'?await onDelete?.():
        action==='react' && emoji?await onReact?.(emoji):
        action==='delete-chat-for-me'?await onHideChat?.():false;
      if(result===true)onClose();
      else error='Η αλλαγή δεν επιβεβαιώθηκε από τον διακομιστή.';
    }catch{error='Η ενέργεια απέτυχε. Δοκίμασε ξανά.';}
    finally{busy=false;}
  }
  async function loadReaders(){
    if(busy || !available.includes('seen-by') || !onSeenBy)return;
    mode='seen-by';readers=null;error=null;busy=true;
    try{
      const response=await onSeenBy();
      if(!validReaders(response,150))throw new Error('Invalid read-receipt data');
      readers=response.readers;
    }catch{error='Η λίστα ανάγνωσης δεν είναι διαθέσιμη.';}
    finally{busy=false;}
  }
  function back(){if(busy)return;mode='actions';error=null;}
</script>

<div class="chat-overlay" data-testid="mobile-chat-action-overlay" role="presentation"
  onclick={event=>{if(event.target===event.currentTarget && !busy)onClose();}}>
  <section bind:this={dialog} role="dialog" aria-modal="true" aria-labelledby={id+'-title'}
    class="chat-dialog" onkeydown={keys} data-testid="mobile-chat-action-menu">
    <div class="chat-title">
      <h2 id={id+'-title'}>{context.kind==='announcement'?'Ανακοίνωση':
        context.kind==='conversation'?'Ιδιωτική συνομιλία':'Μήνυμα'}</h2>
      <button type="button" class="close" aria-label="Κλείσιμο" disabled={busy}
        onclick={onClose}>×</button>
    </div>
    {#if mode==='actions'}
      {#if available.includes('edit')}
        <button type="button" class="choice" onclick={()=>{editText=content;mode='edit';}}>Επεξεργασία</button>
      {/if}
      {#if available.includes('react')}
        <button type="button" class="choice" onclick={()=>{mode='react';}}>Αντίδραση</button>
      {/if}
      {#if available.includes('seen-by')}
        <button type="button" class="choice" onclick={()=>void loadReaders()}
          data-testid="mobile-chat-seen-by">Διαβάστηκε από</button>
      {/if}
      {#if available.includes('delete')}
        <button type="button" class="choice destructive" onclick={()=>{mode='confirm';}}>Διαγραφή μηνύματος</button>
      {/if}
      {#if available.includes('delete-chat-for-me')}
        <p class="notice">Το ιστορικό κρύβεται μόνο από τον λογαριασμό σου. Οι άλλοι συμμετέχοντες το διατηρούν.</p>
        <button type="button" class="choice destructive" onclick={()=>{mode='confirm';}}>Διαγραφή συνομιλίας μόνο για μένα</button>
      {/if}
      {#if available.length===0}<p class="notice" role="status">Δεν υπάρχουν διαθέσιμες ενέργειες.</p>{/if}
    {:else if mode==='edit' && available.includes('edit')}
      <label for={id+'-edit'}>Νέο κείμενο μηνύματος</label>
      <textarea id={id+'-edit'} bind:value={editText} maxlength="10000"
        disabled={busy} rows="4" data-testid="mobile-chat-edit-input"></textarea>
      <button type="button" class="primary" disabled={busy || !editText.trim()}
        onclick={()=>void execute('edit')} data-testid="mobile-chat-edit-save">Αποθήκευση</button>
      <button type="button" class="choice" disabled={busy} onclick={back}>Πίσω</button>
    {:else if mode==='react' && available.includes('react')}
      <p class="notice">Επίλεξε αντίδραση</p>
      <div class="emojis" data-testid="mobile-chat-emoji-reactions">
        {#each CHAT_REACTION_EMOJIS as emoji (emoji)}
          <button type="button" aria-label={'Αντίδραση '+emoji} disabled={busy}
            onclick={()=>void execute('react',emoji)}>{emoji}</button>
        {/each}
      </div>
      <button type="button" class="choice" disabled={busy} onclick={back}>Πίσω</button>
    {:else if mode==='seen-by' && available.includes('seen-by')}
      <h3>Διαβάστηκε από:</h3>
      {#if busy}<p role="status">Φόρτωση επιβεβαιωμένων αναγνώσεων…</p>
      {:else if readers}
        {#if readers.length===0}
          <p class="notice" role="status">Δεν υπάρχουν επιβεβαιωμένες αναγνώσεις.</p>
        {:else}
          <ul class="readers" data-testid="mobile-chat-readers">
            {#each readers as reader (reader.id)}<li>{reader.name}</li>{/each}
          </ul>
        {/if}
      {/if}
      <button type="button" class="choice" disabled={busy} onclick={back}>Πίσω</button>
    {:else if mode==='confirm'}
      <p class="notice">Επιβεβαίωσε τη διαγραφή. Η ενέργεια δεν αναιρείται από αυτό το μενού.</p>
      <button type="button" class="choice destructive" disabled={busy}
        onclick={()=>void execute(context.kind==='conversation'?'delete-chat-for-me':'delete')}
        data-testid="mobile-chat-delete-confirm">Επιβεβαίωση</button>
      <button type="button" class="choice" disabled={busy} onclick={back}>Ακύρωση</button>
    {/if}
    {#if error}<p class="chat-error" role="alert">{error}</p>{/if}
  </section>
</div>

<style>
  .chat-overlay{position:fixed;inset:0;z-index:120;display:grid;place-items:center;padding:18px;background:#000b;backdrop-filter:blur(4px)}
  .chat-dialog{width:min(100%,360px);max-height:min(80dvh,640px);overflow:auto;border:1px solid #65502d;border-radius:16px;background:#141618;color:#f4f4f4;box-shadow:0 22px 70px #000c;padding:17px;font-family:'Fira Sans',sans-serif;font-size:13px}
  .chat-title{display:flex;justify-content:space-between;gap:12px;align-items:center;margin-bottom:12px}
  .chat-title h2{font-family:'Fira Sans Extra Condensed',sans-serif;font-size:20px;margin:0}
  button{cursor:pointer;min-height:44px;border-radius:8px;font:inherit}
  button:disabled{opacity:.5;cursor:default}
  button:focus-visible,textarea:focus-visible{outline:2px solid #ffca46;outline-offset:2px}
  .close{width:44px;border:0;background:transparent;color:#ddd;font-size:24px}
  .choice{display:block;width:100%;padding:10px 12px;margin:5px 0;background:#202325;border:1px solid #35383a;color:#f6e4c0;text-align:left}
  .choice.destructive{color:#ffb4a9;border-color:#7c3e37;background:#2a1c1a}
  .notice{font-size:12px;color:#b9c0c2;line-height:1.55}
  .primary{width:100%;padding:10px;background:#f5a623;color:#111;border:1px solid #f5a623;font-weight:700}
  label{display:block;margin-bottom:8px}
  textarea{width:100%;min-height:100px;border:1px solid #545b5e;border-radius:8px;background:#0b0d0e;color:#fff;padding:11px;font:inherit}
  .emojis{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:8px;margin:12px 0}
  .emojis button{font-size:24px;background:#242627;color:#fff;border:1px solid #525252}
  .readers{list-style:none;padding:0;margin:8px 0}
  .readers li{padding:9px 0;border-bottom:1px solid #333;color:#ececec;overflow-wrap:anywhere}
  .chat-error{border:1px solid #8b433d;border-radius:6px;padding:10px;color:#ffd3cd}
  @media(prefers-reduced-motion:reduce){.chat-overlay{backdrop-filter:none}}
</style>
