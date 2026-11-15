<script lang="ts">
  import {browser} from '$app/env';
  import {buildPlayerDevelopmentTimeline} from './player-development-timeline.ts';

  type Owner='lefteria_fc'|'lefteria_fc_academy';
  type Category='general'|'physical'|'technical'|'tactical'|'mental'|'social';
  type GoalEvent={
    version:number;event:string;progress:number;status:string;
    note:string;recorded_at:string
  };
  type Goal={
    id:string;title:string;description:string;category:Category;
    target_date:string|null;source_evaluation_id:string|null;
    progress:number;status:'active'|'completed'|'archived';version:number;
    created_at:string;updated_at:string;
    history:GoalEvent[];history_count:number
  };
  const categories:{id:Category;label:string}[]=[
    {id:'general',label:'Γενικό'},
    {id:'physical',label:'Φυσική κατάσταση'},
    {id:'technical',label:'Τεχνική'},
    {id:'tactical',label:'Τακτική'},
    {id:'mental',label:'Ψυχολογία'},
    {id:'social',label:'Ομαδικότητα'}
  ];
  const steps=Array.from({length:11},(_,i)=>i*10);
  const timeline=$derived(buildPlayerDevelopmentTimeline(assessments,goals));

  let {owner,playerId,playerName,actorId,assessments=[],onExpired,onRevoked}:{
    owner:Owner;playerId:string;playerName:string;actorId:string;
    assessments?:{id:string;summary:string|null;created_at:string}[];
    onExpired:()=>void;onRevoked:()=>void
  }=$props();
  let goals=$state<Goal[]>([]),loading=$state(false),saving=$state(false);
  let error=$state(''),notice=$state(''),refresh=$state(0);
  let showCreate=$state(false),title=$state(''),description=$state('');
  let category=$state<Category>('technical'),targetDate=$state('');
  let sourceEvaluationId=$state('');
  let createRequest=$state('');
  let editing=$state<string|null>(null),editProgress=$state(0);
  let editNote=$state(''),archive=$state(false),editRequest=$state('');

  const isObject=(x:unknown):x is Record<string,unknown> =>
    !!x&&typeof x==='object'&&!Array.isArray(x);
  const isUuid=(x:unknown)=>typeof x==='string'&&
    /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(x);
  const validId=(x:string)=>x.length>0&&x.length<=128&&!/[\x00-\x1f\x7f]/.test(x);
  function validPage(raw:unknown,expectedOwner:Owner,expectedPlayer:string):raw is {
    organization_id:Owner;player_id:string;total:number;goals:Goal[]
  }{
    if(!isObject(raw)||raw.organization_id!==expectedOwner||
      raw.player_id!==expectedPlayer||!Array.isArray(raw.goals)||
      !Number.isInteger(raw.total)||raw.total!==raw.goals.length||
      raw.goals.length>100)return false;
    return raw.goals.every((item:unknown)=>{
      if(!isObject(item)||!isUuid(item.id)||typeof item.title!=='string'||
        item.title.length>200||typeof item.description!=='string'||
        item.description.length>2000||!categories.some(c=>c.id===item.category)||
        !['active','completed','archived'].includes(String(item.status))||
        typeof item.progress!=='number'||!steps.includes(item.progress)||
        typeof item.version!=='number'||!Number.isInteger(item.version)||item.version<1||item.version>10000||
        typeof item.created_at!=='string'||typeof item.updated_at!=='string'||
        !(item.target_date===null||typeof item.target_date==='string')||
        !(item.source_evaluation_id===null||isUuid(item.source_evaluation_id))||
        !Array.isArray(item.history)||item.history.length>50||
        typeof item.history_count!=='number'||!Number.isInteger(item.history_count)||
        item.history_count<item.history.length)return false;
      return item.history.every((event:unknown)=>isObject(event)&&
        typeof event.version==='number'&&Number.isInteger(event.version)&&event.version>=1&&
        typeof event.event==='string'&&typeof event.recorded_at==='string'&&
        typeof event.note==='string'&&event.note.length<=500&&
        typeof event.progress==='number'&&steps.includes(event.progress));
    });
  }
  const path=()=>'/api/admin/club/players/'+encodeURIComponent(playerId)+
    '/development-goals/';
  const scope=()=>new URLSearchParams({organization_id:owner}).toString();
  function resetCreateRequest(){createRequest='';error='';notice='';}
  function resetEditRequest(){editRequest='';error='';notice='';}

  $effect(()=>{
    const expectedOwner=owner,expectedPlayer=playerId,expectedActor=actorId;
    void refresh;
    goals=[];error='';notice='';editing=null;loading=false;
    if(!browser||!expectedActor||!validId(expectedPlayer))return;
    const controller=new AbortController();
    loading=true;
    void(async()=>{
      try{
        const response=await fetch(path()+'?'+scope(),{
          credentials:'same-origin',cache:'no-store',
          headers:{Accept:'application/json'},signal:controller.signal
        });
        if(controller.signal.aborted)return;
        if(response.status===401){onExpired();return;}
        if(response.status===403){onRevoked();return;}
        if(!response.ok)throw Error('Οι στόχοι ανάπτυξης δεν είναι διαθέσιμοι.');
        const raw:unknown=await response.json();
        if(!validPage(raw,expectedOwner,expectedPlayer))
          throw Error('Μη επαληθευμένη απάντηση ανάπτυξης αθλητή.');
        if(!controller.signal.aborted&&owner===expectedOwner&&
          playerId===expectedPlayer&&actorId===expectedActor)
          goals=raw.goals;
      }catch{
        if(!controller.signal.aborted)error='Αποτυχία ανάκτησης ατομικών στόχων.';
      }finally{if(!controller.signal.aborted)loading=false;}
    })();
    return()=>controller.abort();
  });

  async function createGoal(){
    if(!browser||saving||loading||title!==title.trim()||
      title.length<1||title.length>200||description.length>2000)return;
    if(!createRequest)createRequest=crypto.randomUUID();
    const expectedOwner=owner,expectedPlayer=playerId,expectedActor=actorId;
    const requestId=createRequest;
    saving=true;error='';notice='';
    try{
      const response=await fetch(path()+'?'+scope(),{
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({
          request_id:requestId,title,description,category,
          target_date:targetDate||null,source_evaluation_id:sourceEvaluationId||null
        })
      });
      if(expectedOwner!==owner||expectedPlayer!==playerId||expectedActor!==actorId)return;
      if(response.status===401){onExpired();return;}
      if(response.status===403){onRevoked();return;}
      if(!response.ok)throw Error(response.status===409?
        'Η καταχώριση άλλαξε. Ελέγξτε τα στοιχεία.':'Ο στόχος δεν αποθηκεύτηκε.');
      const result:unknown=await response.json();
      if(!isObject(result)||!isUuid(result.id)||
        result.organization_id!==expectedOwner||result.player_id!==expectedPlayer)
        throw Error('Μη επαληθευμένη αποθήκευση στόχου.');
      title='';description='';category='technical';targetDate='';sourceEvaluationId='';
      createRequest='';showCreate=false;refresh++;
    }catch(e){error=e instanceof Error?e.message:'Σφάλμα αποθήκευσης στόχου.';}
    finally{saving=false;}
  }

  function edit(goal:Goal){
    if(goal.status==='archived'||saving)return;
    editing=goal.id;editProgress=goal.progress;editNote='';
    archive=false;editRequest='';error='';notice='';
  }
  async function updateGoal(goal:Goal){
    if(!browser||saving||loading||editing!==goal.id||
      !steps.includes(editProgress)||editNote.length>500)return;
    if(!editRequest)editRequest=crypto.randomUUID();
    const expectedOwner=owner,expectedPlayer=playerId,expectedActor=actorId;
    const requestId=editRequest;
    saving=true;error='';notice='';
    try{
      const response=await fetch(path()+encodeURIComponent(goal.id)+'?'+scope(),{
        method:'PATCH',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({request_id:requestId,expected_version:goal.version,
          progress:editProgress,archive,note:editNote})
      });
      if(expectedOwner!==owner||expectedPlayer!==playerId||expectedActor!==actorId)return;
      if(response.status===401){onExpired();return;}
      if(response.status===403){onRevoked();return;}
      if(!response.ok)throw Error(response.status===409?
        'Ο στόχος άλλαξε από άλλο διαχειριστή. Ανανεώστε πριν αποθηκεύσετε.':
        'Η πρόοδος δεν αποθηκεύτηκε.');
      const result:unknown=await response.json();
      if(!isObject(result)||result.id!==goal.id||
        result.organization_id!==expectedOwner||result.player_id!==expectedPlayer||
        !Number.isInteger(result.version))
        throw Error('Μη επαληθευμένη ενημέρωση στόχου.');
      editing=null;editRequest='';refresh++;
    }catch(e){error=e instanceof Error?e.message:'Σφάλμα ενημέρωσης.';}
    finally{saving=false;}
  }
</script>

<section class="development-goals" data-testid="admin-player-development-goals"
  aria-label={'Ατομικοί στόχοι '+playerName}>
  <div class="goals-heading">
    <h4>Ατομικοί στόχοι ανάπτυξης ({goals.length})</h4>
    <button type="button" class="admin-secondary" disabled={loading||saving}
      onclick={()=>showCreate=!showCreate}>
      {showCreate?'Κλείσιμο':'Νέος στόχος'}
    </button>
  </div>
  <p class="admin-muted">Συνδέονται με τον μόνιμο φάκελο του αθλητή, όχι με μία μόνο σεζόν.
    Οι ολοκληρωμένοι στόχοι παραμένουν στο ιστορικό.</p>
  {#if !loading&&!error}
    <details class="development-timeline" data-testid="admin-player-development-timeline">
      <summary>Χρονολόγιο αξιολογήσεων και στόχων ({timeline.loadedCount})</summary>
      <p class="admin-muted">Ιδιωτικό χρονολόγιο: μόνο καταχωρισμένες αξιολογήσεις και
        αλλαγές στόχων. Δεν περιλαμβάνει ακόμη παρουσίες, αγώνες ή βίντεο.</p>
      {#if timeline.events.length===0}
        <p>Δεν υπάρχουν ακόμη καταχωρίσεις με επαληθευμένη ημερομηνία.</p>
      {:else}
        <ol class="timeline-entries">
          {#each timeline.events as entry (entry.key)}
            <li>
              <time datetime={entry.at}>{new Date(entry.at).toLocaleString('el-CY')}</time>
              <a href={'#'+entry.targetId}>{entry.title}</a>
              <span>{entry.kind==='assessment'?'Αξιολόγηση':'Στόχος'} · {entry.description}</span>
            </li>
          {/each}
        </ol>
      {/if}
      {#if timeline.hiddenOlderCount>0}
        <p class="admin-muted">Προβάλλονται οι 40 πιο πρόσφατες από {timeline.loadedCount}
          καταχωρίσεις. Οι υπόλοιπες είναι διαθέσιμες στους αντίστοιχους φακέλους.</p>
      {/if}
      {#if timeline.unreturnedGoalEvents>0}
        <p class="admin-muted">Άλλες {timeline.unreturnedGoalEvents}
          παλαιότερες αλλαγές στόχων υπάρχουν στο διατηρημένο ιστορικό,
          αλλά δεν έχουν φορτωθεί σε αυτή την προβολή.</p>
      {/if}
    </details>
  {/if}
  {#if showCreate}
    <form class="goal-form" onsubmit={e=>{e.preventDefault();void createGoal();}}>
      <label>Τίτλος στόχου
        <input required maxlength="200" bind:value={title} oninput={resetCreateRequest}/>
      </label>
      <label>Κατηγορία
        <select bind:value={category} onchange={resetCreateRequest}>
          {#each categories as option}
            <option value={option.id}>{option.label}</option>
          {/each}
        </select>
      </label>
      <label>Περιγραφή
        <textarea maxlength="2000" rows="3" bind:value={description}
          oninput={resetCreateRequest}></textarea>
      </label>
      <label>Σύνδεση με αξιολόγηση (προαιρετικά)
        <select bind:value={sourceEvaluationId} onchange={resetCreateRequest}>
          <option value="">Χωρίς αξιολόγηση</option>
          {#each assessments as assessment (assessment.id)}
            <option value={assessment.id}>{assessment.summary??"Αξιολόγηση"} · {new Date(assessment.created_at).toLocaleDateString("el-CY")}</option>
          {/each}
        </select>
      </label>
      <label>Ημερομηνία-στόχος (προαιρετικά)
        <input type="date" bind:value={targetDate} onchange={resetCreateRequest}/>
      </label>
      <button type="submit" class="admin-secondary" disabled={saving}>
        {saving?'Αποθήκευση…':'Δημιουργία στόχου'}
      </button>
    </form>
  {/if}
  {#if loading}<p role="status">Φόρτωση στόχων…</p>{/if}
  {#if error}<p role="alert">{error}</p>{/if}
  {#if notice}<p role="status">{notice}</p>{/if}
  {#if !loading&&!error&&goals.length===0}<p>Δεν υπάρχουν ακόμη στόχοι.</p>{/if}
  {#each goals as goal (goal.id)}
    <article class="goal-row" id={'development-goal-'+goal.id}>
      <div class="goal-title">
        <strong>{goal.title}</strong>
        <span>{categories.find(c=>c.id===goal.category)?.label} ·
          {goal.status==='completed'?'Ολοκληρώθηκε':
            goal.status==='archived'?'Αρχειοθετήθηκε':'Σε εξέλιξη'}</span>
      </div>
      {#if goal.description}<p>{goal.description}</p>{/if}
      <label>Πρόοδος: {goal.progress}%
        <progress value={goal.progress} max="100"></progress>
      </label>
      {#if goal.target_date}<p>Στόχος έως: {goal.target_date}</p>{/if}
      <details>
        <summary>Ιστορικό αλλαγών ({goal.history_count})</summary>
        {#each goal.history as item (item.version)}
          <p>{new Date(item.recorded_at).toLocaleDateString('el-CY')} ·
            {item.progress}% · {item.event}
            {#if item.note} · {item.note}{/if}
          </p>
        {/each}
        {#if goal.history_count>50}
          <p class="admin-muted">Προβάλλονται οι 50 πιο πρόσφατες αλλαγές. Το πλήρες ιστορικό διατηρείται.</p>
        {/if}
      </details>
      {#if goal.status!=='archived'}
        <button type="button" class="admin-secondary" disabled={saving}
          onclick={()=>editing===goal.id?editing=null:edit(goal)}>
          {editing===goal.id?'Ακύρωση':'Ενημέρωση προόδου'}
        </button>
      {/if}
      {#if editing===goal.id}
        <form class="goal-form" onsubmit={e=>{e.preventDefault();void updateGoal(goal);}}>
          <label>Πρόοδος
            <select bind:value={editProgress} onchange={resetEditRequest}>
              {#each steps as step}<option value={step}>{step}%</option>{/each}
            </select>
          </label>
          <label>Σημείωση για την αλλαγή
            <textarea maxlength="500" rows="2" bind:value={editNote}
              oninput={resetEditRequest}></textarea>
          </label>
          <label class="archive-choice">
            <input type="checkbox" bind:checked={archive} onchange={resetEditRequest}/>
            Αρχειοθέτηση στόχου (οριστική από αυτή την οθόνη)
          </label>
          <button type="submit" class="admin-secondary" disabled={saving}>
            {saving?'Αποθήκευση…':'Καταχώριση προόδου'}
          </button>
        </form>
      {/if}
    </article>
  {/each}
</section>
<style>
  .development-goals{display:grid;gap:12px;border:1px solid #66512e;border-radius:10px;padding:14px;background:#151819}
  .development-timeline{border:1px solid #504634;border-radius:8px;padding:10px 12px}
  .development-timeline summary{cursor:pointer;min-height:28px;font-weight:750;color:#f6c95f}
  .timeline-entries{display:grid;gap:12px;list-style:none;padding:0;margin:12px 0}
  .timeline-entries li{display:grid;gap:3px;padding:9px 10px;border-left:3px solid #ba9b52;background:#1f2424}
  .timeline-entries time{font-size:12px;color:#c9bc9e}
  .timeline-entries a{color:#f2c365;text-decoration:underline;text-underline-offset:3px;overflow-wrap:anywhere}
  .timeline-entries a:focus-visible{outline:3px solid #ffcc45;outline-offset:2px}
  .timeline-entries span{font-size:13px;overflow-wrap:anywhere}
  .goals-heading{display:flex;flex-wrap:wrap;align-items:center;justify-content:space-between;gap:10px}
  .goals-heading h4{margin:0;color:#ffca46}
  .goal-form{display:grid;gap:10px;border:1px solid #49453d;border-radius:8px;padding:12px}
  .goal-form label,.goal-row label{display:grid;gap:5px;font-size:13px}
  .goal-form input,.goal-form select,.goal-form textarea{min-height:40px;border:1px solid #756445;border-radius:6px;padding:8px;color:#fff;background:#151819}
  .goal-form textarea{min-height:66px}
  .archive-choice{display:flex!important;align-items:center}
  .archive-choice input{min-height:unset}
  .goal-row{display:grid;gap:8px;padding:12px;border:1px solid #4b463b;border-radius:8px}
  .goal-row p,.goal-row span{font-size:13px;overflow-wrap:anywhere}
  .goal-title{display:flex;flex-wrap:wrap;align-items:center;gap:8px}
  .goal-row progress{width:100%;height:10px;accent-color:#f5bd43}
  .goal-row details{font-size:13px}
</style>