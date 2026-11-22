<script lang="ts">
  import {browser} from '$app/env';
  import {untrack} from 'svelte';

  type Profile={
    organization_id:'lefteria_fc_academy';player_id:string;official_name:string;
    official_number:number|null;official_position:string|null;
    biography:string;preferred_position:string;preferred_number:number|null;
    has_portrait:boolean;revision:number;updated_at:string|null;
  };

  let {playerId,playerName,actorId,allowWrite=false,onExpired,onRevoked}:{
    playerId:string;playerName:string;actorId:string;allowWrite?:boolean;
    onExpired:()=>void;onRevoked:()=>void;
  }=$props();
  let profile=$state<Profile|null>(null);
  let biography=$state('');
  let preferredPosition=$state('');
  let preferredNumber=$state('');
  let selectedFile=$state<File|null>(null);
  let loading=$state(false),saving=$state(false),uploading=$state(false);
  let notice=$state(''),error=$state(''),revision=$state(0);
  let photoLoaded=$state(false);
  const base=$derived('/api/admin/football/players/'+encodeURIComponent(playerId)+
    '/private-profile');
  const url=$derived(base+'?organization_id=lefteria_fc_academy');
  const photoUrl=$derived(base+'/photo?organization_id=lefteria_fc_academy&revision='+
    (profile?.revision??0));
  const canUpload=$derived(allowWrite&&selectedFile!==null&&
    selectedFile.size>0&&selectedFile.size<=5*1024*1024);

  function valid(value:unknown,id:string):value is Profile {
    if(!value||typeof value!=='object'||Array.isArray(value))return false;
    const p=value as Record<string,unknown>;
    return p.organization_id==='lefteria_fc_academy'&&p.player_id===id&&
      typeof p.official_name==='string'&&p.official_name.length<=240&&
      (p.official_number===null||Number.isInteger(p.official_number))&&
      (p.official_position===null||typeof p.official_position==='string')&&
      typeof p.biography==='string'&&p.biography.length<=1000&&
      typeof p.preferred_position==='string'&&p.preferred_position.length<=80&&
      (p.preferred_number===null||(Number.isInteger(p.preferred_number)&&
        (p.preferred_number as number)>=1&&(p.preferred_number as number)<=99))&&
      typeof p.has_portrait==='boolean'&&Number.isInteger(p.revision)&&
      (p.revision as number)>=0&&
      (p.updated_at===null||typeof p.updated_at==='string');
  }

  async function request(target:string,options?:RequestInit):Promise<Response> {
    const response=await fetch(target,{
      credentials:'same-origin',cache:'no-store',...options
    });
    if(response.status===401){onExpired();throw new Error('Η σύνδεση έληξε.');}
    if(response.status===403){onRevoked();throw new Error('Η πρόσβαση στο προφίλ άλλαξε.');}
    if(!response.ok)throw new Error(response.status===409
      ?'Το προφίλ άλλαξε από άλλον διαχειριστή. Ανανεώστε πριν αποθηκεύσετε.'
      :'Δεν ολοκληρώθηκε η ενέργεια στο ιδιωτικό προφίλ.');
    return response;
  }

  $effect(()=>{
    const id=playerId,actor=actorId,refresh=revision;
    void refresh;
    profile=null;selectedFile=null;loading=false;error='';photoLoaded=false;
    if(!browser||!id||!actor)return;
    const abort=new AbortController();
    loading=true;
    untrack(()=>void(async()=>{
      try{
        const response=await request(url,{signal:abort.signal});
        const data:unknown=await response.json();
        if(!valid(data,id))throw new Error('Το προφίλ δεν επαληθεύτηκε.');
        if(abort.signal.aborted||playerId!==id||actorId!==actor)return;
        profile=data;biography=data.biography;preferredPosition=data.preferred_position;
        preferredNumber=data.preferred_number?.toString()??'';
      }catch(e){if(!abort.signal.aborted)error=e instanceof Error?e.message:'Το προφίλ δεν είναι διαθέσιμο.';}
      finally{if(!abort.signal.aborted)loading=false;}
    })());
    return()=>abort.abort();
  });

  async function save(){
    if(!profile||!allowWrite||saving||uploading||loading)return;
    const bio=biography.trim(),position=preferredPosition.trim();
    const number=preferredNumber.trim()?Number(preferredNumber):null;
    if(bio.length>1000||position.length>80||bio.includes('\r')||
      (number!==null&&(!Number.isInteger(number)||number<1||number>99))){
      error='Ελέγξτε το κείμενο, τη θέση και τον αριθμό 1–99.';return;
    }
    saving=true;error='';notice='';
    try{
      const response=await request(url,{
        method:'PUT',headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({expectedRevision:profile.revision,biography:bio,
          preferredPosition:position,preferredNumber:number})
      });
      const json:unknown=await response.json();
      if(!json||typeof json!=='object'||Array.isArray(json)||
        !('player_id' in json)||json.player_id!==playerId||
        !('revision' in json)||typeof json.revision!=='number')
        throw new Error('Η αποθήκευση δεν επιβεβαιώθηκε.');
      notice='Το ιδιωτικό προφίλ αποθηκεύτηκε.';
      revision++;
    }catch(e){error=e instanceof Error?e.message:'Η αποθήκευση απέτυχε.';}
    finally{saving=false;}
  }

  async function changePortrait(remove=false){
    if(!allowWrite||uploading||saving||loading||(!remove&&!canUpload))return;
    if(remove&&!window.confirm('Να αφαιρεθεί η ιδιωτική φωτογραφία αυτού του παιδιού;'))return;
    uploading=true;error='';notice='';
    try{
      let options:RequestInit;
      if(remove)options={method:'DELETE',headers:{'X-Lefteria-Admin':'1'}};
      else {
        const body=new FormData();
        body.append('file',selectedFile!);
        options={method:'POST',headers:{'X-Lefteria-Admin':'1'},body};
      }
      await request(base+'/photo?organization_id=lefteria_fc_academy',options);
      selectedFile=null;
      notice=remove?'Η φωτογραφία αφαιρέθηκε.':'Η ιδιωτική φωτογραφία αποθηκεύτηκε χωρίς EXIF.';
      revision++;
    }catch(e){error=e instanceof Error?e.message:'Η εικόνα δεν αποθηκεύτηκε.';}
    finally{uploading=false;}
  }
</script>

<section class="admin-player-private-profile admin-panel"
  data-testid="admin-player-private-profile" aria-label={'Ιδιωτικό προφίλ '+playerName}>
  <header class="admin-player-private-head">
    <div>
      <p class="admin-eyebrow">ΑΚΑΔΗΜΙΑ · ΙΔΙΩΤΙΚΟ ΠΡΟΦΙΛ ΑΘΛΗΤΗ</p>
      <h3>{playerName}</h3>
      <p class="admin-muted">Αυτός ο φάκελος είναι ορατός μόνο σε εξουσιοδοτημένο προσωπικό.
        Δεν δημοσιεύει φωτογραφία, βιογραφικό ή προσωπικά στοιχεία στο κοινό.</p>
    </div>
    <button type="button" class="admin-secondary" disabled={loading||saving||uploading}
      onclick={()=>revision++}>Ανανέωση προφίλ</button>
  </header>
  {#if loading}
    <p role="status">Επαλήθευση δικαιωμάτων και φόρτωση στοιχείων…</p>
  {:else if error && !profile}
    <p role="alert">{error}</p>
  {:else if profile}
    <div class="admin-private-profile-grid">
      <div class="admin-player-portrait-editor">
        <div class="admin-player-private-portrait" aria-label="Φωτογραφία αθλητή">
          {#if !photoLoaded}<span aria-hidden="true">{profile.official_name.slice(0,1)}</span>{/if}
          {#if profile.has_portrait}
            {#key photoUrl}
              <img alt={'Ιδιωτική φωτογραφία '+profile.official_name}
                src={photoUrl} hidden={!photoLoaded}
                onload={()=>photoLoaded=true} onerror={()=>photoLoaded=false}/>
            {/key}
          {/if}
        </div>
        {#if allowWrite}
          <label>Ανέβασμα φωτογραφίας
            <input type="file" accept="image/jpeg,image/png,image/gif,image/webp"
              disabled={saving||uploading}
              onchange={event=>selectedFile=event.currentTarget.files?.[0]??null}/>
          </label>
          <p class="admin-muted">Μέχρι 5 MB. Η εικόνα μετατρέπεται σε ιδιωτική JPEG χωρίς μεταδεδομένα EXIF.</p>
          <button type="button" class="admin-secondary" disabled={!canUpload||uploading||saving}
            data-testid="admin-player-upload-private-photo" onclick={()=>void changePortrait()}>
            {uploading?'Μεταφόρτωση…':'Αποθήκευση φωτογραφίας'}
          </button>
          {#if profile.has_portrait}
            <button type="button" class="admin-secondary" disabled={uploading||saving}
              onclick={()=>void changePortrait(true)}>Αφαίρεση φωτογραφίας</button>
          {/if}
        {/if}
      </div>
      <div class="admin-player-private-fields">
        <div class="admin-player-official">
          <strong>Επίσημα στοιχεία εγγραφής</strong>
          <span>{profile.official_name}</span>
          <span>Αριθμός: {profile.official_number??'—'} · Θέση: {profile.official_position??'—'}</span>
          <small>Τα στοιχεία εγγραφής διατηρούνται χωριστά για προστασία των αγώνων, γονέων και παραστατικών.</small>
        </div>
        <label>Αθλητικό βιογραφικό
          <textarea bind:value={biography} maxlength="1000" rows="5"
            disabled={!allowWrite||saving||uploading}
            placeholder="Σύντομη παρουσίαση, δυνατά σημεία και στόχοι ανάπτυξης"></textarea>
        </label>
        <div class="admin-player-preferences">
          <label>Προτιμώμενη θέση
            <input bind:value={preferredPosition} maxlength="80"
              disabled={!allowWrite||saving||uploading} placeholder="Π.χ. Επιθετικός"/>
          </label>
          <label>Προτιμώμενος αριθμός
            <input bind:value={preferredNumber} inputmode="numeric"
              pattern="[0-9]{1,2}" maxlength="2"
              disabled={!allowWrite||saving||uploading} placeholder="1–99"/>
          </label>
        </div>
        {#if profile.updated_at}
          <small>Τελευταία ενημέρωση: {new Date(profile.updated_at).toLocaleString('el-CY')}</small>
        {/if}
        {#if allowWrite}
          <button type="button" class="admin-primary"
            data-testid="admin-player-save-private-profile"
            disabled={saving||uploading||loading} onclick={()=>void save()}>
            {saving?'Αποθήκευση…':'Αποθήκευση προφίλ'}
          </button>
        {/if}
      </div>
    </div>
    {#if error}<p role="alert">{error}</p>{/if}
    {#if notice}<p role="status">{notice}</p>{/if}
  {/if}
</section>

<style>
  .admin-player-private-profile{display:grid;gap:15px}
  .admin-player-private-head{display:flex;flex-wrap:wrap;align-items:start;
    justify-content:space-between;gap:12px}
  .admin-player-private-head h3{margin:4px 0}
  .admin-player-private-profile .admin-muted{font-size:12px;margin:0;line-height:1.5}
  .admin-private-profile-grid{display:grid;grid-template-columns:minmax(170px,220px) 1fr;
    gap:20px;align-items:start}
  .admin-player-portrait-editor{display:grid;gap:12px}
  .admin-player-private-portrait{height:156px;width:156px;border-radius:18px;
    display:grid;place-items:center;overflow:hidden;background:var(--admin-surface,#17191a);
    border:1px solid var(--admin-accent,#f6bb38)}
  .admin-player-private-portrait span{font-size:54px;font-weight:800;color:var(--admin-accent,#f6bb38)}
  .admin-player-private-portrait img{grid-area:1/1;width:100%;height:100%;object-fit:cover}
  .admin-player-private-portrait img[hidden]{display:none}
  .admin-player-portrait-editor label,.admin-player-private-fields label{display:grid;gap:7px;
    font-size:13px}
  .admin-player-portrait-editor input[type="file"]{width:100%;max-width:100%}
  .admin-player-private-fields{display:grid;gap:15px}
  .admin-player-private-fields textarea,.admin-player-private-fields input{width:100%;
    min-height:42px;background:var(--admin-surface,#151719);color:inherit;
    border:1px solid var(--admin-border,#5a5f61);border-radius:8px;padding:10px}
  .admin-player-official{display:grid;gap:6px;padding:12px;
    border:1px solid var(--admin-border,#505456);border-radius:10px}
  .admin-player-official small{font-size:11px;opacity:.8}
  .admin-player-preferences{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px}
  .admin-player-private-fields button{justify-self:start;min-height:43px}
  @media(max-width:650px){.admin-private-profile-grid{grid-template-columns:1fr}
    .admin-player-preferences{grid-template-columns:1fr}}
</style>
