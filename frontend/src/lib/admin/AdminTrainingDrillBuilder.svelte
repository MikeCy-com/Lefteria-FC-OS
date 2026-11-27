<script lang="ts">
  import {onMount,getContext} from 'svelte';
  import {ADMIN_AUTH_CONTEXT,type AdminAuthController} from './controller.ts';
  import {validDrillPage,drillCategories,drillAges,type Drill} from './central-academy-drills.ts';
  import {validTrainingDrillPlan,validTrainingDrillSequence,plannedMinutes,snapshotDrill,drillSelections,
    matchingTrainingDrillSave,type TrainingDrillSnapshot} from './academy-training-drills.ts';
  import type {TrainingPlanDraft} from './academy-training-plans.ts';
  let {plan,actor,canWrite,onSaved,onClose}:{plan:TrainingPlanDraft;actor:string;canWrite:boolean;
    onSaved:()=>void;onClose:()=>void}=$props();
  const auth=getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  const scope=()=>actor+':'+plan.group_id+':'+plan.id+':'+plan.version;
  let mounted=false;
  let sequence=$state<TrainingDrillSnapshot[]>([]),catalogue=$state<Drill[]>([]);
  let loading=$state(true),catalogueLoading=$state(false),saving=$state(false),ready=$state(false);
  let error=$state(''),catalogueError=$state(''),search=$state(''),category=$state('all'),age=$state('any');
  let offset=$state(0),nextOffset=$state<number|null>(null);
  let planController:AbortController|null=null,catalogueController:AbortController|null=null;
  const categoryLabels:Record<string,string>={technical:'Τεχνική',tactical:'Τακτική',physical:'Φυσική κατάσταση',
    goalkeeping:'Τερματοφύλακες',warmup:'Προθέρμανση',game:'Παιχνίδι',recovery:'Αποκατάσταση',other:'Άλλο'};
  const total=$derived(plannedMinutes(sequence));
  const valid=$derived(validTrainingDrillSequence(sequence,plan.duration_minutes));
  const query=()=>new URLSearchParams({organization_id:'lefteria_fc_academy',group_id:plan.group_id});
  const base=()=>'/api/admin/academy/training/plans/'+encodeURIComponent(plan.id)+'/drills';
  function current(owner:string,signal?:AbortSignal){return mounted&&scope()===owner&&!signal?.aborted;}
  function rejectAccess(){ready=false;sequence=[];catalogue=[];void auth.bootstrap();}
  async function loadPlan(){
    planController?.abort();const controller=new AbortController();planController=controller;
    const owner=scope();loading=true;ready=false;error='';sequence=[];
    try{
      const params=query();params.set('version',String(plan.version));
      const response=await fetch(base()+'?'+params,{credentials:'same-origin',cache:'no-store',signal:controller.signal});
      if(!current(owner,controller.signal))return;
      if(response.status===401||response.status===403){rejectAccess();throw Error('Η πρόσβαση στο συγκεκριμένο τμήμα άλλαξε.');}
      if(response.status===409)throw Error('Η έκδοση άλλαξε. Κλείστε τον επεξεργαστή και ανανεώστε την προπόνηση.');
      if(!response.ok)throw Error('Δεν φορτώθηκαν οι ασκήσεις της προπόνησης.');
      const value:unknown=await response.json();
      if(!current(owner,controller.signal))return;
      if(!validTrainingDrillPlan(value,plan,plan.version))throw Error('Μη επαληθευμένο πλάνο ασκήσεων.');
      sequence=value.drills;ready=true;
      if(canWrite)void loadCatalogue(0);
    }catch(e){if(current(owner,controller.signal))error=e instanceof Error?e.message:'Αποτυχία φόρτωσης.';}
    finally{if(current(owner,controller.signal))loading=false;}
  }
  async function loadCatalogue(page:number){
    if(!mounted||!canWrite||!ready||saving)return;
    catalogueController?.abort();const controller=new AbortController();catalogueController=controller;
    const owner=scope();catalogueLoading=true;catalogue=[];nextOffset=null;catalogueError='';
    try{
      const params=new URLSearchParams({search:search.trim(),category,age,status:'approved',offset:String(page)});
      const response=await fetch('/api/admin/academy/drills?'+params,{credentials:'same-origin',cache:'no-store',signal:controller.signal});
      if(!current(owner,controller.signal))return;
      if(response.status===401||response.status===403){rejectAccess();throw Error('Η πρόσβαση στη βιβλιοθήκη άλλαξε.');}
      if(!response.ok)throw Error('Η κεντρική βιβλιοθήκη δεν είναι διαθέσιμη.');
      const value:unknown=await response.json();
      if(!current(owner,controller.signal))return;
      if(!validDrillPage(value,'approved',page))throw Error('Μη επαληθευμένη βιβλιοθήκη.');
      catalogue=value.drills;nextOffset=value.next_offset;offset=page;
    }catch(e){if(current(owner,controller.signal))catalogueError=e instanceof Error?e.message:'Αποτυχία φόρτωσης.';}
    finally{if(current(owner,controller.signal))catalogueLoading=false;}
  }
  function add(drill:Drill){
    if(!canWrite||!ready||saving||sequence.length>=24||sequence.some(r=>r.drill_id===drill.id))return;
    sequence=[...sequence,snapshotDrill(drill)];
  }
  function move(index:number,delta:number){
    if(!canWrite||saving||!ready)return;
    const target=index+delta;if(target<0||target>=sequence.length)return;
    const items=[...sequence];const item=items[index];if(!item)return;
    items.splice(index,1);items.splice(target,0,item);sequence=items;
  }
  function remove(id:string){if(canWrite&&ready&&!saving)sequence=sequence.filter(row=>row.drill_id!==id);}
  function minutes(id:string,value:number){
    if(canWrite&&ready&&!saving)sequence=sequence.map(row=>row.drill_id===id?{...row,duration_minutes:value}:row);
  }
  async function save(){
    if(!mounted||!canWrite||!ready||!valid||saving)return;
    const owner=scope();const submitted=sequence.map(row=>({...row}));saving=true;error='';
    try{
      const response=await fetch(base()+'?'+query(),{method:'PUT',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json',Accept:'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({expected_version:plan.version,drills:drillSelections(submitted)})});
      if(!current(owner)||!canWrite)return;
      if(response.status===401||response.status===403){rejectAccess();throw Error('Η εξουσιοδότηση άλλαξε.');}
      if(response.status===409){ready=false;throw Error('Άλλαξε η προπόνηση ή η έκδοση άσκησης. Κλείστε και ανανεώστε.');}
      if(!response.ok)throw Error('Η νέα έκδοση δεν επιβεβαιώθηκε.');
      const value:unknown=await response.json();
      if(!current(owner)||!canWrite)return;
      if(!matchingTrainingDrillSave(value,plan,submitted)){ready=false;throw Error('Η νέα έκδοση δεν επαληθεύτηκε. Κλείστε και ανανεώστε.');}
      ready=false;onSaved();
    }catch(e){if(current(owner)){ready=false;error=e instanceof Error?e.message:'Η αποθήκευση δεν επιβεβαιώθηκε.';}}
    finally{if(current(owner))saving=false;}
  }
  onMount(()=>{mounted=true;void loadPlan();return()=>{mounted=false;planController?.abort();catalogueController?.abort();};});
</script>

<section class="builder" aria-label="Σχεδιασμός ασκήσεων" data-testid="academy-training-drill-builder">
  <header><div><h3>Ασκήσεις προπόνησης</h3><p>{plan.title} · Έκδοση {plan.version} · {plan.duration_minutes}′</p></div>
    <button type="button" disabled={saving} onclick={onClose}>Κλείσιμο & ανανέωση</button></header>
  <p class="hint">Οι ασκήσεις προέρχονται από την κοινή βιβλιοθήκη όλων των πόλεων και τοποθεσιών. Αποθηκεύονται σε νέα ιδιωτική έκδοση της προπόνησης.</p>
  {#if error}<p class="error" role="alert">{error}</p>{/if}
  {#if loading}<p role="status">Φόρτωση επαληθευμένης έκδοσης…</p>
  {:else if ready}
    <div class="summary">
      <strong>{total}′ / {plan.duration_minutes}′</strong>
      <span>{sequence.length} ασκήσεις · {Math.max(0,plan.duration_minutes-total)}′ διαθέσιμα</span>
      <progress max={plan.duration_minutes} value={Math.min(plan.duration_minutes,total)} aria-label="Κατανομή διάρκειας προπόνησης"></progress>
    </div>
    {#if !valid}<p class="error" role="alert">Ελέγξτε τη διάρκεια κάθε άσκησης και το συνολικό χρόνο.</p>{/if}
    {#if !sequence.length}<p class="hint">Δεν έχουν προστεθεί ασκήσεις.</p>{/if}
    <ol class="sequence">
      {#each sequence as row,i (row.drill_id)}
        <li><div><strong>{i+1}. {row.title}</strong><small>{categoryLabels[row.category]} · {row.age_band.toUpperCase()} · έκδοση {row.drill_version}</small>
          <details><summary>Στόχος & οδηγίες</summary><p>{row.objective}</p><p>{row.instructions}</p></details></div>
          {#if canWrite}
            <label>Λεπτά<input type="number" min="1" max="120" value={row.duration_minutes}
              disabled={saving} oninput={e=>minutes(row.drill_id,e.currentTarget.valueAsNumber)}/></label>
            <div class="actions"><button type="button" aria-label={'Μετακίνηση πάνω: '+row.title} disabled={saving||i===0} onclick={()=>move(i,-1)}>↑</button>
              <button type="button" aria-label={'Μετακίνηση κάτω: '+row.title} disabled={saving||i===sequence.length-1} onclick={()=>move(i,1)}>↓</button>
              <button type="button" disabled={saving} onclick={()=>remove(row.drill_id)}>Αφαίρεση</button></div>
          {:else}<span>{row.duration_minutes}′</span>{/if}
        </li>
      {/each}
    </ol>
    {#if canWrite}
      <div class="picker">
        <h4>Επιλογή από την κεντρική βιβλιοθήκη</h4>
        <form class="filters" onsubmit={e=>{e.preventDefault();void loadCatalogue(0);}}>
          <label>Αναζήτηση<input bind:value={search} maxlength="100" disabled={saving}/></label>
          <label>Κατηγορία<select bind:value={category} disabled={saving}><option value="all">Όλες</option>
            {#each drillCategories as c}<option value={c}>{categoryLabels[c]}</option>{/each}</select></label>
          <label>Ηλικία<select bind:value={age} disabled={saving}><option value="any">Όλες</option>
            {#each drillAges as a}<option value={a}>{a==='all'?'Κάθε ηλικία':a.toUpperCase()}</option>{/each}</select></label>
          <button type="submit" disabled={saving}>Αναζήτηση</button>
        </form>
        {#if catalogueError}<p role="alert" class="error">{catalogueError}</p>
        {:else if catalogueLoading}<p role="status">Φόρτωση βιβλιοθήκης…</p>
        {:else if !catalogue.length}<p class="hint">Δεν βρέθηκαν εγκεκριμένες ασκήσεις.</p>{/if}
        <ul class="catalogue">{#each catalogue as drill (drill.id)}
          <li><div><strong>{drill.title}</strong><small>{drill.duration_minutes}′ · {drill.age_band.toUpperCase()} · {categoryLabels[drill.category]}</small></div>
            <button type="button" disabled={saving||sequence.length>=24||sequence.some(r=>r.drill_id===drill.id)}
              onclick={()=>add(drill)}>Προσθήκη</button></li>
        {/each}</ul>
        <nav class="actions" aria-label="Σελίδες κεντρικής βιβλιοθήκης">
          <button type="button" disabled={saving||catalogueLoading||offset===0} onclick={()=>void loadCatalogue(Math.max(0,offset-30))}>Προηγούμενη</button>
          <button type="button" disabled={saving||catalogueLoading||nextOffset===null} onclick={()=>{if(nextOffset!==null)void loadCatalogue(nextOffset);}}>Επόμενη</button>
        </nav>
      </div>
      <p class="hint">Η νέα έκδοση χρειάζεται πλήρη έλεγχο ασκήσεων πριν από τη δημοσίευση. Η δημοσίευση ασκήσεων δεν έχει ενεργοποιηθεί ακόμη.</p>
      <button type="button" class="save" disabled={saving||!valid} onclick={()=>void save()}>{saving?'Αποθήκευση…':'Αποθήκευση ιδιωτικής σειράς'}</button>
    {/if}
  {/if}
</section>

<style>
  .builder{margin:16px 0;border:1px solid #6b5327;background:#181818;border-radius:10px;padding:16px;display:grid;gap:12px}
  header,.summary,.actions{display:flex;align-items:center;flex-wrap:wrap;gap:10px;justify-content:space-between}
  h3,h4,p{margin:0}h3{font-size:1.05rem;color:#f6bb38}h4{font-size:.95rem}
  p,.hint,small{font-size:.8rem;color:#b9b9b9;line-height:1.5}small{display:block}
  .summary{border:1px solid #444;padding:10px;border-radius:6px;color:#f6bb38}
  progress{width:100%;height:8px;accent-color:#f6bb38}
  ol,ul{list-style:none;margin:0;padding:0;display:grid;gap:8px}
  li{display:flex;align-items:start;flex-wrap:wrap;gap:10px;justify-content:space-between;border:1px solid #424242;padding:10px;border-radius:6px}
  li>div:first-child{flex:1;min-width:min(100%,160px)}
  details{margin-top:6px}details p{white-space:pre-wrap;overflow-wrap:anywhere}summary{cursor:pointer;color:#f6bb38;font-size:.8rem}
  label{display:grid;gap:4px;font-size:.8rem;color:#ddd}input,select,button{min-height:42px;box-sizing:border-box;font:inherit;background:#222;color:#fff;border:1px solid #666;border-radius:6px;padding:7px}
  input[type=number]{width:76px}button{cursor:pointer;color:#f6bb38;border-color:#7c6231}button:disabled{opacity:.5;cursor:not-allowed}
  .filters{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,140px),1fr));gap:8px;align-items:end}
  .picker{display:grid;gap:10px}.save{background:#53401c;color:white}.error{color:#ffadad}
  button:focus-visible,input:focus-visible,select:focus-visible,summary:focus-visible{outline:2px solid #f6bb38;outline-offset:3px}
</style>
