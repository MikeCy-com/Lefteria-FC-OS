<script lang="ts">
  import {browser} from '$app/env';
  import {untrack} from 'svelte';
  import AcademyGuardianProofReview from './AcademyGuardianProofReview.svelte';

  type RegistrationSource={
    id:string;applicant_first_name:string;applicant_last_name:string;applicant_birth_date:string;
    guardian_name:string;guardian_relationship:string;guardian_phone:string;guardian_email:string;
    submitted_at:string;approved_at:string;
  };
  function validRegistration(value:unknown):value is RegistrationSource{
    if(!value||typeof value!=='object'||Array.isArray(value))return false;
    const r=value as Record<string,unknown>;
    return typeof r.id==='string'&&/^[0-9a-f-]{36}$/i.test(r.id)&&
      typeof r.applicant_birth_date==='string'&&/^\d{4}-\d{2}-\d{2}$/.test(r.applicant_birth_date)&&
      Object.entries({applicant_first_name:100,applicant_last_name:100,guardian_name:200,
        guardian_relationship:80,guardian_phone:32,guardian_email:254,submitted_at:64,approved_at:64})
        .every(([key,max])=>typeof r[key]==='string'&&(r[key] as string).length<=max);
  }
  type CurrentContact={name:string;relationship:string;phone:string;email:string;revision:number;updated_at:string};
  function validContact(value:unknown):value is CurrentContact{
    if(!value||typeof value!=='object'||Array.isArray(value))return false;
    const c=value as Record<string,unknown>;
    return Number.isInteger(c.revision)&&(c.revision as number)>=1&&
      Object.entries({name:200,relationship:80,phone:32,email:254,updated_at:64})
        .every(([key,max])=>typeof c[key]==='string'&&(c[key] as string).length>0&&(c[key] as string).length<=max);
  }
  type Profile={
    organization_id:'lefteria_fc_academy';player_id:string;official_name:string;
    official_number:number|null;official_position:string|null;
    biography:string;preferred_position:string;preferred_number:number|null;
    has_portrait:boolean;revision:number;updated_at:string|null;
    registration?:RegistrationSource|null;
    current_contact?:CurrentContact|null;
  };

  let {playerId,playerName,actorId,allowWrite=false,allowGuardianReview=false,onNameChanged=()=>{},onExpired,onRevoked}:{
    playerId:string;playerName:string;actorId:string;allowWrite?:boolean;allowGuardianReview?:boolean;
    onNameChanged?:(name:string)=>void;onExpired:()=>void;onRevoked:()=>void;
  }=$props();
  let profile=$state<Profile|null>(null);
  let biography=$state('');
  let preferredPosition=$state('');
  let preferredNumber=$state('');
  let selectedFile=$state<File|null>(null);
  let loading=$state(false),saving=$state(false),uploading=$state(false);
  let notice=$state(''),error=$state(''),revision=$state(0);
  let photoLoaded=$state(false);
  let showGuardianReview=$state(false);
  let editingContact=$state(false),savingContact=$state(false),contactConfirmation=$state('');
  let editingName=$state(false),savingName=$state(false),newName=$state('');
  let editingOfficialSporting=$state(false),savingOfficialSporting=$state(false);
  let officialPosition=$state(''),officialNumber=$state('');
  const positionLabels:Record<string,string>={Goalkeeper:'Τερματοφύλακας',Defender:'Αμυντικός',Midfielder:'Μέσος',Forward:'Επιθετικός'};
  const officialEditBusy=$derived(editingOfficialSporting||savingOfficialSporting);
  const officialSportingValid=$derived(Object.hasOwn(positionLabels,officialPosition)&&
    /^[1-9][0-9]?$/.test(officialNumber));
  const sportingDirty=$derived(!!profile&&(biography!==profile.biography||preferredPosition!==profile.preferred_position||preferredNumber!==(profile.preferred_number?.toString()??'')));
  let contactName=$state(''),contactRelationship=$state(''),contactPhone=$state(''),contactEmail=$state('');
  const currentContact=$derived(profile?.current_contact??(profile?.registration?{
    name:profile.registration.guardian_name,relationship:profile.registration.guardian_relationship,
    phone:profile.registration.guardian_phone,email:profile.registration.guardian_email,revision:0,updated_at:''
  }:null));
  const contactValid=$derived(contactName.trim().length>0&&contactName.trim().length<=200&&
    contactRelationship.trim().length>0&&contactRelationship.trim().length<=80&&
    /^\+?[0-9 ()-]{7,32}$/.test(contactPhone.trim())&&
    /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(contactEmail.trim())&&contactEmail.trim().length<=254);
  const contactDraft=$derived({name:contactName.trim(),relationship:contactRelationship.trim(),phone:contactPhone.trim(),email:contactEmail.trim()});
  const contactFields=['name','relationship','phone','email'] as const;
  const contactLabels={name:'Ονοματεπώνυμο',relationship:'Σχέση',phone:'Τηλέφωνο',email:'Email'};
  const contactChanges=$derived(contactFields.filter(field=>contactDraft[field]!==(currentContact?.[field]??'')));
  const contactReviewKey=$derived(JSON.stringify([playerId,actorId,profile?.current_contact?.revision??0,contactDraft]));
  const contactConfirmed=$derived(contactConfirmation!==''&&contactConfirmation===contactReviewKey);
  function cancelContact(){if(!savingContact){editingContact=false;contactConfirmation='';}}
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
      (p.updated_at===null||typeof p.updated_at==='string')&&
      (p.registration===undefined||p.registration===null||validRegistration(p.registration))&&
      (p.current_contact===undefined||p.current_contact===null||validContact(p.current_contact));
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
    profile=null;selectedFile=null;loading=false;error='';photoLoaded=false;showGuardianReview=false;editingContact=false;contactConfirmation='';editingName=false;editingOfficialSporting=false;
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

  function editOfficialSporting(){
    if(!profile||!allowWrite||loading||saving||uploading||editingName||savingName||
      editingContact||savingContact||sportingDirty||officialEditBusy)return;
    officialPosition=profile.official_position??'';
    officialNumber=profile.official_number?.toString()??'';
    editingOfficialSporting=true;error='';notice='';
  }

  async function saveOfficialSporting(){
    if(!profile||!allowWrite||!editingOfficialSporting||savingOfficialSporting||
      loading||saving||uploading||editingName||savingName||editingContact||savingContact||
      sportingDirty||!officialSportingValid)return;
    const id=playerId,actor=actorId,position=officialPosition,number=Number(officialNumber);
    if(position===profile.official_position&&number===profile.official_number)return;
    const expectedPosition=profile.official_position,expectedNumber=profile.official_number;
    savingOfficialSporting=true;error='';notice='';
    try{
      const response=await request(url,{method:'PUT',headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({sportingCorrection:{expectedPosition,expectedNumber,position,number}})});
      const value:unknown=await response.json();
      if(!value||typeof value!=='object'||Array.isArray(value))throw Error('Η αλλαγή δεν επιβεβαιώθηκε.');
      const result=value as Record<string,unknown>;
      if(result.organization_id!=='lefteria_fc_academy'||result.player_id!==id||
        result.official_position!==position||result.official_number!==number||result.updated!==true)
        throw Error('Η αλλαγή δεν επιβεβαιώθηκε.');
      if(playerId!==id||actorId!==actor||!allowWrite)return;
      editingOfficialSporting=false;notice='Τα επίσημα αθλητικά στοιχεία ενημερώθηκαν.';revision++;
    }catch(e){if(playerId===id&&actorId===actor)error=e instanceof Error?e.message:'Η αλλαγή απέτυχε.';}
    finally{savingOfficialSporting=false;}
  }

  async function correctName(){
    if(officialEditBusy||!profile||!allowWrite||!editingName||savingName||savingContact||saving||uploading||loading||sportingDirty)return;
    const expected=profile.official_name,name=newName.trim(),id=playerId,actor=actorId;
    if(!name||name.length>200||/[\x00-\x1f\x7f]/.test(name)||name===expected)return;
    savingName=true;error='';notice='';
    try{
      const response=await request(url,{method:'PUT',headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({nameCorrection:{expectedName:expected,name}})});
      const value:unknown=await response.json();
      if(!value||typeof value!=='object'||Array.isArray(value))throw Error('Η αλλαγή δεν επιβεβαιώθηκε.');
      const result=value as Record<string,unknown>;
      if(result.organization_id!=='lefteria_fc_academy'||result.player_id!==id||result.official_name!==name||result.updated!==true)
        throw Error('Η αλλαγή δεν επιβεβαιώθηκε.');
      if(playerId!==id||actorId!==actor||!allowWrite)return;
      editingName=false;notice='Το όνομα ενημερώθηκε.';onNameChanged(name);revision++;
    }catch(e){if(playerId===id&&actorId===actor)error=e instanceof Error?e.message:'Η αλλαγή απέτυχε.';}
    finally{savingName=false;}
  }

  function editContact(){
    if(officialEditBusy||!profile||!allowWrite||saving||uploading||savingContact||savingName||editingName||sportingDirty||loading)return;
    contactName=currentContact?.name??'';contactRelationship=currentContact?.relationship??'';
    contactPhone=currentContact?.phone??'';contactEmail=currentContact?.email??'';
    editingContact=true;contactConfirmation='';showGuardianReview=false;error='';notice='';
  }
  async function saveContact(){
    if(officialEditBusy||!profile||!allowWrite||!editingContact||!contactValid||!contactChanges.length||
      !contactConfirmed||savingContact||saving||uploading||loading||savingName||editingName||sportingDirty)return;
    const id=playerId,actor=actorId,expected=profile.current_contact?.revision??0;
    const submitted={...contactDraft};
    savingContact=true;error='';notice='';
    try{
      const response=await request(url,{
        method:'PUT',headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({expectedRevision:expected,contact:submitted})
      });
      const value:unknown=await response.json();
      if(!value||typeof value!=='object'||Array.isArray(value))throw Error('Η αποθήκευση δεν επιβεβαιώθηκε.');
      const result=value as Record<string,unknown>;
      if(result.organization_id!=='lefteria_fc_academy'||result.player_id!==id||
        result.revision!==expected+1||result.updated!==true)throw Error('Η αποθήκευση δεν επιβεβαιώθηκε.');
      if(playerId!==id||actorId!==actor||!allowWrite)return;
      editingContact=false;contactConfirmation='';notice='Τα στοιχεία επικοινωνίας αποθηκεύτηκαν. Η πρόσβαση κηδεμόνα δεν άλλαξε.';revision++;
    }catch(e){if(playerId===id&&actorId===actor){contactConfirmation='';error=e instanceof Error?e.message:'Η αποθήκευση απέτυχε.';}}
    finally{savingContact=false;}
  }

  async function save(){
    if(!profile||!allowWrite||saving||uploading||savingContact||editingContact||savingName||editingName||loading)return;
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
    if(officialEditBusy||!allowWrite||uploading||saving||savingContact||editingContact||savingName||editingName||sportingDirty||loading||(!remove&&!canUpload))return;
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
    <button type="button" class="admin-secondary" disabled={loading||saving||uploading||savingName||savingContact||savingOfficialSporting}
      onclick={()=>{if((sportingDirty||editingName||editingContact||editingOfficialSporting)&&!window.confirm("Να απορριφθούν οι μη αποθηκευμένες αλλαγές;"))return;revision++;}}>Ανανέωση προφίλ</button>
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
              disabled={officialEditBusy||saving||uploading||editingName||editingContact||sportingDirty||savingName||savingContact}
              onchange={event=>selectedFile=event.currentTarget.files?.[0]??null}/>
          </label>
          <p class="admin-muted">Μέχρι 5 MB. Η εικόνα μετατρέπεται σε ιδιωτική JPEG χωρίς μεταδεδομένα EXIF.</p>
          <button type="button" class="admin-secondary" disabled={officialEditBusy||!canUpload||uploading||saving||sportingDirty||editingName||editingContact||savingName||savingContact}
            data-testid="admin-player-upload-private-photo" onclick={()=>void changePortrait()}>
            {uploading?'Μεταφόρτωση…':'Αποθήκευση φωτογραφίας'}
          </button>
          {#if profile.has_portrait}
            <button type="button" class="admin-secondary" disabled={officialEditBusy||uploading||saving||sportingDirty||editingName||editingContact||savingName||savingContact}
              onclick={()=>void changePortrait(true)}>Αφαίρεση φωτογραφίας</button>
          {/if}
        {/if}
      </div>
      <div class="admin-player-private-fields">
        <div class="admin-player-official">
          <strong>Τρέχοντα επίσημα στοιχεία ρόστερ</strong>
          <span>{profile.official_name}</span>
          {#if allowWrite&&!editingName}
            <button type="button" class="admin-secondary" disabled={officialEditBusy||saving||uploading||savingContact||editingContact||sportingDirty} onclick={()=>{newName=profile?.official_name??'';editingName=true;error='';notice='';}}>Διόρθωση ονόματος</button>
          {/if}
          {#if allowWrite&&editingName}
            <form class="name-correction" onsubmit={event=>{event.preventDefault();void correctName();}}>
              <label>Τρέχον επίσημο όνομα <input bind:value={newName} maxlength="200" required disabled={savingName}/></label>
              <p class="admin-muted">Η αλλαγή αφορά το τρέχον ρόστερ. Η αρχική υπογεγραμμένη αίτηση διατηρείται.</p>
              <div class="contact-actions">
                <button type="submit" class="admin-primary" disabled={savingName||!newName.trim()||newName.trim()===profile.official_name}>Αποθήκευση ονόματος</button>
                <button type="button" class="admin-secondary" disabled={savingName} onclick={()=>editingName=false}>Ακύρωση</button>
              </div>
            </form>
          {/if}
          <span>Αριθμός: {profile.official_number??'—'} · Θέση: {positionLabels[profile.official_position??'']??profile.official_position??'—'}</span>
          {#if allowWrite&&!editingOfficialSporting}
            <button type="button" class="admin-secondary" disabled={saving||uploading||editingName||savingName||editingContact||savingContact||sportingDirty||savingOfficialSporting} onclick={editOfficialSporting}>Επεξεργασία θέσης & αριθμού</button>
          {/if}
          {#if allowWrite&&editingOfficialSporting}
            <form class="name-correction" data-testid="admin-player-official-sporting-form" onsubmit={event=>{event.preventDefault();void saveOfficialSporting();}}>
              <label>Επίσημη θέση
                <select bind:value={officialPosition} required disabled={savingOfficialSporting}>
                  <option value="">Επιλέξτε θέση</option>
                  {#each Object.entries(positionLabels) as [value,label]}<option {value}>{label}</option>{/each}
                </select>
              </label>
              <label>Αριθμός φανέλας
                <input bind:value={officialNumber} inputmode="numeric" pattern="[1-9][0-9]?" maxlength="2" required disabled={savingOfficialSporting}/>
              </label>
              <p class="admin-muted">Αφορά την τρέχουσα αγωνιστική θέση και τον αριθμό 1–99, όχι την προτίμηση ή την αρχική αίτηση.</p>
              <div class="contact-actions">
                <button type="submit" class="admin-primary" disabled={savingOfficialSporting||!officialSportingValid||(officialPosition===profile.official_position&&Number(officialNumber)===profile.official_number)}>Αποθήκευση αγωνιστικών στοιχείων</button>
                <button type="button" class="admin-secondary" disabled={savingOfficialSporting} onclick={()=>editingOfficialSporting=false}>Ακύρωση</button>
              </div>
            </form>
          {/if}
          <small>Η αρχική εγγραφή διατηρείται χωριστά. Οι αλλαγές στο ρόστερ δεν μεταβάλλουν το υπογεγραμμένο έντυπο.</small>
        </div>
        <label>Αθλητικό βιογραφικό
          <textarea bind:value={biography} maxlength="1000" rows="5"
            disabled={officialEditBusy||!allowWrite||saving||uploading||editingName||editingContact||savingName||savingContact}
            placeholder="Σύντομη παρουσίαση, δυνατά σημεία και στόχοι ανάπτυξης"></textarea>
        </label>
        <div class="admin-player-preferences">
          <label>Προτιμώμενη θέση
            <input bind:value={preferredPosition} maxlength="80"
              disabled={officialEditBusy||!allowWrite||saving||uploading||editingName||editingContact||savingName||savingContact} placeholder="Π.χ. Επιθετικός"/>
          </label>
          <label>Προτιμώμενος αριθμός
            <input bind:value={preferredNumber} inputmode="numeric"
              pattern="[0-9][0-9]?" maxlength="2"
              disabled={officialEditBusy||!allowWrite||saving||uploading||editingName||editingContact||savingName||savingContact} placeholder="1–99"/>
          </label>
        </div>
        {#if profile.updated_at}
          <small>Τελευταία ενημέρωση: {new Date(profile.updated_at).toLocaleString('el-CY')}</small>
        {/if}
        {#if allowWrite}
          <button type="button" class="admin-primary"
            data-testid="admin-player-save-private-profile"
            disabled={officialEditBusy||saving||uploading||loading||editingName||editingContact||savingName||savingContact} onclick={()=>void save()}>
            {saving?'Αποθήκευση…':'Αποθήκευση προφίλ'}
          </button>
        {/if}
      </div>
    </div>
    {#if sportingDirty}<p class="admin-muted" role="status">Αποθηκεύστε τις αθλητικές προτιμήσεις πριν αλλάξετε όνομα, φωτογραφία ή στοιχεία επικοινωνίας.</p>{/if}
    <section class="registration-source-card" data-testid="admin-player-current-contact">
      <div class="contact-header">
        <h3>Τρέχοντα στοιχεία επικοινωνίας γονέα / κηδεμόνα</h3>
        {#if allowWrite&&!editingContact}
          <button type="button" class="admin-secondary" disabled={officialEditBusy||saving||uploading||savingContact||editingName||savingName||sportingDirty} onclick={editContact}>Επεξεργασία επικοινωνίας</button>
        {/if}
      </div>
      <p class="admin-muted">Οι διορθώσεις αφορούν μόνο την επικοινωνία της Ακαδημίας. Δεν αλλάζουν την αρχική αίτηση, την ταυτοποίηση, την πρόσβαση στην εφαρμογή ή τα οικονομικά στοιχεία.</p>
      {#if editingContact&&allowWrite}
        <form class="contact-edit" onsubmit={event=>{event.preventDefault();void saveContact();}}>
          <label>Ονοματεπώνυμο <input bind:value={contactName} required maxlength="200" disabled={savingContact}/></label>
          <label>Σχέση <input bind:value={contactRelationship} required maxlength="80" disabled={savingContact}/></label>
          <label>Τηλέφωνο <input type="tel" bind:value={contactPhone} required maxlength="32" disabled={savingContact}/></label>
          <label>Email <input type="email" bind:value={contactEmail} required maxlength="254" disabled={savingContact}/></label>
          {#if contactChanges.length}
            <section class="contact-review" data-testid="admin-guardian-contact-review">
              <h4>Έλεγχος αλλαγών επικοινωνίας</h4>
              <dl>{#each contactChanges as field (field)}
                <div><dt>{contactLabels[field]}</dt><dd><span>Πριν: {currentContact?.[field]||'—'}</span><span>Μετά: {contactDraft[field]||'—'}</span></dd></div>
              {/each}</dl>
              <p class="admin-muted">Επιβεβαιώστε ότι τα νέα στοιχεία ανήκουν στον σωστό γονέα ή κηδεμόνα. Η διόρθωση δεν αλλάζει το συνδεδεμένο τηλέφωνο εισόδου ή τα δικαιώματα.</p>
              <label class="contact-confirm"><input type="checkbox" checked={contactConfirmed} disabled={!contactValid||savingContact}
                onchange={event=>contactConfirmation=event.currentTarget.checked?contactReviewKey:''}/> Επιβεβαιώνω τις παραπάνω αλλαγές.</label>
            </section>
          {/if}
          <div class="contact-actions">
            <button type="submit" class="admin-primary" disabled={!contactValid||!contactChanges.length||!contactConfirmed||savingContact}>{savingContact?'Αποθήκευση…':'Αποθήκευση επικοινωνίας'}</button>
            <button type="button" class="admin-secondary" disabled={savingContact} onclick={cancelContact}>Ακύρωση</button>
          </div>
        </form>
      {:else if currentContact}
        <dl>
          <div><dt>Ονοματεπώνυμο</dt><dd>{currentContact.name}</dd></div>
          <div><dt>Σχέση</dt><dd>{currentContact.relationship}</dd></div>
          <div><dt>Τηλέφωνο</dt><dd>{currentContact.phone}</dd></div>
          <div><dt>Email</dt><dd>{currentContact.email}</dd></div>
        </dl>
        <p class="admin-muted">{currentContact.revision>0?'Τελευταία ενημέρωση: '+new Date(currentContact.updated_at).toLocaleString('el-CY'):'Πηγή: αρχική εγκεκριμένη αίτηση'}</p>
      {:else}
        <p class="admin-muted">Δεν έχουν καταχωριστεί τρέχοντα στοιχεία επικοινωνίας.</p>
      {/if}
    </section>
    <section class="registration-source-card" data-testid="admin-player-registration-source">
      <h3>Αρχική εγγραφή · αμετάβλητο ιστορικό</h3>
      {#if profile.registration}
        <p class="admin-muted">Αρχικά στοιχεία από την εγκεκριμένη αίτηση. Τα στοιχεία επικοινωνίας δεν αποτελούν από μόνα τους επαλήθευση πρόσβασης γονέα στην εφαρμογή.</p>
        <dl>
          <div><dt>Όνομα εγγραφής</dt><dd>{profile.registration.applicant_first_name} {profile.registration.applicant_last_name}</dd></div>
          <div><dt>Ημερομηνία γέννησης</dt><dd>{profile.registration.applicant_birth_date}</dd></div>
          <div><dt>Κηδεμόνας</dt><dd>{profile.registration.guardian_name}</dd></div>
          <div><dt>Σχέση</dt><dd>{profile.registration.guardian_relationship}</dd></div>
          <div><dt>Τηλέφωνο εγγραφής</dt><dd>{profile.registration.guardian_phone}</dd></div>
          <div><dt>Email εγγραφής</dt><dd>{profile.registration.guardian_email}</dd></div>
          <div><dt>Αριθμός αίτησης</dt><dd>{profile.registration.id}</dd></div>
          <div><dt>Ημερομηνία έγκρισης</dt><dd>{new Date(profile.registration.approved_at).toLocaleDateString('el-CY')}</dd></div>
        </dl>
        {#if allowGuardianReview}
          <button type="button" class="admin-secondary" disabled={editingContact||savingContact} aria-expanded={showGuardianReview} onclick={()=>showGuardianReview=!showGuardianReview}>{showGuardianReview?'Κλείσιμο ελέγχου':'Έλεγχος ταυτότητας κηδεμόνα'}</button>
          {#if showGuardianReview}
            {#key actorId+':'+playerId+':'+profile.registration.id}
              <AcademyGuardianProofReview registrationId={profile.registration.id} guardianPhone={profile.registration.guardian_phone} expectedPlayerId={playerId}/>
            {/key}
          {/if}
        {/if}
      {:else}
        <p class="admin-muted">Δεν υπάρχει επαληθευμένη σύνδεση με εγκεκριμένη ηλεκτρονική αίτηση. Δεν δημιουργούνται υποθετικά στοιχεία γονέων.</p>
      {/if}
    </section>
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
  .admin-player-private-fields textarea,.admin-player-private-fields input,.admin-player-private-fields select{width:100%;
    min-height:42px;background:var(--admin-surface,#151719);color:inherit;
    border:1px solid var(--admin-border,#5a5f61);border-radius:8px;padding:10px}
  .admin-player-official{display:grid;gap:6px;padding:12px;
    border:1px solid var(--admin-border,#505456);border-radius:10px}
  .admin-player-official small{font-size:11px;opacity:.8}
  .admin-player-preferences{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px}
  .admin-player-private-fields button{justify-self:start;min-height:43px}
  @media(max-width:650px){.admin-private-profile-grid{grid-template-columns:1fr}
    .admin-player-preferences{grid-template-columns:1fr}}
  .registration-source-card{display:grid;gap:12px;border-top:1px solid var(--admin-line);padding-top:16px;min-width:0}
  .registration-source-card h3,.registration-source-card dl,.registration-source-card dd{margin:0}
  .registration-source-card dl{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,210px),1fr));gap:10px}
  .registration-source-card dl>div{padding:10px;border:1px solid var(--admin-line);border-radius:8px;overflow-wrap:anywhere;min-width:0}
  .registration-source-card dt{font-size:12px;color:#b8b8b8}
  .registration-source-card dd{font-weight:600}
  .contact-header,.contact-actions{display:flex;flex-wrap:wrap;align-items:center;gap:12px}
  .contact-header{justify-content:space-between}
  .contact-edit{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,210px),1fr));gap:12px;min-width:0}
  .contact-edit label{display:grid;gap:6px;min-width:0}
  .contact-edit input{width:100%;min-width:0}
  .contact-actions{grid-column:1/-1}
  .contact-review{grid-column:1/-1;display:grid;gap:8px;padding:12px;border:1px solid var(--admin-line);border-radius:8px;min-width:0}
  .contact-review h4{margin:0}
  .contact-review dd{display:grid;gap:4px;overflow-wrap:anywhere}
  .contact-edit .contact-confirm{display:flex;align-items:center;gap:8px;flex-wrap:wrap}
  .contact-edit .contact-confirm input{width:auto;min-width:16px}
  .name-correction{display:grid;gap:8px;min-width:0}
</style>
