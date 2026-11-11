<script lang="ts">
  import {browser} from '$app/environment';
  type Team={id:string;name:string;kind:'team'|'academy_group';active_players:number};
  type Profile={organization_id:'lefteria_fc';team_id:string;
    level:string;description:string;banner_url:string;revision:number};
  let {actorId,groups,onExpired,onRevoked}:{
    actorId:string;groups:Team[];onExpired:()=>void;onRevoked:()=>void
  }=$props();
  const owner='lefteria_fc';
  let teamId=$state('');
  let record=$state<Profile|null>(null);
  let level=$state("Α' Ομάδα");
  let description=$state('');
  let bannerUrl=$state('');
  let loading=$state(false);
  let saving=$state(false);
  let error=$state('');
  let notice=$state('');
  let refresh=$state(0);
  const isValid=(raw:unknown,id:string):raw is Profile=>{
    if(!raw||typeof raw!=='object'||Array.isArray(raw))return false;
    const p=raw as Record<string,unknown>;
    return p.organization_id===owner&&p.team_id===id&&
      typeof p.level==='string'&&p.level.length>0&&p.level.length<=100&&
      typeof p.description==='string'&&p.description.length<=2000&&
      typeof p.banner_url==='string'&&p.banner_url.length<=2048&&
      Number.isSafeInteger(p.revision)&&(p.revision as number)>=0&&
      (p.revision as number)<=1000000000;
  };
  function populate(profile:Profile){
    level=profile.level;description=profile.description;bannerUrl=profile.banner_url;
  }
  $effect(()=>{
    const selected=teamId,actor=actorId,generation=refresh;
    void generation;
    record=null;loading=false;error='';notice='';
    if(!browser||!actor||!groups.some(t=>t.id===selected&&t.kind==='team'))
      return;
    const controller=new AbortController();loading=true;
    void(async()=>{
      try{
        const response=await fetch(
          '/api/admin/football/first-team-profiles/'+encodeURIComponent(selected)+'?'+
          new URLSearchParams({organization_id:owner}),{
            credentials:'same-origin',cache:'no-store',signal:controller.signal
          });
        if(controller.signal.aborted)return;
        if(response.status===401){onExpired();return;}
        if(response.status===403){onRevoked();return;}
        if(!response.ok)throw Error('Το προφίλ της ομάδας δεν είναι διαθέσιμο.');
        const raw:unknown=await response.json();
        if(!isValid(raw,selected))throw Error('Μη επαληθευμένο προφίλ ομάδας.');
        if(!controller.signal.aborted){record=raw;populate(raw);}
      }catch(e){
        if(!controller.signal.aborted)error=e instanceof Error?e.message:'Σφάλμα προφίλ.';
      }finally{if(!controller.signal.aborted)loading=false;}
    })();
    return()=>controller.abort();
  });
  async function save(){
    if(!browser||!record||saving||loading)return;
    const selected=teamId,actor=actorId,revision=record.revision;
    if(level.trim()!==level||level.length<1||level.length>100||
      description.length>2000||bannerUrl.length>2048||
      [level,description,bannerUrl].some(s=>/[\x00-\x1f\x7f]/.test(s))||
      (bannerUrl&&!/^https:\/\/[A-Za-z0-9.-]+(?:\/[A-Za-z0-9_./%?&=-]*)?$/.test(bannerUrl))){
      error='Ελέγξτε το επίπεδο, την περιγραφή και τον ασφαλή σύνδεσμο banner.';
      return;
    }
    saving=true;notice='';error='';
    try{
      const response=await fetch(
        '/api/admin/football/first-team-profiles/'+encodeURIComponent(selected)+'?'+
        new URLSearchParams({organization_id:owner}),{
          method:'PUT',credentials:'same-origin',cache:'no-store',
          headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
          body:JSON.stringify({expected_revision:revision,level,description,
            banner_url:bannerUrl})
        });
      if(selected!==teamId||actor!==actorId)return;
      if(response.status===401){onExpired();return;}
      if(response.status===403){onRevoked();return;}
      if(response.status===409){refresh++;
        throw Error('Η ομάδα άλλαξε. Ελέγξτε τα νέα στοιχεία.');}
      if(!response.ok)throw Error('Η αποθήκευση απέτυχε.');
      const raw:unknown=await response.json();
      if(!isValid(raw,selected)||raw.revision!==revision+1)
        throw Error('Μη επαληθευμένη αποθήκευση.');
      record=raw;populate(raw);notice='Το προφίλ της ομάδας αποθηκεύτηκε.';
    }catch(e){
      if(selected===teamId&&actor===actorId)
        error=e instanceof Error?e.message:'Αδυναμία αποθήκευσης.';
    }finally{saving=false;}
  }
</script>

<section class="admin-panel first-team-profile" data-testid="admin-first-team-profile">
  <p class="admin-eyebrow">ΠΡΩΤΗ ΟΜΑΔΑ · ΠΡΟΦΙΛ</p>
  <h2>Στοιχεία ομάδας</h2>
  <p class="admin-muted">Τα πεδία επιπέδου, περιγραφής και banner ακολουθούν
    το αρχικό Lefteria FC. Οι παίκτες και οι προπονητές διαχειρίζονται
    από τους ξεχωριστούς επαληθευμένους καταλόγους.</p>
  <label for="first-team-profile-selection">Επιλέξτε ομάδα</label>
  <select id="first-team-profile-selection" bind:value={teamId} disabled={saving}>
    <option value="">Επιλέξτε ομάδα</option>
    {#each groups.filter(g=>g.kind==='team') as group (group.id)}
      <option value={group.id}>{group.name}</option>
    {/each}
  </select>
  {#if loading}<p role="status">Φόρτωση…</p>{/if}
  {#if error}<p role="alert">{error}</p>{/if}
  {#if notice}<p role="status">{notice}</p>{/if}
  {#if record}
    <form class="first-team-fields" onsubmit={e=>{e.preventDefault();void save();}}>
      <label>Επίπεδο ομάδας
        <input type="text" maxlength="100" bind:value={level} disabled={saving} required/>
      </label>
      <label>Περιγραφή
        <textarea rows="4" maxlength="2000" bind:value={description} disabled={saving}/>
      </label>
      <label>Banner ομάδας (HTTPS)
        <input type="url" maxlength="2048" bind:value={bannerUrl} disabled={saving}
          placeholder="https://..."/>
      </label>
      <div class="first-team-buttons">
        <button class="admin-secondary" type="submit" disabled={saving}>
          {saving?'Αποθήκευση…':'Αποθήκευση στοιχείων'}
        </button>
        <button type="button" class="admin-secondary" disabled={saving}
          onclick={()=>refresh++}>Επαναφόρτωση</button>
      </div>
    </form>
  {/if}
</section>
<style>
.first-team-profile{display:grid;gap:12px;margin:18px 0}
.first-team-profile>select{max-width:500px}
.first-team-fields{display:grid;gap:14px;max-width:760px}
.first-team-fields label{display:grid;gap:6px}
.first-team-fields input,.first-team-fields textarea{width:100%}
.first-team-buttons{display:flex;gap:10px;flex-wrap:wrap}
</style>