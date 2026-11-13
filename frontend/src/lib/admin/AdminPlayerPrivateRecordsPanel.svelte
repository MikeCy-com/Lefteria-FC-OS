<script lang="ts">
  import {browser} from '$app/env';
  import AdminPlayerBirthdayPreferences from './AdminPlayerBirthdayPreferences.svelte';

  type Owner='lefteria_fc'|'lefteria_fc_academy';
  type ScoreSection='technical'|'tactical'|'physical'|'character';
  type Evaluation={id:string;revision:number;summary:string|null;notes:string|null;
    created_at:string;technical:Record<string,number>;tactical:Record<string,number>;
    physical:Record<string,number>;character:Record<string,number>;overall:Record<string,unknown>};
  type Document={id:string;document_type:string;title:string;
    expiry_date:string|null;status:string};
  let {owner,playerId,playerName,actorId,allowCreate,onExpired,onRevoked}:{
    owner:Owner;playerId:string;playerName:string;actorId:string;allowCreate:boolean;
    onExpired:()=>void;onRevoked:()=>void;
  }=$props();

  let evaluations=$state<Evaluation[]>([]);
  let documents=$state<Document[]>([]);
  let loading=$state(false);
  let error=$state('');
  let generation=$state(0);
  let showCreate=$state(false);
  let summary=$state('');
  let notes=$state('');
  let scoreInputs=$state<Record<ScoreSection,string>>({
    technical:'',tactical:'',physical:'',character:''
  });
  let creating=$state(false);
  let showUpload=$state(false);
  let documentFile=$state<File|null>(null);
  let documentType=$state('registration_form');
  let documentTitle=$state('');
  let issuedAt=$state('');
  let expiresAt=$state('');
  let uploadRequestId=$state('');
  let uploading=$state(false);
  let uploadError=$state('');
  let uploadNotice=$state('');
  const protectedTypes=[
    {id:'id_passport',label:'Ταυτότητα / Διαβατήριο'},
    {id:'birth_certificate',label:'Πιστοποιητικό γέννησης'},
    {id:'photo',label:'Φωτογραφία'},
    {id:'registration_form',label:'Αίτηση εγγραφής'},
    {id:'kop_registration',label:'Εγγραφή ΚΟΠ'},
    {id:'transfer_certificate',label:'Πιστοποιητικό μεταγραφής'},
    {id:'medical_cert',label:'Ιατρικό πιστοποιητικό'},
    {id:'vaccination_record',label:'Καρτέλα εμβολίων'},
    {id:'consent_form',label:'Γονική συγκατάθεση'},
    {id:'gdpr_consent',label:'Συγκατάθεση GDPR'},
    {id:'insurance',label:'Ασφάλιση'},
    {id:'school_confirmation',label:'Σχολική βεβαίωση'},
    {id:'image_rights',label:'Δικαιώματα εικόνας'},
    {id:'other',label:'Άλλο έγγραφο'}
  ] as const;
  const requiresExpiry=(type:string)=>
    ['id_passport','kop_registration','medical_cert','insurance',
      'school_confirmation'].includes(type);
  function resetUploadRequest(){uploadRequestId='';uploadError='';uploadNotice='';}
  async function uploadDocument(){
    if(!browser||!allowCreate||!documentFile||uploading||loading)return;
    const chosenFile=documentFile;
    if(chosenFile.size<1||chosenFile.size>10485760||
       !documentTitle.trim()||documentTitle!==documentTitle.trim()||
       documentTitle.length>200||
       (requiresExpiry(documentType)&&!expiresAt)||
       (issuedAt&&expiresAt&&expiresAt<issuedAt)){
      uploadError='Ελέγξτε μέγεθος αρχείου (έως 10 MB), τίτλο και ημερομηνίες.';
      return;
    }
    if(!uploadRequestId)uploadRequestId=crypto.randomUUID();
    const expectedOwner=owner,expectedPlayer=playerId,expectedActor=actorId;
    const data=new FormData();
    data.set('request_id',uploadRequestId);
    data.set('document_type',documentType);
    data.set('title',documentTitle);
    if(issuedAt)data.set('issue_date',issuedAt);
    if(expiresAt)data.set('expiry_date',expiresAt);
    data.set('file',chosenFile);
    uploading=true;uploadError='';uploadNotice='';
    try{
      const response=await fetch('/api/admin/club/players/'+encodeURIComponent(expectedPlayer)+
        '/documents?'+new URLSearchParams({organization_id:expectedOwner}),{
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'X-Lefteria-Admin':'1'},body:data
      });
      if(owner!==expectedOwner||playerId!==expectedPlayer||actorId!==expectedActor)return;
      if(response.status===401){onExpired();return;}
      if(response.status===403){onRevoked();return;}
      if(!response.ok)throw new Error(response.status===503?
        'Η προστατευμένη αποθήκευση δεν έχει ενεργοποιηθεί.':
        'Το έγγραφο δεν καταχωρίστηκε.');
      const raw:unknown=await response.json();
      if(!raw||typeof raw!=='object'||Array.isArray(raw)||
        (raw as Record<string,unknown>).organization_id!==expectedOwner||
        (raw as Record<string,unknown>).player_id!==expectedPlayer||
        (raw as Record<string,unknown>).scan_status!=='quarantined'||
        typeof (raw as Record<string,unknown>).id!=='string')
        throw new Error('Μη επαληθευμένη αποθήκευση εγγράφου.');
      documentFile=null;documentTitle='';issuedAt='';expiresAt='';
      uploadRequestId='';showUpload=false;generation++;
      uploadNotice='Το κρυπτογραφημένο αρχείο μπήκε σε καραντίνα και δεν είναι διαθέσιμο για λήψη.';
    }catch(e){uploadError=e instanceof Error?e.message:'Αποτυχία αποθήκευσης.';}
    finally{uploading=false;}
  }
  let createError=$state('');
  let createNotice=$state('');
  let requestId=$state('');
  const scoreKey:Record<ScoreSection,string>={
    technical:'passing',tactical:'positioning',physical:'coordination',
    character:'teamwork'
  };
  function changeDraft(){requestId='';createError='';createNotice='';}
  async function createEvaluation(){
    if(!browser||!allowCreate||creating||loading||!idValid(playerId))return;
    if(!summary.trim()||summary!==summary.trim()||summary.length>500||notes.length>4000){
      createError='Απαιτείται τίτλος έως 500 χαρακτήρες.';return;
    }
    for(const field of sections){
      const v=scoreInputs[field];
      if(v!==''&&(!/^\d{1,3}$/.test(v)||Number(v)>100)){
        createError='Οι βαθμολογίες πρέπει να είναι ακέραιοι 0–100.';return;
      }
    }
    if(!requestId)requestId=crypto.randomUUID();
    const expectedOwner=owner,expectedPlayer=playerId,expectedActor=actorId;
    const payload:Record<string,unknown>={
      request_id:requestId,summary,notes,overall:{}
    };
    for(const field of sections)payload[field]=scoreInputs[field]===''
      ?{}:{[scoreKey[field]]:Number(scoreInputs[field])};
    creating=true;createError='';createNotice='';
    try{
      const response=await fetch('/api/admin/club/players/'+encodeURIComponent(expectedPlayer)+
        '/evaluations?'+new URLSearchParams({organization_id:expectedOwner}),{
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify(payload)
      });
      if(owner!==expectedOwner||playerId!==expectedPlayer||actorId!==expectedActor)return;
      if(response.status===401){onExpired();return;}
      if(response.status===403){onRevoked();return;}
      if(!response.ok)throw new Error(response.status===409?
        'Υπάρχει σύγκρουση καταχώρισης. Ελέγξτε τα στοιχεία.':
        'Η αξιολόγηση δεν αποθηκεύτηκε.');
      const saved:unknown=await response.json();
      if(!saved||typeof saved!=='object'||Array.isArray(saved)||
        (saved as Record<string,unknown>).organization_id!==expectedOwner||
        (saved as Record<string,unknown>).player_id!==expectedPlayer||
        typeof (saved as Record<string,unknown>).id!=='string')
        throw new Error('Μη επαληθευμένη αποθήκευση.');
      showCreate=false;summary='';notes='';
      scoreInputs={technical:'',tactical:'',physical:'',character:''};
      requestId='';generation++;
      createNotice='Η αξιολόγηση καταχωρίστηκε. Η λίστα ανανεώνεται.';
    }catch(e){createError=e instanceof Error?e.message:'Σφάλμα καταχώρισης.';}
    finally{creating=false;}
  }
  const sections:ScoreSection[]=['technical','tactical','physical','character'];
  const idValid=(id:string)=>id.length>0&&id.length<=128&&!/[\x00-\x1f\x7f]/.test(id);

  function verifiedList(value:unknown,type:'evaluations'|'documents',expectedOwner:Owner,expectedPlayer:string):value is Record<string,unknown> {
    if(!value||typeof value!=='object'||Array.isArray(value))return false;
    const x=value as Record<string,unknown>;
    if(x.organization_id!==expectedOwner||x.player_id!==expectedPlayer||
      !Number.isSafeInteger(x.total)||!Array.isArray(x[type])||
      (x.total as number)!==x[type].length||x[type].length>(type==='documents'?100:200))return false;
    return x[type].every((entry:unknown)=>{
      if(!entry||typeof entry!=='object'||Array.isArray(entry))return false;
      const row=entry as Record<string,unknown>;
      if(typeof row.id!=='string'||!/^[0-9a-f-]{36}$/.test(row.id))return false;
      if(type==='documents')return typeof row.title==='string'&&row.title.length<=200&&
        typeof row.document_type==='string'&&row.document_type.length<=32&&
        (row.expiry_date===null||typeof row.expiry_date==='string')&&
        ['no_expiry','expired','critical','warning','notice','watch','valid'].includes(String(row.status));
      return typeof row.created_at==='string'&&
        Number.isInteger(row.revision)&&(row.revision as number)>0&&
        (row.summary===null||typeof row.summary==='string'&&row.summary.length<=500)&&
        (row.notes===null||typeof row.notes==='string'&&row.notes.length<=4000)&&
        sections.every(s=>row[s]!==null&&typeof row[s]==='object'&&!Array.isArray(row[s]))&&
        row.overall!==null&&typeof row.overall==='object'&&!Array.isArray(row.overall);
    });
  }
  $effect(()=>{
    const currentOwner=owner,currentPlayer=playerId,currentActor=actorId;
    void generation;
    evaluations=[];documents=[];loading=false;error='';
    if(!browser||!idValid(currentPlayer)||!currentActor)return;
    const controller=new AbortController();
    loading=true;
    void (async()=>{
      try{
        const query=new URLSearchParams({organization_id:currentOwner});
        const prefix='/api/admin/club/players/'+encodeURIComponent(currentPlayer);
        const responses=await Promise.all(['evaluations','documents'].map(resource=>
          fetch(prefix+'/'+resource+'?'+query.toString(),{
            credentials:'same-origin',cache:'no-store',signal:controller.signal,
            headers:{Accept:'application/json'}
          })));
        if(controller.signal.aborted)return;
        if(responses.some(response=>response.status===401)){onExpired();return;}
        if(responses.some(response=>response.status===403)){onRevoked();return;}
        if(responses.some(response=>!response.ok))
          throw new Error('Οι ιδιωτικοί φάκελοι δεν είναι διαθέσιμοι.');
        const payloads:unknown[]=await Promise.all(responses.map(r=>r.json()));
        if(!verifiedList(payloads[0],'evaluations',currentOwner,currentPlayer)||
           !verifiedList(payloads[1],'documents',currentOwner,currentPlayer))
          throw new Error('Μη επαληθευμένη απόκριση ιδιωτικού φακέλου.');
        if(!controller.signal.aborted){
          evaluations=payloads[0].evaluations as Evaluation[];
          documents=payloads[1].documents as Document[];
        }
      }catch{
        if(!controller.signal.aborted){
          evaluations=[];documents=[];error='Η προβολή ιδιωτικού φακέλου απέτυχε.';
        }
      }finally{if(!controller.signal.aborted)loading=false;}
    })();
    return()=>controller.abort();
  });
</script>

<section class="private-player-card" aria-label={'Ιδιωτικός φάκελος '+playerName}
  data-testid="admin-player-private-records">
  <div class="private-player-card-heading">
    <h3>Ιδιωτικός φάκελος · {playerName}</h3>
    <button type="button" class="admin-secondary" onclick={()=>generation++} disabled={loading}>
      Ανανέωση
    </button>
  </div>
  <p class="admin-muted">Ιδιωτικές αξιολογήσεις και προστατευμένα έγγραφα του επιλεγμένου αθλητή.</p>
  {#if allowCreate}
    <button type="button" class="admin-secondary" onclick={()=>showCreate=!showCreate} disabled={creating}>
      {showCreate?'Κλείσιμο φόρμας':'Νέα αξιολόγηση'}
    </button>
    {#if showCreate}
      <form class="private-evaluation-form" data-testid="admin-evaluation-create"
        onsubmit={event=>{event.preventDefault();void createEvaluation();}}>
        <label>Σύνοψη
          <input required maxlength="500" bind:value={summary} oninput={changeDraft}/>
        </label>
        <label>Σημειώσεις
          <textarea maxlength="4000" rows="3" bind:value={notes} oninput={changeDraft}></textarea>
        </label>
        <fieldset>
          <legend>Επιλεγμένες μετρήσεις 0–100 (προαιρετικές)</legend>
          {#each sections as section}
            <label>{scoreKey[section]}
              <input type="number" min="0" max="100" step="1"
                value={scoreInputs[section]}
                oninput={event=>{scoreInputs[section]=event.currentTarget.value;changeDraft();}}/>
            </label>
          {/each}
        </fieldset>
        {#if createError}<p role="alert">{createError}</p>{/if}
        <button type="submit" class="admin-secondary" disabled={creating}>
          {creating?'Καταχώριση…':'Αποθήκευση αξιολόγησης'}
        </button>
      </form>
    {/if}
  {/if}
  {#if createNotice}<p role="status">{createNotice}</p>{/if}
  {#if allowCreate}
    <button type="button" class="admin-secondary" disabled={uploading}
      onclick={()=>showUpload=!showUpload}>
      {showUpload?'Κλείσιμο μεταφόρτωσης':'Μεταφόρτωση προστατευμένου εγγράφου'}
    </button>
    {#if showUpload}
      <form class="private-evaluation-form" data-testid="admin-document-quarantine-form"
        onsubmit={e=>{e.preventDefault();void uploadDocument();}}>
        <p class="admin-muted">Τα αρχεία κρυπτογραφούνται και τίθενται σε καραντίνα. Δεν διατίθενται λήψεις χωρίς έλεγχο ασφαλείας.</p>
        <label>Τύπος
          <select bind:value={documentType} onchange={resetUploadRequest}>
            {#each protectedTypes as type}
              <option value={type.id}>{type.label}</option>
            {/each}
          </select>
        </label>
        <label>Τίτλος <input bind:value={documentTitle} oninput={resetUploadRequest}
          maxlength="200" required/></label>
        <label>Ημερομηνία έκδοσης
          <input type="date" bind:value={issuedAt} onchange={resetUploadRequest}/>
        </label>
        <label>Λήξη
          <input type="date" bind:value={expiresAt}
            required={requiresExpiry(documentType)} onchange={resetUploadRequest}/>
        </label>
        <label>Αρχείο (PDF, JPEG, PNG ή WebP, έως 10 MB)
          <input type="file" accept=".pdf,.jpg,.jpeg,.png,.webp"
            required onchange={e=>{documentFile=e.currentTarget.files?.item(0)??null;resetUploadRequest();}}/>
        </label>
        {#if uploadError}<p role="alert">{uploadError}</p>{/if}
        <button type="submit" class="admin-secondary" disabled={uploading}>
          {uploading?'Κρυπτογράφηση και αποθήκευση…':'Αποθήκευση σε καραντίνα'}
        </button>
      </form>
    {/if}
  {/if}
  <AdminPlayerBirthdayPreferences {owner} {playerId} {actorId} {onExpired} {onRevoked}/>
  {#if uploadNotice}<p role="status">{uploadNotice}</p>{/if}
  {#if loading}<p role="status">Ανάκτηση προστατευμένων στοιχείων…</p>{/if}
  {#if error}<p role="alert">{error}</p>{/if}
  {#if !loading&&!error}
    <div class="private-player-card-sections">
      <div>
        <h4>Αξιολογήσεις ({evaluations.length})</h4>
        {#each evaluations as evaluation (evaluation.id)}
          <details>
            <summary>{evaluation.summary??'Αξιολόγηση'} · {new Date(evaluation.created_at).toLocaleDateString('el-CY')}</summary>
            {#if evaluation.notes}<p>{evaluation.notes}</p>{/if}
            {#each sections as section}
              <p>{section}: {Object.entries(evaluation[section]).map(([key,value])=>key+': '+value).join(' · ')||'—'}</p>
            {/each}
          </details>
        {/each}
        {#if evaluations.length===0}<p>Δεν υπάρχουν αξιολογήσεις.</p>{/if}
      </div>
      <div>
        <h4>Έγγραφα ({documents.length})</h4>
        {#each documents as doc (doc.id)}
          <p><strong>{doc.title}</strong> · {doc.document_type}
            · {doc.expiry_date??'Χωρίς λήξη'} ({doc.status})
          </p>
        {/each}
        {#if documents.length===0}<p>Δεν υπάρχουν μεταδεδομένα εγγράφων.</p>{/if}
        <p class="admin-muted">Δεν επιτρέπεται λήψη αρχείων από αυτή την οθόνη.</p>
      </div>
    </div>
  {/if}
</section>
<style>
  .private-player-card{grid-column:1/-1;display:grid;gap:10px;border:1px solid #705c36;border-radius:12px;padding:16px;background:#191b1c}
  .private-player-card-heading{display:flex;justify-content:space-between;align-items:center;gap:12px}
  .private-player-card h3,.private-player-card h4{margin:0;color:#ffca46}
  .private-player-card p,.private-player-card summary{font-size:13px;overflow-wrap:anywhere}
  .private-player-card-sections{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:18px}
  .private-player-card details{padding:8px 0;border-bottom:1px solid #333}
  .private-evaluation-form{display:grid;gap:10px;padding:15px;border:1px solid #454545;border-radius:9px}
  .private-evaluation-form label{display:grid;gap:5px;font-size:13px}
  .private-evaluation-form fieldset{display:flex;gap:12px;flex-wrap:wrap;border:0;padding:0}
  .private-evaluation-form input,.private-evaluation-form textarea,.private-evaluation-form select{padding:8px;color:white;background:#171717;border:1px solid #555;border-radius:7px}
</style>