<script lang="ts">
  import {getContext} from 'svelte';
  import {browser} from '$app/env';
  import {goto} from '$app/navigation';
  import {ADMIN_AUTH_CONTEXT,type AdminAuthController} from '../../../../lib/admin/controller.ts';
  import {hasAdminPermission} from '../../../../lib/admin/permissions.ts';
  import {validAdminQuizList,validAdminQuizDetail,validAdminQuizTransition,
    validAdminQuizDraft,newQuizQuestion,QUIZ_TOPICS,QUIZ_POSITIONS,
    type QuizOrg,type AdminQuizSummary,type AdminQuizDetail,
    type AdminQuizQuestionDraft,type AdminQuizDraft} from '../../../../lib/admin/player-quiz-editorial.ts';
  import AdminFrame from '../../../../lib/admin/AdminFrame.svelte';

  const auth=getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  const permitted=$derived($auth.status==='authenticated'&&$auth.session&&
    ['manager','super_admin','editor'].includes($auth.session.user.role)&&
    hasAdminPermission($auth.session.user.permissions,'news:read')
    ? $auth.session.user.organizations.filter(o=>
        o.id==='lefteria_fc'||o.id==='lefteria_fc_academy')
        .map(o=>o.id as QuizOrg):[]);
  const manager=$derived($auth.status==='authenticated'&&
    ($auth.session?.user.role==='manager'||$auth.session?.user.role==='super_admin'));
  const canWrite=$derived(permitted.length>0&&
    hasAdminPermission($auth.session?.user.permissions??[],'news:write'));
  let organization=$state<QuizOrg|null>(null);
  let items=$state<AdminQuizSummary[]>([]);
  let detail=$state<AdminQuizDetail|null>(null);
  let loading=$state(false),busy=$state(false),reload=$state(0);
  let error=$state<string|null>(null),success=$state<string|null>(null);
  let activeDetailRequest=0;

  let weekNumber=$state(1);
  let topic=$state<string>('general');
  let position=$state<string>('all');
  let title=$state('');
  let questions=$state<AdminQuizQuestionDraft[]>([
    newQuizQuestion(),newQuizQuestion(),newQuizQuestion()]);
  const draft=$derived<AdminQuizDraft>({
    week_number:weekNumber,topic,position,title,
    questions:questions.map(q=>({prompt:q.prompt,choices:[...q.choices] as
      [string,string,string,string],correct_index:q.correct_index,explanation:q.explanation}))
  });
  const draftValid=$derived(validAdminQuizDraft(draft));

  $effect(()=>{if(browser&&$auth.status==='anonymous')void goto('/admin/login',{replace:true});});
  $effect(()=>{
    if(!organization||!permitted.includes(organization))organization=permitted[0]??null;
  });
  $effect(()=>{
    const owner=organization,actor=$auth.session?.user.id,generation=reload;
    void actor;void generation;
    items=[];detail=null;error=null;activeDetailRequest++;
    if(!browser||!owner||!permitted.includes(owner))return;
    const abort=new AbortController();
    loading=true;
    void(async()=>{
      try{
        const response=await fetch('/api/admin/hub/player-quizzes?organization_id='+owner,{
          credentials:'same-origin',cache:'no-store',signal:abort.signal});
        if(abort.signal.aborted)return;
        if(response.status===401){auth.invalidateSession();return;}
        if(response.status===403){void auth.bootstrap();return;}
        if(!response.ok)throw new Error();
        const value:unknown=await response.json();
        if(!validAdminQuizList(value,owner))throw new Error();
        if(!abort.signal.aborted)items=value.items;
      }catch{
        if(!abort.signal.aborted)error='Δεν φορτώθηκαν με ασφάλεια τα κουίζ.';
      }finally{if(!abort.signal.aborted)loading=false;}
    })();
    return()=>abort.abort();
  });

  async function createDraft(){
    const owner=organization,actor=$auth.session?.user.id;
    if(!owner||!permitted.includes(owner)||!canWrite||busy||!draftValid){
      error='Συμπλήρωσε τουλάχιστον τρεις ερωτήσεις με τέσσερις διαφορετικές απαντήσεις.';return;
    }
    busy=true;error=null;success=null;
    try{
      const response=await fetch('/api/admin/hub/player-quizzes?organization_id='+owner,{
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify(draft)});
      if(response.status===401){auth.invalidateSession();return;}
      if(response.status===403){void auth.bootstrap();return;}
      if(!response.ok)throw new Error();
      const v:unknown=await response.json();
      if(!v||typeof v!=='object'||Array.isArray(v)||
        (v as Record<string,unknown>).organization_id!==owner||
        (v as Record<string,unknown>).status!=='review_required'||
        (v as Record<string,unknown>).question_count!==questions.length)throw new Error();
      if(organization!==owner||$auth.session?.user.id!==actor)return;
      success='Το κουίζ καταχωρίστηκε για ανεξάρτητο ανθρώπινο έλεγχο. Δεν έχει δημοσιευθεί.';
      title='';questions=[newQuizQuestion(),newQuizQuestion(),newQuizQuestion()];
      reload++;
    }catch{error='Το πρόχειρο κουίζ δεν επιβεβαιώθηκε. Δοκίμασε ξανά.';}
    finally{busy=false;}
  }
  async function selectQuiz(item:AdminQuizSummary){
    const owner=organization,actor=$auth.session?.user.id;
    if(!owner||!manager||busy)return;
    const current=++activeDetailRequest;detail=null;error=null;success=null;
    try{
      const response=await fetch('/api/admin/hub/player-quizzes/'+
        encodeURIComponent(item.id)+'?organization_id='+owner,{
          credentials:'same-origin',cache:'no-store'});
      if(response.status===401){auth.invalidateSession();return;}
      if(response.status===403){void auth.bootstrap();return;}
      if(!response.ok)throw new Error();
      const v:unknown=await response.json();
      if(!validAdminQuizDetail(v,owner,item.id))throw new Error();
      if(current===activeDetailRequest&&organization===owner&&
        $auth.session?.user.id===actor)detail=v;
    }catch{
      if(current===activeDetailRequest)error='Δεν επαληθεύτηκαν τα στοιχεία του κουίζ.';
    }
  }
  async function transition(action:'approve'|'publish'|'withdraw'){
    const selected=detail,owner=organization,actor=$auth.session?.user.id;
    if(!selected||!owner||!manager||busy||
      (action==='approve'&&selected.status!=='review_required')||
      (action==='publish'&&selected.status!=='approved')||
      (action==='withdraw'&&!['review_required','approved','published'].includes(selected.status)))return;
    if(action==='publish'&&!confirm('Να δημοσιευθεί αυτό το εγκεκριμένο κουίζ για τους αθλητές;'))return;
    if(action==='withdraw'&&!confirm('Να αποσυρθεί το κουίζ από όλους τους αθλητές; Οι προηγούμενες βαθμολογίες διατηρούνται.'))return;
    busy=true;error=null;success=null;
    try{
      const response=await fetch('/api/admin/hub/player-quizzes/'+
        encodeURIComponent(selected.id)+'/'+action+'?organization_id='+owner,{
          method:'POST',credentials:'same-origin',cache:'no-store',
          headers:{'X-Lefteria-Admin':'1'}});
      if(response.status===401){auth.invalidateSession();return;}
      if(response.status===403){void auth.bootstrap();return;}
      if(!response.ok)throw new Error();
      const v:unknown=await response.json();
      if(!validAdminQuizTransition(v,owner,selected.id,
        action==='approve'?'approved':action==='publish'?'published':'withdrawn'))throw new Error();
      if(organization!==owner||$auth.session?.user.id!==actor)return;
      success=action==='approve'
        ?'Ο ανθρώπινος έλεγχος ολοκληρώθηκε. Απαιτείται ξεχωριστή δημοσίευση.'
        :action==='publish'?'Το κουίζ δημοσιεύθηκε για την κατάλληλη προσωπική εβδομάδα.'
        :'Το κουίζ αποσύρθηκε από την εφαρμογή χωρίς διαγραφή βαθμολογιών.';
      detail=null;activeDetailRequest++;reload++;
    }catch{error='Η ενέργεια δεν επιβεβαιώθηκε. Ίσως απαιτείται διαφορετικός υπεύθυνος ή νέα ανάκτηση.';}
    finally{busy=false;}
  }
  function resetQuestions(){questions=[newQuizQuestion(),newQuizQuestion(),newQuizQuestion()];}
</script>

<svelte:head><title>Εβδομαδιαία κουίζ · HUB Admin | Lefteria FC</title></svelte:head>
{#if $auth.status==='authenticated'&&$auth.session}
  <AdminFrame user={$auth.session.user} busy={$auth.busy||busy} onLogout={()=>void auth.logout()}>
    <main class="editor" data-testid="weekly-quiz-admin">
      <header>
        <p class="eyebrow">ΛΕΥΤΕΡΙΑ FC · HUB · ΑΘΛΗΤΕΣ</p>
        <h1>Εβδομαδιαία ποδοσφαιρικά κουίζ</h1>
        <p>Κάθε αθλητής προχωρά με δική του σειρά. Οι ερωτήσεις απαιτούν ανθρώπινη έγκριση
          από διαφορετικό υπεύθυνο και ξεχωριστή δημοσίευση.</p>
        <a href="/admin/hub">Επιστροφή στο HUB editorial</a>
      </header>
      {#if permitted.length}
        <div class="toolbar">
          <label>Οργανισμός
            <select bind:value={organization}>
              {#each permitted as owner (owner)}
                <option value={owner}>{owner==='lefteria_fc'?'Lefteria FC':'Lefteria FC Academy'}</option>
              {/each}
            </select>
          </label>
          <button type="button" disabled={loading||busy} onclick={()=>reload++}>Ανανέωση</button>
        </div>
        {#if canWrite}
          <section class="panel" aria-labelledby="quiz-create">
            <h2 id="quiz-create">Νέο πρόχειρο κουίζ για έλεγχο</h2>
            <p>Δημιουργείται ως «Απαιτείται έγκριση». Δεν στέλνεται ποτέ αυτόματα στους αθλητές.</p>
            <div class="fields">
              <label>Εβδομάδα
                <input type="number" min="1" max="520" bind:value={weekNumber}/>
              </label>
              <label>Θέμα
                <select bind:value={topic}>
                  {#each QUIZ_TOPICS as t}<option value={t}>{t}</option>{/each}
                </select>
              </label>
              <label>Θέση
                <select bind:value={position}>
                  {#each QUIZ_POSITIONS as p}<option value={p}>{p}</option>{/each}
                </select>
              </label>
            </div>
            <label>Τίτλος κουίζ
              <input type="text" maxlength="180" bind:value={title}
                placeholder="Εβδομάδα 1 · Θέσεις και βασικοί ρόλοι"/>
            </label>
            {#each questions as question,index (index)}
              <fieldset>
                <legend>Ερώτηση {index+1}</legend>
                <label>Ερώτηση
                  <textarea rows="3" maxlength="500" bind:value={question.prompt}></textarea>
                </label>
                <div class="answers">
                  {#each [0,1,2,3] as optionIndex}
                    <label>Απάντηση {optionIndex+1}
                      <input type="text" maxlength="500"
                        bind:value={question.choices[optionIndex]}/>
                    </label>
                  {/each}
                </div>
                <label>Σωστή απάντηση
                  <select bind:value={question.correct_index}>
                    <option value={0}>1</option><option value={1}>2</option>
                    <option value={2}>3</option><option value={3}>4</option>
                  </select>
                </label>
                <label>Σημείωση για τον ανθρώπινο έλεγχο
                  <textarea rows="2" maxlength="2000" bind:value={question.explanation}></textarea>
                </label>
                {#if questions.length>3}
                  <button type="button" disabled={busy}
                    onclick={()=>questions=questions.filter((_,i)=>i!==index)}>Αφαίρεση ερώτησης</button>
                {/if}
              </fieldset>
            {/each}
            <div class="actions">
              <button type="button" disabled={busy||questions.length>=20}
                onclick={()=>questions=[...questions,newQuizQuestion()]}>Προσθήκη ερώτησης</button>
              <button type="button" disabled={busy} onclick={resetQuestions}>Καθαρισμός ερωτήσεων</button>
              <button type="button" class="primary" disabled={busy||!draftValid}
                onclick={()=>void createDraft()}>Υποβολή για ανθρώπινο έλεγχο</button>
            </div>
          </section>
        {/if}
        <div class="layout">
          <section class="panel">
            <h2>Καταχωρισμένα κουίζ</h2>
            {#if loading}<p role="status">Φόρτωση…</p>
            {:else if !items.length}<p>Δεν έχουν υποβληθεί κουίζ.</p>{/if}
            <ul class="list">
              {#each items as item (item.id)}
                <li>
                  <button type="button" disabled={busy||!manager}
                    onclick={()=>void selectQuiz(item)}>
                    <strong>{item.title}</strong>
                    <span>Εβδομάδα {item.week_number} · {item.position} ·
                      {item.status} · {item.question_count} ερωτήσεις</span>
                  </button>
                </li>
              {/each}
            </ul>
          </section>
          {#if manager}
            <section class="panel">
              <h2>Ανεξάρτητος έλεγχος και δημοσίευση</h2>
              {#if detail}
                <p>Εβδομάδα {detail.week_number} · {detail.topic} · {detail.position}</p>
                <h3>{detail.title}</h3>
                <p>Κατάσταση: <strong>{detail.status}</strong></p>
                {#each detail.questions as question (question.id)}
                  <div class="review-question">
                    <strong>{question.sequence}. {question.prompt}</strong>
                    <ol>
                      {#each question.choices as choice,index}
                        <li class:correct={index===question.correct_index}>
                          {choice}{index===question.correct_index?' ✓':''}
                        </li>
                      {/each}
                    </ol>
                    {#if question.explanation}<p>{question.explanation}</p>{/if}
                  </div>
                {/each}
                {#if detail.status==='review_required'}
                  <button type="button" class="primary" disabled={busy}
                    onclick={()=>void transition('approve')}>Έγκριση κουίζ</button>
                {:else if detail.status==='approved'}
                  <button type="button" class="primary" disabled={busy}
                    onclick={()=>void transition('publish')}>Δημοσίευση για αθλητές</button>
                {/if}
                {#if ['review_required','approved','published'].includes(detail.status)}
                  <button type="button" disabled={busy}
                    onclick={()=>void transition('withdraw')}>Απόσυρση κουίζ</button>
                {/if}
              {:else}
                <p>Επίλεξε κουίζ από τη λίστα για να δεις και να ελέγξεις όλες τις απαντήσεις.</p>
              {/if}
            </section>
          {/if}
        </div>
      {:else}
        <p role="alert">Δεν υπάρχει επαληθευμένη άδεια διαχείρισης HUB.</p>
      {/if}
      {#if error}<p role="alert" class="error">{error}</p>{/if}
      {#if success}<p role="status" class="success">{success}</p>{/if}
    </main>
  </AdminFrame>
{/if}

<style>
  .editor{width:min(100%,1150px);margin:0 auto;padding:24px;color:var(--lf-text,#f0f0ee)}
  header{padding:25px;border:1px solid #785b24;border-radius:14px;background:#1b1d1c}
  .eyebrow{font-size:11px;letter-spacing:.13em;color:#f5bb44;font-weight:700}
  h1{font-size:clamp(28px,5vw,42px);margin:7px 0}h2{font-size:22px;margin:0 0 15px}
  header p,.panel p{font-size:14px;line-height:1.7;color:#c2c6c4}
  a{color:#f3bc50}
  .toolbar,.actions{display:flex;flex-wrap:wrap;align-items:end;gap:12px;margin:18px 0}
  .layout{display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1fr);gap:17px}
  .panel{padding:22px;border:1px solid #414542;border-radius:14px;background:#1b1e1e;margin:16px 0}
  label{display:grid;gap:7px;font-size:13px;font-weight:700;min-width:0;margin:10px 0}
  .fields,.answers{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px}
  input,select,textarea{width:100%;min-height:44px;background:#121515;color:#f4f2ec;border:1px solid #626463;border-radius:9px;padding:10px;font:inherit}
  fieldset{border:1px solid #5b5449;border-radius:10px;padding:15px;margin:15px 0}
  legend{font-weight:700;font-size:14px;padding:0 6px}
  button{min-height:44px;border:1px solid #947331;border-radius:9px;background:#30281b;color:#ffe1a0;padding:10px 15px;cursor:pointer}
  button:disabled{opacity:.55;cursor:default}
  .primary{background:#c89426;color:#151512;font-weight:700}
  button:focus-visible,a:focus-visible{outline:2px solid #ffd36d;outline-offset:3px}
  .list{list-style:none;padding:0}.list li{margin:9px 0}
  .list button{width:100%;display:grid;gap:7px;text-align:left}.list span{font-size:12px}
  .review-question{padding:13px 0;border-top:1px solid #414542;font-size:14px;line-height:1.7}
  .review-question li.correct{color:#d4ecaa;font-weight:700}
  .error{color:#ffcdbe}.success{color:#d4ecaa}
  @media(max-width:800px){.layout,.fields,.answers{grid-template-columns:1fr}.editor{padding:12px}}
</style>
