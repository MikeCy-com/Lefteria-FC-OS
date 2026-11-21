<script lang="ts">
  import {browser} from '$app/env';

  type Owner='lefteria_fc'|'lefteria_fc_academy';
  type Group={id:string;name:string;kind:'team'|'academy_group';active_players:number};
  type Account={id:string;name:string;role:string;active:boolean;
    invitation_pending:boolean;organizations:string[]};
  type CandidatePage={offset:number;limit:number;total:number;accounts:Account[]};
  type Site={id:string;name:string;city_id:string};
  type City={id:string;name:string};
  type Cohort={id:string;name:string;city_id:string;location_id:string;scope_id:string};
  type Terr={organization_id:Owner;cities:City[];locations:Site[];groups:Cohort[]};
  type Receipt={organization_id:Owner;request_id:string;staff_id:string;
    name:string;job_role:string;group_id:string;account_id:string|null;replayed:boolean};

  let {owner,actorId,groups,onSaved,onExpired,onRevoked}:{
    owner:Owner;actorId:string;groups:Group[];
    onSaved:()=>void;onExpired:()=>void;onRevoked:()=>void;
  }=$props();

  const jobs=['Head Coach','Assistant Coach','Goalkeeper Coach','Fitness Coach','Youth Coach'];
  let coachName=$state(''),job=$state('Youth Coach'),groupId=$state('');
  let accountId=$state(''),accountOffset=$state(0),accounts=$state<Account[]>([]);
  let accountsTotal=$state(0),accountsBusy=$state(false),accountsError=$state('');
  let sites=$state<Terr|null>(null),sitesBusy=$state(false),sitesError=$state('');
  let busy=$state(false),error=$state(''),receipt=$state<Receipt|null>(null);
  let nonce=$state<string|null>(null);

  const permittedIds=$derived(new Set(groups.map(g=>g.id)));
  const choices=$derived(owner==='lefteria_fc'
    ?groups.filter(g=>g.kind==='team').map(g=>({id:g.id,label:g.name}))
    :sites?.groups.filter(g=>permittedIds.has(g.id) &&
        sites?.locations.some(s=>s.id===g.location_id&&s.city_id===g.city_id))
      .map(g=>({
        id:g.id,label:[
          sites?.cities.find(c=>c.id===g.city_id)?.name,
          sites?.locations.find(s=>s.id===g.location_id)?.name,
          g.name
        ].join(' · ')
      }))??[]);
  const availableAccounts=$derived(accounts.filter(a=>
    a.role==='coach'&&a.active&&!a.invitation_pending&&a.organizations.includes(owner)));

  function edited(){
    nonce=null;receipt=null;error='';
  }
  function validTerr(value:unknown,expected:Owner):value is Terr{
    if(!value||typeof value!=='object'||Array.isArray(value))return false;
    const v=value as Record<string,unknown>;
    if(v.organization_id!==expected)return false;
    return ['cities','locations','groups'].every(k=>
      Array.isArray(v[k])&&(v[k] as unknown[]).length<=3000&&
      (v[k] as unknown[]).every(x=>
        x&&typeof x==='object'&&!Array.isArray(x)&&
        typeof (x as Record<string,unknown>).id==='string'&&
        typeof (x as Record<string,unknown>).name==='string'));
  }
  function validCandidates(value:unknown,requestedOffset:number):value is CandidatePage{
    if(!value||typeof value!=='object'||Array.isArray(value))return false;
    const v=value as Record<string,unknown>;
    return v.offset===requestedOffset&&v.limit===50&&
      Number.isSafeInteger(v.total)&&(v.total as number)>=0&&
      (v.total as number)<=25000&&Array.isArray(v.accounts)&&v.accounts.length<=50&&
      v.accounts.every((x:unknown)=>x&&typeof x==='object'&&!Array.isArray(x)&&
        typeof (x as Account).id==='string'&&typeof (x as Account).name==='string'&&
        typeof (x as Account).role==='string'&&
        typeof (x as Account).active==='boolean'&&
        typeof (x as Account).invitation_pending==='boolean'&&
        Array.isArray((x as Account).organizations));
  }

  $effect(()=>{
    const organization=owner,actor=actorId;
    void actor;
    coachName='';job='Youth Coach';groupId='';accountId='';
    accountOffset=0;nonce=null;receipt=null;error='';sites=null;
    if(!browser||organization!=='lefteria_fc_academy')return;
    const controller=new AbortController();
    sitesBusy=true;sitesError='';
    void(async()=>{
      try{
        const response=await fetch('/api/admin/football/territories?organization_id='+
          encodeURIComponent(organization),{credentials:'same-origin',cache:'no-store',
          headers:{Accept:'application/json'},signal:controller.signal});
        if(controller.signal.aborted)return;
        if(response.status===401){onExpired();return;}
        if(response.status===403){onRevoked();return;}
        if(!response.ok)throw new Error('Δεν είναι διαθέσιμες οι εγκαταστάσεις Ακαδημίας.');
        const raw:unknown=await response.json();
        if(!validTerr(raw,organization))throw new Error('Μη επαληθευμένη δομή εγκαταστάσεων.');
        if(!controller.signal.aborted&&owner===organization&&actorId===actor)sites=raw;
      }catch(e){if(!controller.signal.aborted)sitesError=e instanceof Error?e.message:'Αποτυχία εγκαταστάσεων.';}
      finally{if(!controller.signal.aborted)sitesBusy=false;}
    })();
    return()=>controller.abort();
  });

  $effect(()=>{
    const organization=owner,actor=actorId,offset=accountOffset;
    void actor;
    accounts=[];accountsBusy=false;accountsError='';
    if(!browser)return;
    const controller=new AbortController();
    accountsBusy=true;
    void(async()=>{
      try{
        const response=await fetch('/api/admin/access/users?offset='+offset+'&limit=50',{
          credentials:'same-origin',cache:'no-store',signal:controller.signal,
          headers:{Accept:'application/json'}
        });
        if(controller.signal.aborted)return;
        if(response.status===401){onExpired();return;}
        if(response.status===403){onRevoked();return;}
        if(!response.ok)throw new Error('Δεν είναι διαθέσιμοι οι λογαριασμοί Coach.');
        const data:unknown=await response.json();
        if(!validCandidates(data,offset))throw new Error('Μη επαληθευμένοι λογαριασμοί.');
        if(!controller.signal.aborted&&owner===organization&&actorId===actor){
          accounts=data.accounts;accountsTotal=data.total;
        }
      }catch(e){if(!controller.signal.aborted)accountsError=e instanceof Error?e.message:'Αποτυχία λογαριασμών.';}
      finally{if(!controller.signal.aborted)accountsBusy=false;}
    })();
    return()=>controller.abort();
  });

  $effect(()=>{
    if(!choices.some(c=>c.id===groupId))groupId=choices[0]?.id??'';
  });
  $effect(()=>{
    if(accountId&&!availableAccounts.some(a=>a.id===accountId))accountId='';
  });

  async function submit(){
    if(!browser||busy||owner==='lefteria_fc_academy'&&(!sites||sitesBusy||sitesError)||
      !choices.some(g=>g.id===groupId)||coachName.trim().length<2||
      !jobs.includes(job)||accountsBusy||accountsError||
      (!!accountId&&!availableAccounts.some(a=>a.id===accountId)))return;
    const selectedOwner=owner,selectedActor=actorId;
    const name=coachName.trim(),role=job,group=groupId,account=accountId||null;
    if(!window.confirm('Δημιουργία πραγματικής εγγραφής προπονητή «'+name+
      '» στο '+choices.find(x=>x.id===group)?.label+
      (account?' και σύνδεση του επιλεγμένου υπάρχοντος λογαριασμού Coach;':' χωρίς σύνδεση λογαριασμού;')+
      ' Η δημιουργία δεν δίνει αυτόματα πρόσθετα δικαιώματα.'))return;
    const id=nonce??window.crypto.randomUUID();
    nonce=id;busy=true;error='';receipt=null;
    try{
      const response=await fetch('/api/admin/staff/coaches',{
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({request_id:id,organization_id:selectedOwner,
          name,job_role:role,group_id:group,account_id:account})
      });
      if(selectedOwner!==owner||selectedActor!==actorId)return;
      if(response.status===401){onExpired();throw new Error('Η σύνδεση έληξε.');}
      if(response.status===403)throw new Error('Δεν επιτρέπεται η σύνδεση αυτού του λογαριασμού ή η δημιουργία στον οργανισμό.');
      if(response.status===409)throw new Error('Το αίτημα έχει ήδη χρησιμοποιηθεί με άλλα στοιχεία ή η ομάδα δεν έχει επαληθευμένη εγκατάσταση.');
      if(response.status===503)throw new Error('Το μητρώο προσωπικού δεν είναι διαθέσιμο.');
      if(!response.ok)throw new Error('Η δημιουργία προπονητή δεν ολοκληρώθηκε.');
      const raw:unknown=await response.json();
      if(!raw||typeof raw!=='object'||Array.isArray(raw))
        throw new Error('Δεν επαληθεύτηκε η αποθήκευση.');
      const result=raw as Record<string,unknown>;
      if(result.organization_id!==selectedOwner||result.request_id!==id||
        result.name!==name||result.job_role!==role||result.group_id!==group||
        result.account_id!==account||
        typeof result.staff_id!=='string'||
        !/^stf_admin_[0-9a-f]{32}$/.test(result.staff_id)||
        typeof result.replayed!=='boolean')
        throw new Error('Η απάντηση δεν συμφωνεί με την καταχώριση.');
      receipt=result as Receipt;
      coachName='';accountId='';nonce=null;
      onSaved();
    }catch(e){if(selectedOwner===owner&&selectedActor===actorId)
      error=e instanceof Error?e.message:'Αποτυχία δημιουργίας.';}
    finally{busy=false;}
  }
</script>

<section class="admin-panel native-coach" data-testid="admin-native-coach">
  <h2>Νέα εγγραφή προπονητή</h2>
  <p class="admin-muted">Μόνο Super Admin. Δημιουργεί πραγματική εγγραφή προσωπικού με μία επαληθευμένη ομάδα/εγκατάσταση. Προαιρετικά συνδέει έναν ήδη ενεργό λογαριασμό Coach χωρίς νέα πρόσβαση ή νέο κωδικό.</p>
  {#if receipt}
    <p role="status" data-testid="admin-native-coach-success">
      Ο προπονητής {receipt.name} καταχωρίστηκε στην ομάδα. Κωδικός εγγραφής: <code>{receipt.staff_id}</code>.
      {receipt.account_id?' Ο υπάρχων λογαριασμός συνδέθηκε.':' Δεν συνδέθηκε λογαριασμός.'}
    </p>
  {/if}
  {#if error}<p role="alert" data-testid="admin-native-coach-error">{error}</p>{/if}
  {#if sitesError}<p role="alert">{sitesError}</p>{/if}
  {#if accountsError}<p role="alert">{accountsError}</p>{/if}
  <form class="native-coach-form" onsubmit={e=>{e.preventDefault();void submit();}}>
    <label>Ονοματεπώνυμο προπονητή
      <input name="coach_name" required minlength="2" maxlength="200" autocomplete="off"
        value={coachName} disabled={busy} oninput={e=>{coachName=e.currentTarget.value;edited();}}
        placeholder="π.χ. Ανδρέας Ιωάννου"/>
    </label>
    <label>Θέση προσωπικού
      <select value={job} disabled={busy} onchange={e=>{job=e.currentTarget.value;edited();}}>
        {#each jobs as value (value)}<option value={value}>{value}</option>{/each}
      </select>
    </label>
    <label>Υποχρεωτική ομάδα / συγκεκριμένη εγκατάσταση
      <select value={groupId} disabled={busy||sitesBusy||choices.length===0}
        data-testid="admin-native-coach-group"
        onchange={e=>{groupId=e.currentTarget.value;edited();}}>
        {#each choices as g (g.id)}<option value={g.id}>{g.label}</option>{/each}
      </select>
    </label>
    {#if sitesBusy}<p role="status">Φόρτωση εγκαταστάσεων…</p>{/if}
    {#if choices.length===0&&!sitesBusy}
      <p>Δεν υπάρχει επαληθευμένη ομάδα με εγκατάσταση για καταχώριση.</p>
    {/if}
    <label>Σύνδεση υπάρχοντος λογαριασμού Coach (προαιρετικά)
      <select value={accountId} disabled={busy||accountsBusy||!!accountsError}
        data-testid="admin-native-coach-account"
        onchange={e=>{accountId=e.currentTarget.value;edited();}}>
        <option value="">Χωρίς σύνδεση λογαριασμού</option>
        {#each availableAccounts as candidate (candidate.id)}
          <option value={candidate.id}>{candidate.name}</option>
        {/each}
      </select>
    </label>
    {#if accountsBusy}<p role="status">Φόρτωση λογαριασμών…</p>{/if}
    {#if accountsTotal>50}
      <div class="native-coach-paging">
        <span>Λογαριασμοί {accountOffset+1}–{Math.min(accountOffset+50,accountsTotal)} από {accountsTotal}</span>
        <button type="button" class="admin-secondary" disabled={busy||accountsBusy||accountOffset===0}
          onclick={()=>{accountOffset=Math.max(0,accountOffset-50);edited();}}>Προηγούμενοι</button>
        <button type="button" class="admin-secondary" disabled={busy||accountsBusy||accountOffset+50>=accountsTotal}
          onclick={()=>{accountOffset+=50;edited();}}>Επόμενοι</button>
      </div>
    {/if}
    <p class="admin-muted">Οι λογαριασμοί διαχειρίζονται ξεχωριστά από το <a href="/admin/users">Χρήστες & δικαιώματα</a>. Δεν πραγματοποιείται αυτόματη πρόσκληση, άνοιγμα λογαριασμού ή πρόσβαση σε άλλες εγκαταστάσεις.</p>
    <button type="submit" disabled={busy||accountsBusy||!!accountsError||
      sitesBusy||!!sitesError||choices.length===0||coachName.trim().length<2}>
      {busy?'Καταχώριση…':'Δημιουργία προπονητή'}
    </button>
  </form>
</section>

<style>
  .native-coach{margin:16px 0}
  .native-coach h2{margin:0 0 9px;font-size:1.25rem}
  .native-coach-form{display:grid;gap:13px;max-width:690px;margin-top:16px}
  .native-coach-form label{display:grid;gap:6px;font-weight:650}
  .native-coach-form input,.native-coach-form select{
    color:inherit;background:#181d1e;border:1px solid #75726b;
    min-height:44px;padding:10px 12px;border-radius:8px;width:100%}
  .native-coach-form button{min-height:44px;padding:9px 12px}
  .native-coach-paging{display:flex;gap:8px;align-items:center;flex-wrap:wrap}
  .native-coach-paging span{font-size:13px}
  [role="alert"]{font-weight:700}
  code{overflow-wrap:anywhere}
  @media(max-width:600px){.native-coach-paging{display:grid}}
</style>
