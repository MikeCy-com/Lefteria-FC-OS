<script lang="ts">
  import {browser} from '$app/environment';

  type Group={id:string;name:string;kind:'team'|'academy_group';active_players:number};
  type Profile={
    organization_id:'lefteria_fc_academy';group_id:string;revision:number;
    age_range:string;coach_name:string;training_schedule:string;
    description:string;max_players:number;season:string;
    banner_url:string;display_order:number;
  };
  let {actorId,groups,onExpired,onRevoked}:{
    actorId:string;groups:Group[];onExpired:()=>void;onRevoked:()=>void
  }=$props();
  const owner='lefteria_fc_academy';
  let selected=$state('');
  let record=$state<Profile|null>(null);
  let loading=$state(false);
  let saving=$state(false);
  let error=$state('');
  let saved=$state('');
  let refresh=$state(0);
  let ageRange=$state('');
  let coachName=$state('');
  let trainingSchedule=$state('');
  let description=$state('');
  let maxPlayers=$state(25);
  let season=$state('');
  let bannerUrl=$state('');
  let displayOrder=$state(0);
  const group=$derived(groups.find(g=>g.id===selected));
  const valid=(raw:unknown,id:string):raw is Profile=>{
    if(!raw||typeof raw!=='object'||Array.isArray(raw))return false;
    const p=raw as Record<string,unknown>;
    return p.organization_id===owner&&p.group_id===id&&
      Number.isSafeInteger(p.revision)&&(p.revision as number)>=0&&
      (p.revision as number)<=1000000000&&
      ['age_range','coach_name','training_schedule','description',
        'season','banner_url'].every(k=>typeof p[k]==='string')&&
      (p.age_range as string).length<=80&&
      (p.coach_name as string).length<=120&&
      (p.training_schedule as string).length<=500&&
      (p.description as string).length<=2000&&
      (p.season as string).length<=40&&
      (p.banner_url as string).length<=2048&&
      Number.isSafeInteger(p.max_players)&&(p.max_players as number)>=1&&
      (p.max_players as number)<=200&&
      Number.isSafeInteger(p.display_order)&&(p.display_order as number)>=0&&
      (p.display_order as number)<=10000;
  };
  function populate(p:Profile){
    ageRange=p.age_range;coachName=p.coach_name;
    trainingSchedule=p.training_schedule;description=p.description;
    maxPlayers=p.max_players;season=p.season;
    bannerUrl=p.banner_url;displayOrder=p.display_order;
  }
  $effect(()=>{
    const id=selected,actor=actorId,generation=refresh;
    void generation;
    record=null;error='';saved='';loading=false;
    if(!browser||!actor||!groups.some(g=>g.id===id&&g.kind==='academy_group'))
      return;
    const controller=new AbortController();loading=true;
    void(async()=>{
      try{
        const response=await fetch(
          '/api/admin/football/academy-group-profiles/'+encodeURIComponent(id)+'?'+
          new URLSearchParams({organization_id:owner}),{
            cache:'no-store',credentials:'same-origin',signal:controller.signal
          });
        if(controller.signal.aborted)return;
        if(response.status===401){onExpired();return;}
        if(response.status===403){onRevoked();return;}
        if(!response.ok)throw Error('Δεν ήταν δυνατή η φόρτωση του τμήματος.');
        const raw:unknown=await response.json();
        if(!valid(raw,id))throw Error('Μη επαληθευμένα στοιχεία τμήματος.');
        if(!controller.signal.aborted){
          record=raw;populate(raw);
        }
      }catch(e){
        if(!controller.signal.aborted)error=e instanceof Error?e.message:'Σφάλμα τμήματος.';
      }finally{if(!controller.signal.aborted)loading=false;}
    })();
    return()=>controller.abort();
  });
  async function save(){
    if(!browser||!record||!group||saving||loading)return;
    const id=selected,actor=actorId,revision=record.revision;
    if(!Number.isSafeInteger(maxPlayers)||maxPlayers<1||maxPlayers>200||
       !Number.isSafeInteger(displayOrder)||displayOrder<0||displayOrder>10000||
       maxPlayers<group.active_players){
      error='Ελέγξτε τη χωρητικότητα και τη σειρά εμφάνισης.';
      return;
    }
    const fields=[ageRange,coachName,trainingSchedule,description,season,bannerUrl];
    if(fields.some(s=>/[\x00-\x1f\x7f]/.test(s))||
       ageRange.length>80||coachName.length>120||
       trainingSchedule.length>500||description.length>2000||
       season.length>40||bannerUrl.length>2048){
      error='Ελέγξτε τα πεδία τμήματος και τα επιτρεπτά μήκη.';
      return;
    }
    if(bannerUrl&&!/^https:\/\/[A-Za-z0-9.-]+(?:\/[A-Za-z0-9_./%?&=-]*)?$/.test(bannerUrl)){
      error='Χρησιμοποιήστε μόνο ασφαλή σύνδεσμο HTTPS για το banner.';
      return;
    }
    saving=true;error='';saved='';
    try{
      const response=await fetch(
        '/api/admin/football/academy-group-profiles/'+encodeURIComponent(id)+'?'+
        new URLSearchParams({organization_id:owner}),{
          method:'PUT',cache:'no-store',credentials:'same-origin',
          headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
          body:JSON.stringify({expected_revision:revision,age_range:ageRange,
            coach_name:coachName,training_schedule:trainingSchedule,
            description,max_players:maxPlayers,season,banner_url:bannerUrl,
            display_order:displayOrder})
        });
      if(id!==selected||actor!==actorId)return;
      if(response.status===401){onExpired();return;}
      if(response.status===403){onRevoked();return;}
      if(response.status===409){refresh++;throw Error(
        'Τα στοιχεία ενημερώθηκαν από άλλον χρήστη ή ξεπερνούν τη χωρητικότητα.');}
      if(!response.ok)throw Error('Η αποθήκευση δεν ολοκληρώθηκε.');
      const raw:unknown=await response.json();
      if(!valid(raw,id)||raw.revision!==revision+1)
        throw Error('Μη επαληθευμένη ενημέρωση τμήματος.');
      record=raw;populate(raw);saved='Οι πληροφορίες του τμήματος αποθηκεύτηκαν.';
    }catch(e){
      if(id===selected&&actor===actorId)
        error=e instanceof Error?e.message:'Σφάλμα αποθήκευσης.';
    }finally{saving=false;}
  }
</script>

<section class="admin-panel academy-profile" data-testid="admin-academy-group-profile">
  <p class="admin-eyebrow">ΑΚΑΔΗΜΙΑ · ΛΕΠΤΟΜΕΡΕΙΕΣ ΤΜΗΜΑΤΟΣ</p>
  <h2>Προφίλ τμήματος Ακαδημίας</h2>
  <p class="admin-muted">Τα πεδία ακολουθούν τον αρχικό πίνακα Lefteria FC. Οι αναθέσεις
    παικτών και προπονητών παραμένουν ανεξάρτητες και προστατευμένες.</p>
  <label for="academy-profile-group">Τμήμα</label>
  <select id="academy-profile-group" bind:value={selected} disabled={saving}>
    <option value="">Επιλέξτε τμήμα</option>
    {#each groups.filter(g=>g.kind==='academy_group') as item (item.id)}
      <option value={item.id}>{item.name}</option>
    {/each}
  </select>
  {#if loading}<p role="status">Φόρτωση προφίλ…</p>{/if}
  {#if error}<p role="alert">{error}</p>{/if}
  {#if saved}<p role="status">{saved}</p>{/if}
  {#if record&&group}
    <form onsubmit={e=>{e.preventDefault();void save();}} class="academy-profile-form">
      <label>Ηλικιακή ομάδα
        <input type="text" maxlength="80" bind:value={ageRange} disabled={saving}/>
      </label>
      <label>Προπονητής (όνομα εμφάνισης)
        <input type="text" maxlength="120" bind:value={coachName} disabled={saving}/>
      </label>
      <label>Πρόγραμμα προπονήσεων
        <textarea rows="3" maxlength="500" bind:value={trainingSchedule} disabled={saving}/>
      </label>
      <label>Περιγραφή
        <textarea rows="4" maxlength="2000" bind:value={description} disabled={saving}/>
      </label>
      <label>Μέγιστος αριθμός παικτών
        <input type="number" min={Math.max(1,group.active_players)} max="200"
          step="1" bind:value={maxPlayers} disabled={saving} required/>
      </label>
      <label>Αγωνιστική περίοδος
        <input type="text" maxlength="40" bind:value={season} disabled={saving}/>
      </label>
      <label>Διεύθυνση banner (HTTPS)
        <input type="url" maxlength="2048" bind:value={bannerUrl} disabled={saving}
          placeholder="https://..." />
      </label>
      <label>Σειρά εμφάνισης
        <input type="number" min="0" max="10000" step="1"
          bind:value={displayOrder} disabled={saving} required/>
      </label>
      <div class="academy-profile-actions">
        <button class="admin-secondary" type="submit" disabled={saving}>
          {saving?'Αποθήκευση…':'Αποθήκευση προφίλ'}
        </button>
        <button class="admin-secondary" type="button" disabled={saving}
          onclick={()=>refresh++}>Επαναφόρτωση</button>
      </div>
    </form>
  {/if}
</section>
<style>
  .academy-profile{display:grid;gap:12px;margin:18px 0}
  .academy-profile>select{max-width:500px}
  .academy-profile-form{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:15px;max-width:900px}
  .academy-profile-form label{display:grid;gap:6px;font-size:13px}
  .academy-profile-form input,.academy-profile-form textarea{width:100%}
  .academy-profile-actions{display:flex;gap:10px;align-items:center;grid-column:1/-1}
  @media(max-width:700px){.academy-profile-form{grid-template-columns:1fr}}
</style>
