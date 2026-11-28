<script lang="ts">
  import {getContext,onDestroy} from 'svelte';
  import {ADMIN_AUTH_CONTEXT,type AdminAuthController} from './controller.ts';
  import {hasAdminPermission} from './permissions.ts';
  import {trainingSeries,trainingSeriesWindows,seriesPlanStatus,type WeeklySlot} from './academy-training-series.ts';
  import {validTrainingPlanPage,matchingTrainingPlanWrite,type TrainingPlanForm,type TrainingPlanDraft} from './academy-training-plans.ts';

  let {groupId,actor,from,to,allowed,onSaved}:{
    groupId:string;actor:string;from:string;to:string;allowed:boolean;onSaved:()=>void;
  }=$props();
  const auth=getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  const owner='lefteria_fc_academy';
  const base='/api/admin/academy/training/plans/';
  const days=[{id:1,label:'Δευτέρα'},{id:2,label:'Τρίτη'},{id:3,label:'Τετάρτη'},{id:4,label:'Πέμπτη'},{id:5,label:'Παρασκευή'},{id:6,label:'Σάββατο'},{id:0,label:'Κυριακή'}];
  let today=$state(new Intl.DateTimeFormat('en-CA',{timeZone:'Asia/Nicosia',year:'numeric',month:'2-digit',day:'2-digit'}).format(new Date()));
  let start=$state(''),end=$state(''),title=$state('Προπόνηση'),location=$state(''),notes=$state('');
  let slots=$state(days.map(day=>({weekday:day.id,enabled:false,start_time:'17:00',duration_minutes:60})));
  let excludedDays=$state<string[]>([]),exceptionDay=$state('');
  let busy=$state(false),message=$state(''),error=$state(''),confirmed=$state(false);
  let preview=$state<{plan:TrainingPlanForm;status:'new'|'existing'|'conflict'}[]|null>(null);
  let previewKey=$state(''),created=$state(0),attempted=$state('');
  let controller:AbortController|null=null;
  const input=$derived({start,end,title,location,notes,slots:slots.filter(s=>s.enabled).map(({weekday,start_time,duration_minutes}):WeeklySlot=>({weekday,start_time,duration_minutes})),excludedDays});
  const allCandidates=$derived(start>=today?trainingSeries({...input,excludedDays:[]}):null);
  const canExclude=$derived(!!allCandidates?.some(plan=>plan.day===exceptionDay)&&!excludedDays.includes(exceptionDay));
  function addException(){
    if(busy||!permitted||!canExclude)return;
    excludedDays=[...excludedDays,exceptionDay].sort();exceptionDay='';
  }
  function removeException(day:string){if(!busy&&permitted)excludedDays=excludedDays.filter(value=>value!==day);}
  const candidates=$derived(start>=today?trainingSeries(input):null);
  const key=$derived(JSON.stringify(input));
  const pending=$derived(preview?.filter(row=>row.status==='new')??[]);
  const conflicts=$derived(preview?.filter(row=>row.status==='conflict')??[]);
  const existingCount=$derived(preview?.filter(row=>row.status==='existing').length??0);
  const permitted=$derived(allowed&&$auth.status==='authenticated'&&$auth.session?.user.id===actor&&
    ['manager','super_admin'].includes($auth.session.user.role)&&
    $auth.session.user.organizations.some(org=>org.id===owner)&&hasAdminPermission($auth.session.user.permissions,'training:write'));
  $effect(()=>{
    if(!start)start=from<today?today:from;
    if(!end)end=to<start?start:to;
  });
  $effect(()=>{
    // Any exception no longer matching the selected period or weekday is
    // removed by the operator, not silently ignored.
    if(previewKey&&key!==previewKey){preview=null;confirmed=false;previewKey='';}
  });
  onDestroy(()=>controller?.abort());

  async function check(){
    if(!permitted||busy||!candidates)return;
    const planned=candidates.map(row=>({...row})),scope=groupId,who=actor,requestedKey=key;
    const windows=trainingSeriesWindows(start,end);
    controller=new AbortController();const signal=controller.signal;
    busy=true;error='';message='';confirmed=false;preview=null;created=0;attempted='';
    try{
      const current:TrainingPlanDraft[]=[];
      for(const window of windows){
        if(signal.aborted||!permitted||scope!==groupId||who!==actor)return;
        const params=new URLSearchParams({organization_id:owner,group_id:scope,start:window.start,end:window.end});
        const response=await fetch(base+'?'+params,{credentials:'same-origin',cache:'no-store',signal});
        if(response.status===401||response.status===403){void auth.bootstrap();throw Error('Η πρόσβαση άλλαξε.');}
        if(!response.ok)throw Error('Δεν ήταν δυνατός ο έλεγχος του υπάρχοντος προγράμματος.');
        const raw:unknown=await response.json();
        if(!validTrainingPlanPage(raw,scope,window.start,window.end))throw Error('Μη επαληθευμένο πρόγραμμα.');
        current.push(...raw.plans);
      }
      if(!signal.aborted&&permitted&&scope===groupId&&who===actor&&key===requestedKey){
        preview=planned.map(plan=>({plan,status:seriesPlanStatus(plan,current)}));
        previewKey=requestedKey;
      }
    }catch(e){if(!signal.aborted)error=e instanceof Error?e.message:'Αποτυχία ελέγχου.';}
    finally{if(!signal.aborted)busy=false;controller=null;}
  }
  async function save(){
    if(!permitted||busy||!confirmed||!preview||previewKey!==key||!pending.length||conflicts.length)return;
    const planned=pending.map(row=>({...row.plan})),scope=groupId,who=actor;
    controller=new AbortController();const signal=controller.signal;
    busy=true;confirmed=false;error='';message='';created=0;attempted='';
    try{
      for(const plan of planned){
        if(signal.aborted||!permitted||scope!==groupId||who!==actor)return;
        attempted=plan.day+' '+plan.start_time;
        const response=await fetch(base+'?organization_id='+owner,{
          method:'POST',credentials:'same-origin',cache:'no-store',signal,
          headers:{'Content-Type':'application/json','Accept':'application/json','X-Lefteria-Admin':'1'},
          body:JSON.stringify({group_id:scope,...plan,expected_version:0,archive:false})
        });
        if(response.status===401||response.status===403){void auth.bootstrap();throw Error('Η πρόσβαση άλλαξε.');}
        if(response.status===409)throw Error('Σύγκρουση προγράμματος. Δεν συνεχίστηκε η σειρά.');
        if(!response.ok)throw Error('Δεν επιβεβαιώθηκε η καταχώριση.');
        const raw:unknown=await response.json();
        if(!matchingTrainingPlanWrite(raw,scope,undefined,1,'draft')||
          raw.day!==plan.day||raw.start_time!==plan.start_time||raw.duration_minutes!==plan.duration_minutes||
          raw.title!==plan.title||raw.location!==plan.location||raw.notes!==plan.notes)
          throw Error('Μη επαληθευμένη καταχώριση.');
        if(signal.aborted||!permitted||scope!==groupId||who!==actor)return;
        created++;attempted='';
      }
      message='Αποθηκεύτηκαν '+created+' ιδιωτικά πρόχειρα. Δεν δημοσιεύτηκαν.';
    }catch(e){
      if(!signal.aborted)error=created+' επιβεβαιωμένες αποθηκεύσεις. '+(attempted?'Ελέγξτε την καταχώριση '+attempted+'. ':'')+(e instanceof Error?e.message:'Αποτυχία.')+' Ελέγξτε ξανά το πρόγραμμα πριν επαναλάβετε.';
    }finally{
      if(!signal.aborted){busy=false;preview=null;previewKey='';if(created>0||attempted)onSaved();}
      controller=null;
    }
  }
</script>

{#if permitted}
<details class="admin-panel training-series" data-testid="admin-training-series">
  <summary>Μαζική δημιουργία προπονήσεων · εβδομαδιαίο πρόγραμμα περιόδου</summary>
  <p class="admin-muted">Επιλέξτε ημερομηνίες και ώρες για κάθε ημέρα. Δημιουργούνται μόνο ιδιωτικά πρόχειρα. Η έγκριση και δημοσίευση γίνονται από τις υπάρχουσες διαδικασίες.</p>
  <fieldset disabled={busy}>
    <div class="series-grid">
      <label>Από <input type="date" min={today} bind:value={start}/></label>
      <label>Έως <input type="date" min={start} bind:value={end}/></label>
      <label>Τίτλος <input bind:value={title} maxlength="180"/></label>
      <label>Τοποθεσία <input bind:value={location} maxlength="200"/></label>
      <label class="series-wide">Σημειώσεις <textarea bind:value={notes} maxlength="2000" rows="2"></textarea></label>
    </div>
    <div class="series-slots">
      {#each slots as slot,i (slot.weekday)}
        <div>
          <label><input type="checkbox" bind:checked={slot.enabled}/> {days[i].label}</label>
          {#if slot.enabled}
            <label>Ώρα <input type="time" bind:value={slot.start_time}/></label>
            <label>Λεπτά <input type="number" min="20" max="240" bind:value={slot.duration_minutes}/></label>
          {/if}
        </div>
      {/each}
    </div>
    <div class="series-exceptions" data-testid="training-series-exceptions">
      <h4>Ημερομηνίες χωρίς προπόνηση</h4>
      <p class="admin-muted">Εξαιρέστε αργίες, τουρνουά ή ημέρες ανάπαυσης πριν από τη δημιουργία. Δεν ακυρώνονται υπάρχουσες προπονήσεις.</p>
      <div class="series-exception-picker">
        <label>Ημερομηνία εξαίρεσης <input type="date" min={start} max={end} bind:value={exceptionDay}/></label>
        <button type="button" class="admin-secondary" disabled={!canExclude||busy} onclick={addException}>Προσθήκη εξαίρεσης</button>
      </div>
      {#if excludedDays.length}
        <ul class="series-exception-list">{#each excludedDays as day (day)}
          <li><span>{day}</span><button type="button" class="admin-secondary" onclick={()=>removeException(day)} aria-label={'Αφαίρεση εξαίρεσης '+day}>Αφαίρεση</button></li>
        {/each}</ul>
        <p role="status">{excludedDays.length} εξαιρέσεις · {candidates?.length??0} προγραμματισμένες προπονήσεις</p>
        {#if !candidates}<p role="alert">Ελέγξτε τις εξαιρέσεις μετά την αλλαγή ημερομηνιών ή ημερών. Απαιτείται τουλάχιστον μία προπόνηση.</p>{/if}
      {/if}
    </div>
    <button type="button" class="admin-secondary" disabled={!candidates||busy} onclick={()=>void check()}>Έλεγχος & προεπισκόπηση {candidates?.length??0} προπονήσεων</button>
    {#if !candidates}<p class="admin-muted">Επιλέξτε έγκυρες ημερομηνίες και ημέρες (έως 180 προπονήσεις σε 366 ημέρες).</p>{/if}
  </fieldset>
  {#if busy}<p role="status">Επεξεργασία… {created} επιβεβαιωμένες αποθηκεύσεις.</p>{/if}
  {#if error}<p role="alert">{error}</p>{/if}
  {#if message}<p role="status">{message}</p>{/if}
  {#if preview&&previewKey===key}
    <p>{pending.length} νέες · {existingCount} υπάρχουν ήδη · {conflicts.length} συγκρούσεις</p>
    <details><summary>Αναλυτικές ημερομηνίες</summary>
      <ul class="series-preview">{#each preview as row (row.plan.day)}
        <li>{row.plan.day} · {row.plan.start_time} · {row.plan.duration_minutes}′ — {row.status==='new'?'Νέα':row.status==='existing'?'Υπάρχει':'Σύγκρουση'}</li>
      {/each}</ul>
    </details>
    {#if conflicts.length}<p role="alert">Αλλάξτε τις ώρες ή τις ημερομηνίες που συγκρούονται και κάντε νέο έλεγχο.</p>{/if}
    {#if pending.length&&!conflicts.length}
      <label><input type="checkbox" bind:checked={confirmed} disabled={busy}/> Επιβεβαιώνω τη δημιουργία {pending.length} ιδιωτικών πρόχειρων.</label>
      <button type="button" class="admin-primary" disabled={!confirmed||busy} onclick={()=>void save()}>Δημιουργία {pending.length} προπονήσεων</button>
      <p class="admin-muted">Οι καταχωρίσεις γίνονται διαδοχικά. Σε σφάλμα σταματούν και εμφανίζεται ο αριθμός επιβεβαιωμένων αποθηκεύσεων.</p>
    {/if}
  {/if}
</details>
{/if}
<style>
.training-series{display:grid;gap:12px;margin:12px 0}
.training-series summary{cursor:pointer;font-weight:700;min-height:44px}
.training-series fieldset{border:0;min-width:0;padding:0;display:grid;gap:12px}
.series-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,210px),1fr));gap:10px}
.series-grid label,.series-slots label{display:grid;gap:5px}
.series-wide{grid-column:1/-1}
.series-slots{display:grid;gap:6px}
.series-slots>div{display:flex;flex-wrap:wrap;align-items:end;gap:12px;padding:8px;border:1px solid var(--admin-line);border-radius:8px}
.series-slots>div>label:first-child{display:flex;align-items:center;min-width:120px}
.series-preview{max-height:300px;overflow:auto}
.series-exceptions{display:grid;gap:8px;padding:12px;border:1px solid var(--admin-line);border-radius:8px}
.series-exceptions h4,.series-exceptions p{margin:0}
.series-exception-picker,.series-exception-list li{display:flex;align-items:end;gap:10px;flex-wrap:wrap}
.series-exception-picker label{display:grid;gap:5px}
.series-exception-list{display:grid;gap:6px;list-style:none;padding:0;margin:0;max-height:200px;overflow:auto}
.series-exception-list li{justify-content:space-between;align-items:center}
</style>
