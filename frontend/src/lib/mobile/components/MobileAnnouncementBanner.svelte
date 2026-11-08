<script lang="ts">
  import {onMount} from 'svelte';
  import type {MobileAuthController} from '../controller.ts';
  import type {GroupChatRoom} from '../group-chat.ts';
  import type {AppAnnouncementMedia} from '../app-announcements.ts';

  let {controller,room,announcementId,media}:{
    controller:MobileAuthController;room:GroupChatRoom;
    announcementId:string;media:AppAnnouncementMedia
  }=$props();
  let node:HTMLElement;
  let src=$state<string|null>(null);
  let failed=$state(false);
  let started=false,alive=true;

  onMount(()=>{
    alive=true;
    const observer=new IntersectionObserver(entries=>{
      if(started||!entries.some(e=>e.isIntersecting))return;
      started=true;
      void controller.getAppAnnouncementMedia(room,announcementId,media).then(blob=>{
        const url=URL.createObjectURL(blob);
        if(!alive){URL.revokeObjectURL(url);return;}
        src=url;
      }).catch(()=>{if(alive)failed=true;});
    },{rootMargin:'100px',threshold:0.01});
    observer.observe(node);
    return()=>{
      alive=false;
      observer.disconnect();
      if(src)URL.revokeObjectURL(src);
    };
  });
</script>

<figure class="banner" bind:this={node} aria-label="Εικόνα ανακοίνωσης">
  {#if src}
    <img src={src} alt="Εικόνα ανακοίνωσης" decoding="async" loading="lazy"/>
  {:else if failed}
    <figcaption role="status">Η εικόνα δεν είναι διαθέσιμη.</figcaption>
  {:else}
    <figcaption>Φόρτωση εικόνας ανακοίνωσης…</figcaption>
  {/if}
</figure>
<style>
  .banner{margin:12px 0;border-radius:12px;overflow:hidden;
    background:var(--lf-elevated);border:1px solid var(--lf-line)}
  img{display:block;max-height:360px;aspect-ratio:16/9;object-fit:cover;width:100%}
  figcaption{font-size:12px;color:var(--lf-muted);padding:14px}
</style>
