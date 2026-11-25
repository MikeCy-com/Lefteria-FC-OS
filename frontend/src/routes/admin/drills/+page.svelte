<script lang="ts">
  import {getContext,untrack} from 'svelte';
  import {browser} from '$app/env';
  import {goto} from '$app/navigation';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';
  import {ADMIN_AUTH_CONTEXT,type AdminAuthController} from '../../../lib/admin/controller.ts';
  import {hasAdminPermission} from '../../../lib/admin/permissions.ts';
  import {drillCategories,drillAges,drillDifficulties,type DrillDifficulty,validDrillForm,validDrill,validDrillPage,canReadDrillLibrary,
    type Drill,type DrillForm} from '../../../lib/admin/central-academy-drills.ts';
  const auth=getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  const labels:Record<string,string>={technical:'Τεχνική',tactical:'Τακτική',physical:'Φυσική κατάσταση',
    goalkeeping:'Τερματοφύλακες',warmup:'Προθέρμανση',game:'Παιχνίδι',recovery:'Αποκατάσταση',other:'Άλλο',
    draft:'Πρόχειρο',approved:'Εγκεκριμένο',archived:'Αρχείο',beginner:'Αρχάριοι',intermediate:'Μεσαίο επίπεδο',advanced:'Προχωρημένοι'};
  const blank=():DrillForm=>({title:'',category:'technical',age_band:'all',duration_minutes:15,objective:'',instructions:''});
  const allowed=$derived($auth.status==='authenticated'&&!!$auth.session&&
    canReadDrillLibrary($auth.session.user.role,$auth.session.user.organizations)&&
    (hasAdminPermission($auth.session.user.permissions,'training:read')||
      hasAdminPermission($auth.session.user.permissions,'training:read_assigned')));
  const canWrite=$derived(allowed&&$auth.session?.user.role==='super_admin'&&
    hasAdminPermission($auth.session.user.permissions,'training:write'));
  const canPropose=$derived(allowed&&$auth.session?.user.role==='coach'&&hasAdminPermission($auth.session.user.permissions,'training:write_assigned'));
  const canSeeProposals=$derived(canPropose||canWrite);
  let view=$state<'catalogue'|'proposals'>('catalogue');
  const scope=$derived(allowed?$auth.session?.user.id+':'+$auth.session?.user.role:'');
  let search=$state(''),appliedSearch=$state(''),category=$state('all'),age=$state('any'),status=$state('approved');
  let offset=$state(0),nextOffset=$state<number|null>(null),refresh=$state(0);
  let rows=$state<Drill[]>([]),loading=$state(false),saving=$state(false),loaded=$state(false);
  let error=$state(''),writeError=$state(''),notice=$state('');
  let form=$state<DrillForm>(blank()),editing=$state<Drill|null>(null);
  let source=$state<Drill|null>(null),proposalKind=$state<'revision'|'variant'>('variant'),difficulty=$state<DrillDifficulty>('intermediate');
  const base='/api/admin/academy/drills/';
  $effect(()=>{if(browser&&$auth.status==='anonymous')void goto('/admin/login',{replace:true});});
  $effect(()=>{
    void scope;
    untrack(()=>{view='catalogue';search='';appliedSearch='';category='all';age='any';status='approved';offset=0;
      form=blank();editing=null;source=null;proposalKind='variant';difficulty='intermediate';notice='';writeError='';saving=false;});
  });
  $effect(()=>{
    const owner=scope,s=appliedSearch,c=category,a=age,st=canWrite?status:'approved',o=offset,proposals=view==='proposals';
    void refresh;
    rows=[];nextOffset=null;loaded=false;error='';loading=false;
    untrack(()=>{if(editing){editing=null;form=blank();}});
    if(!browser||!owner||(proposals&&!canSeeProposals))return;
    const controller=new AbortController();
    loading=true;
    untrack(()=>{void(async()=>{
      try{
        const query=new URLSearchParams({search:s,category:c,age:a,status:st,offset:String(o)});
        const response=await fetch(proposals?base+'proposals?offset='+o:base+'?'+query,{credentials:'same-origin',cache:'no-store',
          headers:{Accept:'application/json'},signal:controller.signal});
        if(controller.signal.aborted||scope!==owner)return;
        if(response.status===401||response.status===403){void auth.bootstrap();throw Error('Η πρόσβαση άλλαξε.');}
        if(!response.ok)throw Error('Η βιβλιοθήκη δεν είναι διαθέσιμη.');
        const value:unknown=await response.json();
        if(controller.signal.aborted||scope!==owner)return;
        if(!validDrillPage(value,proposals?(canWrite?'draft':'all'):st,o,proposals))throw Error('Μη επαληθευμένη απόκριση βιβλιοθήκης.');
        rows=value.drills;nextOffset=value.next_offset;loaded=true;
      }catch(e){if(!controller.signal.aborted&&scope===owner)error=e instanceof Error?e.message:'Αποτυχία φόρτωσης.';}
      finally{if(!controller.signal.aborted&&scope===owner)loading=false;}
    })();});
    return()=>controller.abort();
  });
  function switchView(next:'catalogue'|'proposals'){view=next;offset=0;editing=null;source=null;form=blank();notice='';writeError='';refresh++;}
  function searchNow(){appliedSearch=search.trim();offset=0;refresh++;}
  function edit(row:Drill){
    if(!canWrite||saving||loading||row.status==='archived')return;
    editing=row;form={title:row.title,category:row.category,age_band:row.age_band,
      duration_minutes:row.duration_minutes,objective:row.objective,instructions:row.instructions};
    notice='';writeError='';
  }
  function reset(){editing=null;source=null;form=blank();difficulty='intermediate';}
  function proposeFrom(row:Drill,kind:'revision'|'variant'){
    if(!canPropose||saving||loading||view!=='catalogue'||row.status!=='approved')return;
    source=row;proposalKind=kind;difficulty=row.difficulty??'intermediate';
    form={title:row.title,category:row.category,age_band:row.age_band,duration_minutes:row.duration_minutes,objective:row.objective,instructions:row.instructions};
    notice='';writeError='';
  }
  async function propose(){
    if(!browser||!canPropose||saving||loading||!scope||!validDrillForm(form))return;
    if(!window.confirm('Υποβολή της νέας άσκησης για έλεγχο από τον διαχειριστή της κεντρικής βιβλιοθήκης;'))return;
    const owner=scope,baseDrill=source,kind=proposalKind,level=difficulty,body={...form};
    saving=true;notice='';writeError='';
    try{
      const response=await fetch(base+(baseDrill?'proposals/linked':'proposals'),{method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json',Accept:'application/json','X-Lefteria-Admin':'1'},body:JSON.stringify(baseDrill?{...body,base_drill_id:baseDrill.id,base_version:baseDrill.version,proposal_kind:kind,difficulty:level}:body)});
      if(scope!==owner||!canPropose)return;
      if(response.status===401||response.status===403){void auth.bootstrap();throw Error('Η πρόσβαση άλλαξε.');}
      if(!response.ok)throw Error('Η υποβολή δεν επιβεβαιώθηκε.');
      const value:unknown=await response.json();
      if(scope!==owner||!canPropose)return;
      if(!validDrill(value,!!baseDrill)||value.status!=='draft'||value.version!==1||
        (baseDrill&&(value.base_drill_id!==baseDrill.id||value.base_version!==baseDrill.version||value.proposal_kind!==kind||value.difficulty!==level||value.applied_version!==null))||
        value.title!==body.title||value.category!==body.category||value.age_band!==body.age_band||
        value.duration_minutes!==body.duration_minutes||value.objective!==body.objective||value.instructions!==body.instructions)
        throw Error('Η νέα πρόταση δεν επαληθεύτηκε.');
      reset();view='proposals';offset=0;notice='Η πρόταση υποβλήθηκε για έλεγχο. Δεν είναι ακόμη διαθέσιμη στην κοινή βιβλιοθήκη.';
    }catch(e){if(scope===owner&&canPropose)writeError=e instanceof Error?e.message:'Η υποβολή δεν επιβεβαιώθηκε.';}
    finally{if(scope===owner){saving=false;refresh++;}}
  }
  async function applyRevision(row:Drill){
    if(!canWrite||saving||loading||view!=='proposals'||row.proposal_kind!=='revision'||row.status!=='draft'||!row.base_version||
      !rows.some(x=>x.id===row.id&&x.version===row.version))return;
    if(!window.confirm('Εφαρμογή της πρότασης στην αρχική άσκηση; Η προηγούμενη εγκεκριμένη έκδοση θα διατηρηθεί στο ιστορικό.'))return;
    const owner=scope;saving=true;writeError='';notice='';
    try{
      const response=await fetch(base+'proposals/'+encodeURIComponent(row.id)+'/apply-revision',{method:'POST',
        credentials:'same-origin',cache:'no-store',headers:{'Content-Type':'application/json',Accept:'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({expected_version:row.version,base_version:row.base_version,confirm_review:true})});
      if(scope!==owner||!canWrite)return;
      if(response.status===401||response.status===403){void auth.bootstrap();throw Error('Η πρόσβαση άλλαξε.');}
      if(response.status===409)throw Error('Η αρχική άσκηση ή η πρόταση άλλαξε. Απαιτείται νέα πρόταση στην τρέχουσα έκδοση.');
      if(!response.ok)throw Error('Η εφαρμογή της αλλαγής δεν επιβεβαιώθηκε.');
      const value:unknown=await response.json();
      if(scope!==owner||!canWrite)return;
      if(!validDrill(value,true)||value.id!==row.id||value.version!==row.version+1||value.status!=='archived'||
        value.proposal_kind!=='revision'||value.base_drill_id!==row.base_drill_id||value.base_version!==row.base_version||
        value.applied_version!==row.base_version+1)throw Error('Η νέα εγκεκριμένη έκδοση δεν επαληθεύτηκε.');
      reset();notice='Η αλλαγή εγκρίθηκε και εφαρμόστηκε στην αρχική άσκηση.';
    }catch(e){if(scope===owner&&canWrite)writeError=e instanceof Error?e.message:'Η αλλαγή δεν επιβεβαιώθηκε.';}
    finally{if(scope===owner){saving=false;refresh++;}}
  }
  async function write(action:'create'|'edit'|'approve'|'archive'|'restore',row:Drill|null){
    if(!browser||!canWrite||saving||loading||!scope)return;
    const content=action==='create'||action==='edit';
    if(content&&!validDrillForm(form))return;
    if((action==='create')!==(row===null))return;
    if(row&&!rows.some(x=>x.id===row.id&&x.version===row.version&&x.status===row.status))return;
    if(!content&&row&&!window.confirm(labels[action==='approve'?'approved':action==='archive'?'archived':'draft']+
      ' — '+row.title+' · έκδοση '+row.version+';'))return;
    const owner=scope,expected=row?.version??0;
    const body={action,expected_version:expected,title:null,category:null,age_band:null,
      duration_minutes:null,objective:null,instructions:null,...(content?form:{})};
    const desired=action==='approve'?'approved':action==='archive'?'archived':'draft';
    saving=true;notice='';writeError='';
    try{
      const response=await fetch(row?base+encodeURIComponent(row.id):base,{
        method:row?'PUT':'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json',Accept:'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify(body)});
      if(scope!==owner||!canWrite)return;
      if(response.status===401||response.status===403){void auth.bootstrap();throw Error('Τα δικαιώματα άλλαξαν.');}
      if(response.status===409)throw Error('Η έκδοση άλλαξε. Ελέγξτε ξανά την άσκηση.');
      if(!response.ok)throw Error('Η αλλαγή δεν επιβεβαιώθηκε.');
      const value:unknown=await response.json();
      if(scope!==owner||!canWrite)return;
      if(!validDrill(value)||(row&&value.id!==row.id)||value.version!==expected+1||value.status!==desired||
        (content&&(value.title!==body.title||value.category!==body.category||value.age_band!==body.age_band||
          value.duration_minutes!==body.duration_minutes||value.objective!==body.objective||value.instructions!==body.instructions)))
        throw Error('Η νέα έκδοση δεν επαληθεύτηκε.');
      reset();notice='Η νέα έκδοση αποθηκεύτηκε: '+labels[desired]+'.';
      status=desired;offset=0;
    }catch(e){if(scope===owner&&canWrite){writeError=e instanceof Error?e.message:'Η αλλαγή δεν επιβεβαιώθηκε.';reset();}}
    finally{if(scope===owner){saving=false;refresh++;}}
  }
</script>

<svelte:head><title>Βιβλιοθήκη ασκήσεων | Λευτεριά FC OS</title><meta name="robots" content="noindex"/></svelte:head>
{#if $auth.status==='authenticated'&&$auth.session}
  <AdminFrame user={$auth.session.user} active="drills" busy={$auth.busy} onLogout={()=>void auth.logout()}>
    <section class="library" aria-labelledby="library-title" data-testid="central-academy-drills">
      <h1 id="library-title">Βιβλιοθήκη ασκήσεων</h1>
      <p class="muted">Μία κοινή βιβλιοθήκη για όλες τις πόλεις, τοποθεσίες και ομάδες της Ακαδημίας.</p>
      {#if !allowed}
        <p role="alert">Δεν έχετε δικαίωμα πρόσβασης στην κεντρική βιβλιοθήκη.</p>
      {:else}
        {#if canSeeProposals}<nav class="actions" aria-label="Βιβλιοθήκη και προτάσεις">
          <button type="button" aria-pressed={view==='catalogue'} disabled={saving} onclick={()=>switchView('catalogue')}>Κεντρική βιβλιοθήκη</button>
          <button type="button" aria-pressed={view==='proposals'} disabled={saving} onclick={()=>switchView('proposals')}>{canPropose?'Οι προτάσεις μου':'Προτάσεις προπονητών · έλεγχος'}</button>
        </nav>{/if}
        {#if view==='catalogue'}
        <form class="filters" onsubmit={e=>{e.preventDefault();searchNow();}}>
          <label>Αναζήτηση<input bind:value={search} maxlength="100" disabled={saving} placeholder="Τίτλος ή στόχος"/></label>
          <label>Κατηγορία<select bind:value={category} disabled={saving} onchange={()=>{offset=0;}}>
            <option value="all">Όλες</option>{#each drillCategories as c}<option value={c}>{labels[c]}</option>{/each}
          </select></label>
          <label>Ηλικία<select bind:value={age} disabled={saving} onchange={()=>{offset=0;}}>
            <option value="any">Όλες</option>{#each drillAges as a}<option value={a}>{a==='all'?'Κάθε ηλικία':a.toUpperCase()}</option>{/each}
          </select></label>
          {#if canWrite}<label>Κατάσταση<select bind:value={status} disabled={saving} onchange={()=>{offset=0;}}>
            <option value="all">Όλες</option><option value="approved">Εγκεκριμένες</option>
            <option value="draft">Πρόχειρες</option><option value="archived">Αρχείο</option>
          </select></label>{/if}
          <button type="submit" disabled={saving}>Αναζήτηση</button>
        </form>
        {:else}<p class="muted">{canPropose?'Παρακολουθήστε τις δικές σας προτάσεις. Μόνο οι εγκεκριμένες ασκήσεις προστίθενται στην κεντρική βιβλιοθήκη.':'Ελέγξτε τις προτάσεις των προπονητών και εγκρίνετε την ακριβή έκδοση ή αρχειοθετήστε την.'}</p>{/if}
        {#if loading}<p role="status">Φόρτωση βιβλιοθήκης…</p>
        {:else if error}<p role="alert">{error}</p><button type="button" disabled={saving} onclick={()=>refresh++}>Δοκιμή ξανά</button>
        {:else if loaded&&!rows.length}<p>Δεν βρέθηκαν ασκήσεις.</p>
          {#if offset>0}<button type="button" disabled={saving} onclick={()=>offset=Math.max(0,offset-30)}>Προηγούμενη σελίδα</button>{/if}
        {:else if loaded}
          <div class="drill-grid">
            {#each rows as row (row.id)}
              <article class="drill">
                <div class="metadata">{labels[row.category]} · {row.age_band.toUpperCase()} · {row.duration_minutes}′</div>
                <h2>{row.title}</h2>
                {#if row.difficulty}<p class="metadata">{labels[row.difficulty]}{row.variant_of?' · Παραλλαγή':''}</p>{/if}
                {#if row.proposal_kind}<p class="muted">{row.proposal_kind==='revision'?'Προτεινόμενη αλλαγή':'Νέα παραλλαγή'} · Αρχική έκδοση {row.base_version}</p>{/if}
                <p>{row.objective}</p>
                <details><summary>Οδηγίες άσκησης</summary><p>{row.instructions}</p></details>
                {#if view==='proposals'&&canPropose}<p class="muted">{row.applied_version?'Εφαρμόστηκε στην αρχική άσκηση · έκδοση '+row.applied_version:row.status==='draft'?'Αναμένει έλεγχο':row.status==='approved'?'Εγκρίθηκε για κοινή χρήση':'Αρχειοθετήθηκε'} · Έκδοση {row.version}</p>{/if}
                {#if canPropose&&view==='catalogue'&&row.status==='approved'}<div class="actions">
                  <button type="button" disabled={saving||loading} onclick={()=>proposeFrom(row,'revision')}>Πρόταση αλλαγής</button>
                  <button type="button" disabled={saving||loading} onclick={()=>proposeFrom(row,'variant')}>Νέα παραλλαγή</button>
                </div>{/if}
                {#if canWrite}
                  <p class="muted">{labels[row.status]} · Έκδοση {row.version}</p>
                  <div class="actions">
                    {#if row.status!=='archived'}
                      <button type="button" disabled={saving} onclick={()=>edit(row)}>Επεξεργασία</button>
                      {#if row.status==='draft'&&row.proposal_kind==='revision'}<button type="button" disabled={saving} onclick={()=>void applyRevision(row)}>Έγκριση αλλαγής αρχικής άσκησης</button>
                      {:else if row.status==='draft'}<button type="button" disabled={saving} onclick={()=>void write('approve',row)}>Έγκριση έκδοσης</button>{/if}
                      <button type="button" disabled={saving} onclick={()=>void write('archive',row)}>Αρχειοθέτηση</button>
                    {:else if !row.applied_version}<button type="button" disabled={saving} onclick={()=>void write('restore',row)}>Επαναφορά ως πρόχειρο</button>{/if}
                  </div>
                {/if}
              </article>
            {/each}
          </div>
          <nav class="actions" aria-label="Σελίδες βιβλιοθήκης">
            <button type="button" disabled={saving||offset===0} onclick={()=>offset=Math.max(0,offset-30)}>Προηγούμενη</button>
            <span>Σελίδα {Math.floor(offset/30)+1}</span>
            <button type="button" disabled={saving||nextOffset===null} onclick={()=>{if(nextOffset!==null)offset=nextOffset;}}>Επόμενη</button>
          </nav>
        {/if}
        {#if canWrite||canPropose}
          <section class="editor" aria-labelledby="editor-title">
            <h2 id="editor-title">{editing?'Επεξεργασία · έκδοση '+editing.version:canPropose?(source?(proposalKind==='revision'?'Πρόταση αλλαγής':'Νέα παραλλαγή'):'Πρόταση νέας άσκησης'):'Νέα άσκηση'}</h2>
            <p class="muted">{canPropose?'Υποβάλετε νέο εκπαιδευτικό περιεχόμενο για έγκριση από τον διαχειριστή. Οι υπάρχουσες εγκεκριμένες ασκήσεις επιλέγονται ελεύθερα στις προπονήσεις.':'Η αποθήκευση δημιουργεί πρόχειρη έκδοση. Η επεξεργασία εγκεκριμένης άσκησης απαιτεί νέα έγκριση.'}</p>
            {#if notice}<p role="status">{notice}</p>{/if}
            {#if writeError}<p role="alert">{writeError}</p>{/if}
            <form class="fields" onsubmit={e=>{e.preventDefault();if(canPropose)void propose();else void write(editing?'edit':'create',editing);}}>
              {#if source}<p class="wide muted">Αρχική άσκηση: {source.title} · έκδοση {source.version}</p>
                <label>Είδος πρότασης<select bind:value={proposalKind} disabled={saving}><option value="revision">Αλλαγή της αρχικής</option><option value="variant">Ξεχωριστή παραλλαγή</option></select></label>
                <label>Επίπεδο δυσκολίας<select bind:value={difficulty} disabled={saving}>{#each drillDifficulties as level}<option value={level}>{labels[level]}</option>{/each}</select></label>
              {/if}
              <label>Τίτλος<input bind:value={form.title} maxlength="180" required disabled={saving}/></label>
              <label>Κατηγορία<select bind:value={form.category} disabled={saving}>{#each drillCategories as c}<option value={c}>{labels[c]}</option>{/each}</select></label>
              <label>Ηλικία<select bind:value={form.age_band} disabled={saving}>{#each drillAges as a}<option value={a}>{a==='all'?'Κάθε ηλικία':a.toUpperCase()}</option>{/each}</select></label>
              <label>Διάρκεια (λεπτά)<input type="number" bind:value={form.duration_minutes} min="1" max="120" required disabled={saving}/></label>
              <label class="wide">Στόχος<input bind:value={form.objective} maxlength="1000" required disabled={saving}/></label>
              <label class="wide">Οδηγίες<textarea bind:value={form.instructions} maxlength="4000" rows="5" required disabled={saving}></textarea></label>
              <div class="actions wide"><button type="submit" disabled={saving||loading||!validDrillForm(form)}>{canPropose?'Υποβολή για κεντρικό έλεγχο':'Αποθήκευση πρόχειρου'}</button>
                {#if editing||source}<button type="button" disabled={saving} onclick={reset}>Ακύρωση επεξεργασίας</button>{/if}</div>
            </form>
          </section>
        {/if}
      {/if}
    </section>
  </AdminFrame>
{:else}<AdminSessionStatus unavailable={$auth.status==='unavailable'} busy={$auth.busy}
  error={$auth.error} onRetry={()=>void auth.bootstrap()} onLogout={()=>void auth.logout()}/>{/if}
<style>
  .library{display:grid;gap:1rem;max-width:1240px;margin:auto}
  h1{font-size:1.65rem;margin:0}h2{font-size:1.1rem;margin:.35rem 0}
  p{overflow-wrap:anywhere}.muted{color:#b9b9b9;font-size:.9rem;margin:0}
  .filters,.fields{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,180px),1fr));gap:.75rem;align-items:end}
  label{display:grid;gap:.35rem;font-size:.85rem;color:#ddd}
  input,select,button,textarea{box-sizing:border-box;min-width:0;min-height:42px;border:1px solid #464646;background:#1c1c1c;color:#fff;padding:.5rem .65rem;border-radius:6px;font:inherit}
  button{cursor:pointer;color:#f6bb38;border-color:#74602e}button[aria-pressed=true]{background:#f6bb38;color:#141414;border-color:#f6bb38}button:disabled{opacity:.5;cursor:not-allowed}
  button:focus-visible,input:focus-visible,select:focus-visible,textarea:focus-visible,summary:focus-visible{outline:2px solid #f6bb38;outline-offset:3px}
  .drill-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,280px),1fr));gap:.8rem}
  .drill,.editor{background:#151515;border:1px solid #383838;border-radius:8px;padding:1rem;min-width:0}
  .drill details p{white-space:pre-wrap}.metadata{font-size:.8rem;color:#f6bb38}.actions{display:flex;align-items:center;flex-wrap:wrap;gap:.5rem}
  summary{cursor:pointer;color:#f6bb38}.wide{grid-column:1/-1}
  [role=alert]{color:#ffb3b3}
</style>
