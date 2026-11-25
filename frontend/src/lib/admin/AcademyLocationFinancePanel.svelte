<script lang="ts">
  import {browser} from '$app/env';
  type Site={city_id:string;location_id:string;
    training_issued_count:number;training_issued_cents:number;training_paid_cents:number;
    training_cancelled_count:number;training_cancelled_cents:number;
    training_review_count:number;general_issued_count:number;general_issued_cents:number;
    general_paid_cents:number;general_overdue_count:number};
  type BacklogIssue={kind:'training_fee'|'general_charge';id:string;
    player_id:string;player_name:string;amount_cents:number;month:string};
  type Backlog={organization_id:string;month:string;total_count:number;issues:BacklogIssue[]};
  type Directory={organization_id:string;month:string;currency:'EUR';locations:Site[]};
  let {actorId,cityId,locationId,locations,superAdmin,revision=0}:{
    actorId:string;cityId:string;locationId:string;superAdmin:boolean;
    locations:{id:string;city_id:string;name:string}[];revision?:number;
  }=$props();
  const owner='lefteria_fc_academy';
  const today=new Date();
  let month=$state(new Date(Date.UTC(today.getUTCFullYear(),today.getUTCMonth(),1))
    .toISOString().slice(0,7));
  let data=$state<Directory|null>(null);
  let backlog=$state<Backlog|null>(null);
  let error=$state(''),notice=$state('');
  let loading=$state(false),saving=$state(false),refresh=$state(0);
  let selectedIssue=$state(''),selectedSite=$state(''),evidence=$state('');
  const euro=(cents:number)=>new Intl.NumberFormat('el-CY',{
    style:'currency',currency:'EUR'
  }).format(cents/100);
  const site=$derived(data?.locations.find(x=>x.city_id===cityId &&
    x.location_id===locationId));
  const city=$derived((data?.locations??[]).filter(x=>x.city_id===cityId));
  const cityTraining=$derived(city.reduce((n,x)=>n+x.training_issued_cents,0));
  const cityGeneral=$derived(city.reduce((n,x)=>n+x.general_issued_cents,0));
  const selection=$derived(backlog?.issues.find(i=>i.kind+'|'+i.id===selectedIssue));
  $effect(()=>{
    const identity=actorId,requested=month,rev=revision,reload=refresh,admin=superAdmin;
    void rev;void reload;
    data=null;backlog=null;error='';loading=false;
    if(!browser||!identity||!/^(20[2-9][0-9]|2100)-(0[1-9]|1[0-2])$/.test(requested))
      return;
    const ctrl=new AbortController();
    loading=true;
    void(async()=>{
      try{
        const base='/api/admin/football/territories/finance?organization_id='+
          encodeURIComponent(owner)+'&month='+encodeURIComponent(requested);
        const opts={credentials:'same-origin' as const,cache:'no-store' as const,
          headers:{Accept:'application/json'},signal:ctrl.signal};
        const response=await fetch(base,opts);
        if(!response.ok)throw new Error('Τα οικονομικά στοιχεία δεν είναι διαθέσιμα.');
        const value:unknown=await response.json();
        if(!value||typeof value!=='object'||Array.isArray(value)
          ||(value as Record<string,unknown>).organization_id!==owner
          ||(value as Record<string,unknown>).month!==requested+'-01'
          ||(value as Record<string,unknown>).currency!=='EUR'
          ||!Array.isArray((value as Record<string,unknown>).locations))
          throw new Error('Μη επαληθευμένα οικονομικά δεδομένα.');
        const d=value as Directory;
        if(d.locations.length>3000||!d.locations.every(x=>
          typeof x.city_id==='string'&&typeof x.location_id==='string'&&
          ['training_issued_count','training_issued_cents','training_paid_cents',
            'training_cancelled_count','training_cancelled_cents',
          'training_review_count','general_issued_count','general_issued_cents',
          'general_paid_cents','general_overdue_count']
          .every(k=>Number.isSafeInteger(x[k as keyof Site])&&
            (x[k as keyof Site] as number)>=0)))
          throw new Error('Μη έγκυρα οικονομικά ποσά.');
        let u:Backlog|null=null;
        if(admin){
          const reply=await fetch('/api/admin/football/territories/finance/unallocated?'+
            'organization_id='+encodeURIComponent(owner)+'&month='+encodeURIComponent(requested),opts);
          if(!reply.ok)throw new Error('Ο έλεγχος μη κατανεμημένων χρεώσεων απέτυχε.');
          const raw:unknown=await reply.json();
          if(!raw||typeof raw!=='object'||Array.isArray(raw)
            ||(raw as Record<string,unknown>).organization_id!==owner
            ||(raw as Record<string,unknown>).month!==requested+'-01'
            ||!Number.isSafeInteger((raw as Record<string,unknown>).total_count)
            ||!Array.isArray((raw as Record<string,unknown>).issues))
            throw new Error('Μη επαληθευμένες εκκρεμείς χρεώσεις.');
          u=raw as Backlog;
          if(u.issues.length>100||u.issues.some(i=>!['training_fee','general_charge'].includes(i.kind)
            ||typeof i.id!=='string'||typeof i.player_name!=='string'
            ||!Number.isSafeInteger(i.amount_cents)||i.amount_cents<0))
            throw new Error('Μη έγκυρη λίστα εκκρεμών χρεώσεων.');
        }
        if(!ctrl.signal.aborted&&actorId===identity&&month===requested){
          data=d;backlog=u;
        }
      }catch(e){if(!ctrl.signal.aborted)error=e instanceof Error?e.message:'Αποτυχία φόρτωσης.';}
      finally{if(!ctrl.signal.aborted)loading=false;}
    })();
    return()=>ctrl.abort();
  });
  $effect(()=>{
    if(!locations.some(x=>x.id===selectedSite)){
      selectedSite=locations[0]?.id??'';
    }
  });
  async function allocate(){
    if(!browser||!superAdmin||!selection||!selectedSite||!evidence.trim()||saving)return;
    const issue=selection,location=selectedSite,reference=evidence,identity=actorId;
    if(!window.confirm('Επιβεβαιώνετε ότι έχετε ελέγξει την αρχική χρέωση και την πραγματική τοποθεσία;'))return;
    saving=true;error='';notice='';
    try{
      const response=await fetch('/api/admin/football/territories/finance/allocate?'+
        'organization_id='+encodeURIComponent(owner),{
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({kind:issue.kind,issue_id:issue.id,location_id:location,
          evidence:reference})
      });
      if(actorId!==identity)return;
      if(!response.ok)throw new Error(response.status===409
        ?'Η χρέωση έχει ήδη κατανεμηθεί. Ανανεώστε για να δείτε την επαληθευμένη κατάσταση.'
        :'Αποτυχία επαλήθευσης ή ανάθεσης της χρέωσης.');
      const body:unknown=await response.json();
      if(!body||typeof body!=='object'||Array.isArray(body)
        ||(body as Record<string,unknown>).issue_id!==issue.id
        ||(body as Record<string,unknown>).location_id!==location
        ||(body as Record<string,unknown>).organization_id!==owner
        ||(body as Record<string,unknown>).method!=='manually_verified')
        throw new Error('Η επιβεβαίωση δεν αντιστοιχεί στη χρέωση.');
      selectedIssue='';evidence='';
      notice='Η επιβεβαιωμένη χρέωση συνδέθηκε με την τοποθεσία χωρίς αλλαγή τιμολογίου.';
      refresh++;
    }catch(e){error=e instanceof Error?e.message:'Η ανάθεση απέτυχε.';}
    finally{saving=false;}
  }
</script>
<section class="academy-site-finance" aria-labelledby="site-finance-heading"
  data-testid="academy-verified-location-finance">
  <h3 id="site-finance-heading">Οικονομικά ανά τοποθεσία · επαληθευμένες χρεώσεις</h3>
  <p>Μόνο χρεώσεις με επιβεβαιωμένη τοποθεσία τη στιγμή έκδοσης ή ελεγμένη
    χειροκίνητη ανάθεση. Οι ιστορικές χρεώσεις χωρίς επιβεβαίωση δεν προστίθενται στα ποσά.</p>
  <label>Μήνας
    <input type="month" bind:value={month} min="2020-01" max="2100-12"/>
  </label>
  {#if loading}
    <p role="status">Φόρτωση οικονομικών στοιχείων…</p>
  {:else if site}
    <div class="finance-cards">
      <div><strong>{euro(site.training_issued_cents)}</strong><span>Μηνιαία δίδακτρα · εκδόθηκαν ({site.training_issued_count})</span></div>
      <div><strong>{euro(site.training_paid_cents)}</strong><span>Δίδακτρα · εξοφλημένα</span></div>
      <div><strong>{euro(site.training_cancelled_cents)}</strong><span>Δίδακτρα · ακυρωμένα ({site.training_cancelled_count})</span></div>
      <div><strong>{euro(site.general_issued_cents)}</strong><span>Λοιπές χρεώσεις · εκδόθηκαν ({site.general_issued_count})</span></div>
      <div><strong>{euro(site.general_paid_cents)}</strong><span>Λοιπές χρεώσεις · καταχωρισμένες πληρωμές</span></div>
    </div>
    {#if site.training_review_count||site.general_overdue_count}
      <p role="status">{site.training_review_count} δίδακτρα για έλεγχο · {site.general_overdue_count} λοιπές ληξιπρόθεσμες χρεώσεις.</p>
    {/if}
    <small>Τα δύο καθολικά παρουσιάζονται χωριστά για να μην γίνει διπλή καταμέτρηση
      παλαιών και νέων χρεώσεων.</small>
    {#if city.length>1}
      <p>Επιβεβαιωμένες χρεώσεις πόλης ({city.length} τοποθεσίες):
        Δίδακτρα {euro(cityTraining)} · Λοιπές {euro(cityGeneral)}</p>
    {/if}
  {:else if data}
    <p>Δεν υπάρχουν εξουσιοδοτημένα στοιχεία για αυτή την τοποθεσία.</p>
  {/if}
  {#if superAdmin && backlog && !loading}
    <div class="finance-review">
      <h4>Μη κατανεμημένες χρεώσεις · {backlog.total_count}</h4>
      <p>Η ανάθεση επιτρέπεται μόνο σε Super Admin με αποδεικτικό αρχικής τοποθεσίας.
        Δεν αλλάζει το τιμολόγιο, το ποσό, την κατάσταση πληρωμής ή τα Payabl γεγονότα.</p>
      {#if backlog.issues.length}
        <form onsubmit={e=>{e.preventDefault();void allocate();}}>
          <label>Χρέωση
            <select bind:value={selectedIssue} required>
              <option value="">Επιλέξτε χρέωση προς επαλήθευση</option>
              {#each backlog.issues as issue (issue.kind+'-'+issue.id)}
                <option value={issue.kind+'|'+issue.id}>{issue.player_name} ·
                  {issue.kind==='training_fee'?'Δίδακτρα':'Λοιπή χρέωση'} ·
                  {euro(issue.amount_cents)}</option>
              {/each}
            </select>
          </label>
          <label>Επαληθευμένη τοποθεσία
            <select bind:value={selectedSite} required>
              {#each locations as loc (loc.id)}
                <option value={loc.id}>{loc.name}</option>
              {/each}
            </select>
          </label>
          <label>Αριθμός παραστατικού / πηγή επαλήθευσης
            <input bind:value={evidence} maxlength="160" minlength="5" required/>
          </label>
          <button type="submit" disabled={saving||!selection||!selectedSite||evidence.trim().length<5}>
            Σύνδεση με επαληθευμένη τοποθεσία
          </button>
        </form>
      {:else if backlog.total_count>0}
        <p>Υπάρχουν περισσότερες μη κατανεμημένες χρεώσεις. Η λίστα δείχνει έως 100 ανά μήνα.</p>
      {:else}
        <p>Δεν υπάρχουν μη κατανεμημένες χρεώσεις αυτόν τον μήνα.</p>
      {/if}
    </div>
  {/if}
  {#if notice}<p role="status">{notice}</p>{/if}
  {#if error}<p role="alert">{error}</p>{/if}
</section>
<style>
  .academy-site-finance{border:1px solid rgba(173,137,93,.48);border-radius:1rem;padding:1.1rem;margin-top:1.2rem}
  h3,h4{margin:.5rem 0;font-size:1.07rem}
  p{max-width:74ch}
  label{display:grid;gap:.4rem;font-weight:650;margin:.7rem 0}
  input,select{min-height:44px;border:1px solid #8c806b;background:transparent;color:inherit;padding:.65rem;max-width:650px;width:100%;border-radius:.5rem}
  .finance-cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:.7rem}
  .finance-cards div{display:grid;gap:.4rem;padding:.8rem;border:1px solid rgba(173,137,93,.45);border-radius:.55rem}
  .finance-cards strong{font-size:1.2rem}
  .finance-cards span,small{font-size:.83rem}
  button{min-height:44px;cursor:pointer}
  button:focus-visible,input:focus-visible,select:focus-visible{outline:3px solid #bd9763;outline-offset:3px}
  button:disabled{opacity:.5}
  .finance-review{border-top:1px solid rgba(173,137,93,.5);padding-top:.8rem;margin-top:1.1rem}
  .finance-review form{display:grid;gap:.5rem}
</style>
