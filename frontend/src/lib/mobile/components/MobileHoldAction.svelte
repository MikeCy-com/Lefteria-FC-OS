<script lang="ts">
  import type {Snippet} from 'svelte';
  let {children,onOpen,enabled=true,label='Επιλογές μηνύματος'}:{
    children:Snippet;
    onOpen:()=>void;
    enabled?:boolean;
    label?:string;
  }=$props();

  let timer:ReturnType<typeof setTimeout>|null=null;
  let origin:{x:number;y:number}|null=null;
  let held=false;
  let suppressClick=false;
  let pointerId:number|null=null;

  function cancel(){
    if(timer!==null){clearTimeout(timer);timer=null;}
    origin=null;pointerId=null;
  }
  $effect(()=>{if(!enabled)cancel();return cancel;});
  function down(event:PointerEvent){
    cancel();held=false;suppressClick=false;
    if(!enabled || event.button!==0 ||
      (event.target instanceof Element &&
        event.target.closest('[data-chat-menu-trigger],button,a,input,textarea,select')))return;
    pointerId=event.pointerId;
    origin={x:event.clientX,y:event.clientY};
    timer=setTimeout(()=>{
      if(origin && enabled){
        timer=null;held=true;suppressClick=true;origin=null;
        onOpen();
      }
    },550);
  }
  function move(event:PointerEvent){
    if(origin && pointerId===event.pointerId &&
      Math.hypot(event.clientX-origin.x,event.clientY-origin.y)>10)cancel();
  }
  function up(event:PointerEvent){
    if(pointerId===event.pointerId)cancel();
  }
  function context(event:MouseEvent){
    if(!enabled)return;
    event.preventDefault();cancel();suppressClick=true;
    onOpen();
  }
  function key(event:KeyboardEvent){
    if(enabled && (event.key==='ContextMenu' || (event.shiftKey && event.key==='F10'))){
      event.preventDefault();cancel();onOpen();
    }
  }
  function clicked(event:MouseEvent){
    if(held || suppressClick){
      event.preventDefault();event.stopPropagation();
      held=false;suppressClick=false;
    }
  }
</script>

<div class="chat-hold-action" data-testid="mobile-chat-hold-target" data-enabled={enabled}
  onpointerdown={down} onpointermove={move} onpointerup={up}
  onpointercancel={cancel} onpointerleave={cancel}
  oncontextmenu={context} onkeydown={key} onclickcapture={clicked}
  role="group" aria-label={label}>
  <div class="chat-hold-content">{@render children()}</div>
  {#if enabled}
    <button type="button" class="chat-menu-trigger" data-chat-menu-trigger="true"
      aria-haspopup="dialog" aria-label={label} onclick={onOpen}
      data-testid="mobile-chat-menu-trigger">•••</button>
  {/if}
</div>
<style>
  .chat-hold-action{display:flex;align-items:center;gap:6px;min-width:0;touch-action:pan-y}
  .chat-hold-content{min-width:0;flex:1;overflow-wrap:anywhere}
  .chat-menu-trigger{display:grid;place-items:center;flex:0 0 38px;min-height:44px;
    border:0;border-radius:8px;background:transparent;color:#b6b7ba;
    font-size:16px;letter-spacing:2px;cursor:pointer}
  .chat-menu-trigger:is(:hover,:focus-visible){background:#f5a6231c;color:#ffca46}
  .chat-menu-trigger:focus-visible{outline:2px solid #ffca46;outline-offset:2px}
</style>
