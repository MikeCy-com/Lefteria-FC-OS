<script lang="ts">
  import {adminPhotoRevision} from './photo-revision.ts';
  let {initials,actorId,size='small'}:{
    initials:string;actorId:string;size?:'small'|'large'
  }=$props();
  let loaded=$state(false);
  // A different signed-in person must never retain the previous portrait
  // in a long-lived shared header or profile.
  $effect(()=>{
    void actorId;void $adminPhotoRevision;
    loaded=false;
  });
</script>
<div class="admin-photo-avatar" class:large={size==='large'}
  aria-label="Εικόνα λογαριασμού" data-testid="admin-private-photo-avatar">
  {#if !loaded}<span aria-hidden="true">{initials}</span>{/if}
  {#key actorId}
    <img alt="" src={'/api/admin/auth/profile/photo?revision='+$adminPhotoRevision}
      hidden={!loaded} onload={()=>loaded=true} onerror={()=>loaded=false}/>
  {/key}
</div>
<style>
  .admin-photo-avatar{
    display:grid;place-items:center;flex:none;overflow:hidden;
    width:38px;height:38px;border-radius:50%;
    background:linear-gradient(140deg,#f7c75a,#aa7820);color:#101010;
    font:900 17px/1 'Fira Sans Extra Condensed',sans-serif;
  }
  .admin-photo-avatar.large{width:96px;height:96px;font-size:37px}
  img{grid-area:1/1;width:100%;height:100%;object-fit:cover;border-radius:50%}
  img[hidden]{display:none}
  span{grid-area:1/1}
</style>
