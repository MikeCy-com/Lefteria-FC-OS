<script lang="ts">
  import {getContext,untrack} from 'svelte';
  import {browser} from '$app/env';
  import {goto} from '$app/navigation';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';
  import {ADMIN_AUTH_CONTEXT,type AdminAuthController} from '../../../lib/admin/controller.ts';
  import {hasAdminPermission} from '../../../lib/admin/permissions.ts';
  import {drillCategories,drillAges,validDrillForm,validDrill,validDrillPage,canReadDrillLibrary,
    type Drill,type DrillForm} from '../../../lib/admin/central-academy-drills.ts';
  const auth=getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  const labels:Record<string,string>={technical:'Τεχνική',tactical:'Τακτική',physical:'Φυσική κατάσταση',
    goalkeeping:'Τερματοφύλακες',warmup:'Προθέρμανση',game:'Παιχνίδι',recovery:'Αποκατάσταση',other:'Άλλο',
    draft:'Πρόχειρο',approved:'Εγκεκριμένο',archived:'Αρχείο'};
  const blank=():DrillForm=>({title:'',category:'technical',age_band:'all',duration_minutes:15,objective:'',instructions:''});
  const allowed=$derived($auth.status==='authenticated'&&!!$auth.session&&
    canReadDrillLibrary($auth.session.user.role,$auth.session.user.organizations)&&
    (hasAdminPermission($auth.session.user.permissions,'training:read')||
      hasAdminPermission($auth.session.user.permissions,'training:read_assigned')));
  const canWrite=$derived(allowed&&$auth.session?.user.role==='super_admin'&&
    hasAdminPermission($auth.session.user.permissions,'training:write'));
  const scope=$derived(allowed?$auth.session?.user.id+':'+$auth.session?.user.role:'');
  let search=$state(''),appliedSearch=$state(''),category=$state('all'),age=$state('any'),status=$state('approved');
  let offset=$state(0),nextOffset=$state<number|null>(null),refresh=$state(0);
  let rows=$state<Drill[]>([]),loading=$state(false),saving=$state(false),loaded=$state(false);
  let error=$state(''),writeError=$state(''),notice=$state('');
  let form=$state<DrillForm>(blank()),editing=$state<Drill|null>(null);
  const base='/api/admin/academy/drills/';
  $effect(()=>{if(browser&&$auth.status==='anonymous')void goto('/admin/login',{replace:true});});
  $effect(()=>{
    void scope;
    untrack(()=>{search='';appliedSearch='';category='all';age='any';status='approved';offset=0;
      form=blank();editing=null;notice='';writeError='';saving=false;});
  });
  $effect(()=>{
    const owner=scope,s=appliedSearch,c=category,a=age,st=canWrite?status:'approved',o=offset;
    void refresh;
    rows=[];nextOffset=null;loaded=false;error='';loading=false;
    if(!browser||!owner)return;
    const controller=new AbortController();
    loading=true;
    untrack(()=>{void(async()=>{
      try{
        const query=new URLSearchParams({search:s,category:c,age:a,status:st,offset:String(o)});
        const response=await fetch(base+'?'+query,{credentials:'same-origin',cache:'no-store',
          headers:{Accept:'application/json'},signal:controller.signal});
        if(controller.signal.aborted||scope!==owner)return;
        if(response.status===401||response.status===403){void auth.bootstrap();throw Error('Η πρόσβαση άλλαξε.');}
        if(!response.ok)throw Error('Η βιβλιοθήκη δεν είναι διαθέσιμη.');
        const value:unknown=await response.json();
        if(controller.signal.aborted||scope!==owner)return;
        if(!validDrillPage(value,st,o))throw Error('Μη επαληθευμένη απόκριση βιβλιοθήκης.');
        rows=value.drills;nextOffset=value.next_offset;loaded=true;
      }catch(e){if(!controller.signal.aborted&&scope===owner)error=e instanceof Error?e.message:'Αποτυχία φόρτωσης.';}
      finally{if(!controller.signal.aborted&&scope===owner)loading=false;}
    })();});
    return()=>controller.abort();
  });
  function searchNow(){appliedSearch=search.trim();offset=0;refresh++;}
  function edit(row:Drill){
    if(!canWrite||saving||loading||row.status==='archived')return;
    editing=row;form={title:row.title,category:row.category,age_band:row.age_band,
      duration_minutes:row.duration_minutes,objective:row.objective,instructions:row.instructions};
    notice='';writeError='';
  }
  function reset(){editing=null;form=blank();}
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
        {#if loading}<p role="status">Φόρτωση βιβλιοθήκης…</p>
        {:else if error}<p role="alert">{error}</p><button type="button" disabled={saving} onclick={()=>refresh++}>Δοκιμή ξανά</button>
        {:else if loaded&&!rows.length}<p>Δεν βρέθηκαν ασκήσεις.</p>
        {:else if loaded}
          <div class="drill-grid">
            {#each rows as row (row.id)}
              <article class="drill">
                <div class="metadata">{labels[row.category]} · {row.age_band.toUpperCase()} · {row.duration_minutes}′</div>
                <h2>{row.title}</h2>
                <p>{row.objective}</p>
                <details><summary>Οδηγίες άσκησης</summary><p>{row.instructions}</p></details>
                {#if canWrite}
                  <p class="muted">{labels[row.status]} · Έκδοση {row.version}</p>
                  <div class="actions">
                    {#if row.status!=='archived'}
                      <button type="button" disabled={saving} onclick={()=>edit(row)}>Επεξεργασία</button>
                      {#if row.status==='draft'}<button type="button" disabled={saving} onclick={()=>void write('approve',row)}>Έγκριση έκδοσης</button>{/if}
                      <button type="button" disabled={saving} onclick={()=>void write('archive',row)}>Αρχειοθέτηση</button>
                    {:else}<button type="button" disabled={saving} onclick={()=>void write('restore',row)}>Επαναφορά ως πρόχειρο</button>{/if}
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
        {#if canWrite}
          <section class="editor" aria-labelledby="editor-title">
            <h2 id="editor-title">{editing?'Επεξεργασία · έκδοση '+editing.version:'Νέα άσκηση'}</h2>
            <p class="muted">Η αποθήκευση δημιουργεί πρόχειρη έκδοση. Η επεξεργασία εγκεκριμένης άσκησης απαιτεί νέα έγκριση.</p>
            {#if notice}<p role="status">{notice}</p>{/if}
            {#if writeError}<p role="alert">{writeError}</p>{/if}
            <form class="fields" onsubmit={e=>{e.preventDefault();void write(editing?'edit':'create',editing);}}>
              <label>Τίτλος<input bind:value={form.title} maxlength="180" required disabled={saving}/></label>
              <label>Κατηγορία<select bind:value={form.category} disabled={saving}>{#each drillCategories as c}<option value={c}>{labels[c]}</option>{/each}</select></label>
              <label>Ηλικία<select bind:value={form.age_band} disabled={saving}>{#each drillAges as a}<option value={a}>{a==='all'?'Κάθε ηλικία':a.toUpperCase()}</option>{/each}</select></label>
              <label>Διάρκεια (λεπτά)<input type="number" bind:value={form.duration_minutes} min="1" max="120" required disabled={saving}/></label>
              <label class="wide">Στόχος<input bind:value={form.objective} maxlength="1000" required disabled={saving}/></label>
              <label class="wide">Οδηγίες<input bind:value={form.instructions} maxlength="4000" required disabled={saving}/></label>
              <div class="actions wide"><button type="submit" disabled={saving||loading||!validDrillForm(form)}>Αποθήκευση πρόχειρου</button>
                {#if editing}<button type="button" disabled={saving} onclick={reset}>Ακύρωση επεξεργασίας</button>{/if}</div>
            </form>
          </section>
        {/if}
      {/if}
    </section>
  </AdminFrame>
{:else}<AdminSessionStatus status={$auth.status}/>{/if}
<style>
  .library{display:grid;gap:1rem;max-width:1240px;margin:auto}
  h1{font-size:1.65rem;margin:0}h2{font-size:1.1rem;margin:.35rem 0}
  p{overflow-wrap:anywhere}.muted{color:#b9b9b9;font-size:.9rem;margin:0}
  .filters,.fields{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,180px),1fr));gap:.75rem;align-items:end}
  label{display:grid;gap:.35rem;font-size:.85rem;color:#ddd}
  input,select,button{box-sizing:border-box;min-width:0;min-height:42px;border:1px solid #464646;background:#1c1c1c;color:#fff;padding:.5rem .65rem;border-radius:6px;font:inherit}
  button{cursor:pointer;color:#f6bb38;border-color:#74602e}button:disabled{opacity:.5;cursor:not-allowed}
  button:focus-visible,input:focus-visible,select:focus-visible,summary:focus-visible{outline:2px solid #f6bb38;outline-offset:3px}
  .drill-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,280px),1fr));gap:.8rem}
  .drill,.editor{background:#151515;border:1px solid #383838;border-radius:8px;padding:1rem;min-width:0}
  .metadata{font-size:.8rem;color:#f6bb38}.actions{display:flex;align-items:center;flex-wrap:wrap;gap:.5rem}
  summary{cursor:pointer;color:#f6bb38}.wide{grid-column:1/-1}
  [role=alert]{color:#ffb3b3}
</style>
