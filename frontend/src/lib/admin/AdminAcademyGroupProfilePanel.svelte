<script lang="ts">

  type Group={id:string;name:string;kind:'team'|'academy_group';active_players:number};
  type Profile={
    organization_id:'lefteria_fc_academy';group_id:string;revision:number;
    age_range:string;coach_name:string;training_schedule:string;
    description:string;max_players:number;season:string;
    banner_url:string;display_order:number;
    is_public:boolean;
  };
  let {actorId,groups,onExpired,onRevoked}:{
    actorId:string;groups:Group[];onExpired:()=>void;onRevoked:()=>void
  }=$props();
  const owner='lefteria_fc_academy';
  let selected=$state('');
  $effect(()=>{
    const eligible=groups.filter(g=>g.kind==='academy_group');
    if(eligible.length===1&&selected!==eligible[0].id)selected=eligible[0].id;
    else if(!eligible.some(g=>g.id===selected))selected='';
  });
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
  let publiclyVisible=$state(false);
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
      (p.display_order as number)<=10000 &&
      typeof p.is_public==='boolean';
  };
  function populate(p:Profile){
    ageRange=p.age_range;coachName=p.coach_name;
    trainingSchedule=p.training_schedule;description=p.description;
    maxPlayers=p.max_players;season=p.season;
    bannerUrl=p.banner_url;displayOrder=p.display_order;
    publiclyVisible=p.is_public;
  }
  $effect(()=>{
    const id=selected,actor=actorId,generation=refresh;
    void generation;
    record=null;error='';saved='';loading=false;
    if(typeof window === 'undefined'||!actor||!groups.some(g=>g.id===id&&g.kind==='academy_group'))
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
    if(typeof window === 'undefined'||!record||!group||saving||loading)return;
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
  async function saveVisibility(){
    if(typeof window==='undefined'||!record||!group||saving||loading||
       record.revision<1||record.is_public===publiclyVisible)return;
    const id=selected,actor=actorId,revision=record.revision,next=publiclyVisible;
    saving=true;error='';saved='';
    try{
      const response=await fetch(
        '/api/admin/football/academy-group-profiles/'+encodeURIComponent(id)+
        '/visibility?'+new URLSearchParams({organization_id:owner}),{
          method:'PUT',cache:'no-store',credentials:'same-origin',
          headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
          body:JSON.stringify({expected_revision:revision,is_public:next})
        });
      if(id!==selected||actor!==actorId)return;
      if(response.status===401){onExpired();return;}
      if(response.status===403){onRevoked();return;}
      if(response.status===409){refresh++;throw Error(
        'Η προβολή του τμήματος ενημερώθηκε από άλλο διαχειριστή. Ανανέωση στοιχείων.');}
      if(!response.ok)throw Error('Η κατάσταση δημοσίευσης δεν αποθηκεύτηκε.');
      const raw:unknown=await response.json();
      if(!valid(raw,id)||raw.revision!==revision+1||raw.is_public!==next)
        throw Error('Μη επαληθευμένη αλλαγή δημοσίευσης.');
      record=raw;populate(raw);
      saved=next?'Το τμήμα δημοσιεύτηκε στην ιστοσελίδα.':
        'Το τμήμα αποσύρθηκε από τον δημόσιο κατάλογο.';
    }catch(e){
      if(id===selected&&actor===actorId)
        error=e instanceof Error?e.message:'Σφάλμα προβολής τμήματος.';
    }finally{saving=false;}
  }
</script>

<section class="admin-panel academy-profile" data-testid="admin-academy-group-profile">
  <p class="admin-eyebrow">ΑΚΑΔΗΜΙΑ · ΛΕΠΤΟΜΕΡΕΙΕΣ ΤΜΗΜΑΤΟΣ</p>
  <h2>Προφίλ τμήματος Ακαδημίας</h2>
  <p class="admin-muted">Τα πεδία ακολουθούν τον αρχικό πίνακα Lefteria FC. Οι αναθέσεις
    παικτών και προπονητών παραμένουν ανεξάρτητες και προστατευμένες.</p>
  {#if groups.filter(g=>g.kind==='academy_group').length>1}
  <label for="academy-profile-group">Τμήμα</label>
  <select id="academy-profile-group" bind:value={selected} disabled={saving}>
    <option value="">Επιλέξτε τμήμα</option>
    {#each groups.filter(g=>g.kind==='academy_group') as item (item.id)}
      <option value={item.id}>{item.name}</option>
    {/each}
  </select>
  {:else if groups.length===1}<p class="admin-muted" data-testid="group-profile-current-name"><strong>{groups[0].name}</strong></p>{/if}
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
      {#if record.banner_url&&/^https:\/\/[A-Za-z0-9.-]+(?:\/[A-Za-z0-9_./%?&=-]*)?$/.test(record.banner_url)}
        <div class="group-banner-preview"><span>Τρέχον banner</span><img src={record.banner_url} alt="Banner ομάδας" loading="lazy" referrerpolicy="no-referrer"/></div>
      {/if}
      <label>Σειρά εμφάνισης
        <input type="number" min="0" max="10000" step="1"
          bind:value={displayOrder} disabled={saving} required/>
      </label>
      <div class="academy-publication" data-testid="academy-group-publication-controls">
        <label class="academy-visibility-choice" for="academy-group-visible">
          <input id="academy-group-visible" type="checkbox" bind:checked={publiclyVisible}
            disabled={saving||loading||record.revision<1} />
          Δημόσια εμφάνιση τμήματος στην ιστοσελίδα
        </label>
        <p>Η απόκρυψη αφαιρεί το τμήμα από τη δημόσια Ακαδημία και τους συνδέσμους
          προς την καρτέλα του. Δεν διαγράφει παίκτες, προπονήσεις, αναθέσεις ή ιστορικά δεδομένα.</p>
        <button class="admin-secondary" type="button" data-testid="academy-group-visibility-save"
          disabled={saving||loading||record.revision<1||record.is_public===publiclyVisible}
          onclick={()=>void saveVisibility()}>
          {saving?'Αποθήκευση…':'Ενημέρωση δημόσιας προβολής'}
        </button>
        <p role="status" class="academy-visibility-status">
          {record.is_public?'ΔΗΜΟΣΙΕΥΜΕΝΟ':'ΜΗ ΔΗΜΟΣΙΕΥΜΕΝΟ'}
        </p>
      </div>
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
  .academy-profile-form{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:15px;width:100%;max-width:none;min-width:0}
  .academy-profile-form label{display:grid;gap:6px;font-size:13px}
  .academy-profile-form input,.academy-profile-form textarea{width:100%}
  .academy-profile-actions{display:flex;gap:10px;align-items:center;grid-column:1/-1}
  .academy-publication{display:grid;gap:10px;grid-column:1/-1;padding:17px;border:1px solid #80662b;background:#15130e}
  .academy-publication .academy-visibility-choice{display:flex;gap:12px;align-items:center;font-size:16px;font-weight:800}
  .academy-visibility-choice input{width:20px;height:20px;accent-color:#eac15f;flex-shrink:0}
  .academy-publication>p{line-height:1.55;color:#d0d0d0;font-size:13px;margin:0}
  .academy-publication .academy-visibility-status{font-weight:800;color:#eac15f}
  .academy-publication button{justify-self:start;min-height:44px}
  @media(max-width:700px){.academy-profile-form{grid-template-columns:1fr}.academy-publication button{width:100%}}
.group-banner-preview{grid-column:1/-1;display:grid;gap:8px;max-width:100%;min-width:0}
.group-banner-preview img{width:100%;max-height:260px;object-fit:contain;background:var(--admin-surface);border-radius:8px}
</style>
