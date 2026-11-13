<script lang="ts">
  import {getContext,untrack} from 'svelte';
  import {browser} from '$app/env';
  import {ADMIN_AUTH_CONTEXT,type AdminAuthController} from './controller.ts';
  import {validTrainingPlanPage,validTrainingPlanForm,matchingTrainingPlanWrite,
    validTrainingPlanApprovals,validTrainingPlanApprovalResult,
    validTrainingDistributionPreview}
    from './academy-training-plans.ts';
  import type {TrainingPlanDraft,TrainingPlanForm,TrainingDistributionPreview} from './academy-training-plans.ts';

  let {groupId,from,to,actor,allowed,canWrite}:{
    groupId:string;from:string;to:string;actor:string;
    allowed:boolean;canWrite:boolean;
  }=$props();
  const auth=getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  const org='lefteria_fc_academy';
  let rows=$state<TrainingPlanDraft[]>([]);
  let loading=$state(false),saving=$state(false);
  let approvedIds=$state<string[]>([]),reviewConfirmed=$state(false);
  let preview=$state<TrainingDistributionPreview|null>(null),previewing=$state(false);
  let error=$state(''),notice=$state(''),refreshNonce=$state(0);
  let editing=$state<TrainingPlanDraft|null>(null);
  let archiveConfirmed=$state(false);
  let form=$state<TrainingPlanForm>({
    day:from,start_time:'17:00',duration_minutes:75,
    title:'',location:'',notes:''
  });
  function reset(){
    editing=null;archiveConfirmed=false;reviewConfirmed=false;preview=null;notice='';error='';
    form={day:from,start_time:'17:00',duration_minutes:75,
      title:'',location:'',notes:''};
  }
  function edit(row:TrainingPlanDraft){
    if(row.status!=='draft')return;
    editing=row;archiveConfirmed=false;notice='';error='';
    form={day:row.day,start_time:row.start_time,
      duration_minutes:row.duration_minutes,title:row.title,
      location:row.location,notes:row.notes};
  }
  const base='/api/admin/academy/training/plans/';
  const ownerQuery='organization_id='+org;

  $effect(()=>{
    const scope=actor+':'+groupId+':'+from+':'+to;
    const valid=allowed&&!!actor&&!!groupId&&!!from&&!!to;
    void refreshNonce;void scope;
    rows=[];approvedIds=[];preview=null;editing=null;archiveConfirmed=false;reviewConfirmed=false;notice='';error='';
    if(!browser||!valid)return;
    const controller=new AbortController();
    loading=true;
    untrack(()=>{void(async()=>{
      try{
        const params=new URLSearchParams({
          organization_id:org,group_id:groupId,start:from,end:to
        });
        const response=await fetch(base+'?'+params,{
          credentials:'same-origin',cache:'no-store',
          headers:{Accept:'application/json'},signal:controller.signal
        });
        if(controller.signal.aborted)return;
        if(response.status===401||response.status===403){
          void auth.bootstrap();throw Error('Τα δικαιώματα άλλαξαν.');
        }
        if(!response.ok)throw Error('Δεν φορτώθηκαν τα προσωπικά πρόχειρα προπονήσεων.');
        const raw:unknown=await response.json();
        if(!validTrainingPlanPage(raw,groupId,from,to))
          throw Error('Μη επαληθευμένη λίστα προγραμματισμένων προπονήσεων.');
        const approvalResponse=await fetch(base+'approvals?'+params,{
          credentials:'same-origin',cache:'no-store',
          headers:{Accept:'application/json'},signal:controller.signal
        });
        if(controller.signal.aborted)return;
        if(approvalResponse.status===401||approvalResponse.status===403){
          void auth.bootstrap();throw Error('Τα δικαιώματα άλλαξαν.');
        }
        if(!approvalResponse.ok)throw Error('Δεν επιβεβαιώθηκαν οι εγκρίσεις προπονήσεων.');
        const approvals:unknown=await approvalResponse.json();
        if(!validTrainingPlanApprovals(approvals,groupId,from,to,raw.plans))
          throw Error('Μη επαληθευμένες εγκρίσεις προπονήσεων.');
        if(!controller.signal.aborted){
          rows=raw.plans;approvedIds=approvals.approvals.map(p=>p.id);
        }
      }catch(e){
        if(!controller.signal.aborted)
          error=e instanceof Error?e.message:'Σφάλμα φόρτωσης προπονήσεων.';
      }finally{if(!controller.signal.aborted)loading=false;}
    })();});
    return()=>controller.abort();
  });

  async function inspect(plan:TrainingPlanDraft){
    if(!browser||!allowed||loading||saving||previewing||
      !approvedIds.includes(plan.id)||plan.status!=='draft')return;
    const owner=actor,group=groupId,period=from+':'+to;
    previewing=true;preview=null;error='';
    try{
      const params=new URLSearchParams({organization_id:org,
        version:String(plan.version)});
      const response=await fetch(
        base+encodeURIComponent(plan.id)+'/publication-preview?'+params,{
          credentials:'same-origin',cache:'no-store',
          headers:{Accept:'application/json'}
        });
      if(owner!==actor||group!==groupId||period!==from+':'+to||!allowed)return;
      if(response.status===401||response.status===403){
        void auth.bootstrap();throw Error('Τα δικαιώματα άλλαξαν.');
      }
      if(response.status===409){
        refreshNonce++;throw Error('Η έγκριση δεν αφορά πια αυτή την έκδοση.');
      }
      if(!response.ok)throw Error('Η προεπισκόπηση δεν είναι διαθέσιμη.');
      const raw:unknown=await response.json();
      if(!validTrainingDistributionPreview(raw,plan))
        throw Error('Η προεπισκόπηση δεν επαληθεύτηκε.');
      preview=raw;
    }catch(e){error=e instanceof Error?e.message:'Σφάλμα προεπισκόπησης.';}
    finally{previewing=false;}
  }

  async function approve(plan:TrainingPlanDraft){
    if(!browser||!canWrite||loading||saving||!reviewConfirmed||
      approvedIds.includes(plan.id)||plan.status!=='draft')return;
    const owner=actor,group=groupId,period=from+':'+to;
    saving=true;error='';notice='';
    try{
      const response=await fetch(
        base+encodeURIComponent(plan.id)+'/approve?'+ownerQuery,{
          method:'POST',credentials:'same-origin',cache:'no-store',
          headers:{'Content-Type':'application/json','Accept':'application/json',
            'X-Lefteria-Admin':'1'},
          body:JSON.stringify({expected_version:plan.version,
            confirm_human_review:true})
        });
      if(owner!==actor||group!==groupId||period!==from+':'+to||
        !allowed||!canWrite)return;
      if(response.status===401||response.status===403){
        void auth.bootstrap();throw Error('Η εξουσιοδότηση άλλαξε.');
      }
      if(response.status===409){
        refreshNonce++;throw Error('Το πρόχειρο άλλαξε ή εγκρίθηκε. Ελέγξτε τη νέα έκδοση.');
      }
      if(!response.ok)throw Error('Δεν εγκρίθηκε το πρόχειρο.');
      const raw:unknown=await response.json();
      if(!validTrainingPlanApprovalResult(raw,plan))
        throw Error('Η έγκριση δεν επαληθεύτηκε.');
      reviewConfirmed=false;
      notice='Η συγκεκριμένη έκδοση εγκρίθηκε από άνθρωπο. ΔΕΝ δημοσιεύτηκε.';
      refreshNonce++;
    }catch(e){error=e instanceof Error?e.message:'Δεν εγκρίθηκε το πρόχειρο.';}
    finally{saving=false;}
  }

  async function save(archive=false){
    if(!browser||!canWrite||saving||loading||!validTrainingPlanForm(form)||
      form.day<from||form.day>to||
      archive&&(!editing||!archiveConfirmed))return;
    const current=editing,expected=current?.version??0;
    const owner=actor,group=groupId,period=from+':'+to;
    const url=current?base+encodeURIComponent(current.id)+'?'+ownerQuery
      :base+'?'+ownerQuery;
    const body={
      group_id:group,day:form.day,start_time:form.start_time,
      duration_minutes:form.duration_minutes,title:form.title.trim(),
      location:form.location,notes:form.notes,
      expected_version:expected,archive
    };
    saving=true;error='';notice='';
    try{
      const response=await fetch(url,{
        method:current?'PUT':'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','Accept':'application/json',
          'X-Lefteria-Admin':'1'},
        body:JSON.stringify(body)
      });
      if(owner!==actor||group!==groupId||period!==from+':'+to||
        !allowed||!canWrite)return;
      if(response.status===401||response.status===403){
        void auth.bootstrap();throw Error('Η συνεδρία ή τα δικαιώματα άλλαξαν.');
      }
      if(response.status===409){
        refreshNonce++;throw Error('Η ώρα συγκρούεται με άλλο ενεργό πρόχειρο του τμήματος ή άλλαξε η έκδοση. Ελέγξτε το πρόγραμμα και δοκιμάστε ξανά.');
      }
      if(!response.ok)throw Error('Η αλλαγή δεν αποθηκεύτηκε. Δεν έγινε δημοσίευση.');
      const payload:unknown=await response.json();
      if(!matchingTrainingPlanWrite(payload,group,current?.id,
        expected+1,archive?'archived':'draft'))
        throw Error('Δεν επαληθεύτηκε η ενημερωμένη έκδοση.');
      reset();
      notice=archive?'Το πρόχειρο αρχειοθετήθηκε. Δεν δημοσιεύτηκε.'
        :'Το ιδιωτικό πρόχειρο αποθηκεύτηκε. Δεν εμφανίζεται σε γονείς ή αθλητές.';
      refreshNonce++;
    }catch(e){error=e instanceof Error?e.message:'Η αλλαγή δεν αποθηκεύτηκε.';}
    finally{saving=false;}
  }
</script>

<section class="plan-drafts" aria-labelledby="training-plan-heading" data-testid="academy-private-training-plans">
  <header class="plan-header">
    <div><p class="plan-eyebrow">ΜΟΝΟ ΓΙΑ ΔΙΑΧΕΙΡΙΣΤΕΣ · ΠΡΟΧΕΙΡΑ</p>
      <h2 id="training-plan-heading">Ιδιωτικός σχεδιασμός προπονήσεων</h2></div>
    <button type="button" onclick={()=>refreshNonce++} disabled={loading||saving}>Ανανέωση</button>
  </header>
  <p class="plan-note">Τα πρόχειρα είναι ξεχωριστά από το επίσημο ημερολόγιο και ΔΕΝ δημοσιεύονται στους αθλητές, γονείς ή προπονητές. Η πραγματική έκδοση προπόνησης παραμένει επόμενο στάδιο.</p>
  {#if error}<p class="plan-error" role="alert">{error}</p>{/if}
  {#if notice}<p class="plan-notice" role="status">{notice}</p>{/if}
  {#if loading}
    <p role="status">Επαλήθευση ιδιωτικών προπονήσεων…</p>
  {:else}
    {#if !rows.length}<p class="plan-note">Δεν έχουν δημιουργηθεί ιδιωτικά πρόχειρα για τον μήνα και το επιλεγμένο τμήμα.</p>{/if}
    <ul class="plan-list">
      {#each rows as plan (plan.id)}
        <li class:archived={plan.status==='archived'}>
          <span><strong>{plan.title}</strong><small>{plan.day} · {plan.start_time} · {plan.duration_minutes}′
            {plan.location?' · '+plan.location:''}</small>
            <small>Έκδοση {plan.version} · {plan.status==='archived'?'Αρχειοθετημένο':approvedIds.includes(plan.id)?'Εγκρίθηκε ανθρώπινα · ΟΧΙ δημοσιευμένο':'Αναμένει ανθρώπινη έγκριση'}</small></span>
          {#if canWrite&&plan.status==='draft'}
            <button type="button" disabled={saving} onclick={()=>edit(plan)}>Επεξεργασία</button>
            {#if !approvedIds.includes(plan.id)}
              <button type="button" disabled={saving||!reviewConfirmed}
                onclick={()=>void approve(plan)}>Έγκριση έκδοσης {plan.version}</button>
            {:else}
              <button type="button" disabled={saving||previewing}
                onclick={()=>void inspect(plan)}>Προεπισκόπηση αποδεκτών</button>
            {/if}
          {/if}
        </li>
      {/each}
    </ul>
  {/if}
  {#if preview}
    <section class="training-distribution" aria-label="Προεπισκόπηση αποδεκτών" data-testid="training-distribution-preview">
      <h3>Προεπισκόπηση · χωρίς δημοσίευση</h3>
      <p><strong>{preview.title}</strong> · {preview.day} {preview.start_time} · {preview.duration_minutes} λεπτά</p>
      <p>Τμήμα: {groupId} · Τοποθεσία: {preview.location||'Δεν ορίστηκε'}</p>
      <p>Τρέχοντες επιλέξιμοι αθλητές: <strong>{preview.eligible_player_count}</strong></p>
      {#if preview.eligible_player_count===0}
        <p class="plan-error">Δεν υπάρχουν επιλέξιμοι αθλητές στο τμήμα.</p>
      {/if}
      <p class="plan-note">Εγκρίθηκε από άνθρωπο · Δεν δημοσιεύτηκε, δεν στάλθηκαν ειδοποιήσεις. Ο αριθμός μπορεί να αλλάξει με τις αναθέσεις αθλητών.</p>
      <button type="button" onclick={()=>preview=null}>Κλείσιμο προεπισκόπησης</button>
    </section>
  {/if}
  {#if canWrite}
    <label class="review-confirm"><input type="checkbox"
      bind:checked={reviewConfirmed} disabled={saving||loading}/>
      Εξέτασα προσωπικά την ακριβή ημερομηνία, ώρα, διάρκεια και ομάδα. Η έγκριση ΔΕΝ δημοσιεύει προπόνηση.
    </label>
    <form class="plan-form" onsubmit={e=>{e.preventDefault();void save();}}>
      <div class="plan-header">
        <h3>{editing?'Επεξεργασία πρόχειρου':'Νέο ιδιωτικό πρόχειρο'}</h3>
        {#if editing}<button type="button" onclick={reset} disabled={saving}>Νέο πρόχειρο</button>{/if}
      </div>
      <div class="plan-fields">
        <label>Ημερομηνία <input type="date" bind:value={form.day} min={from} max={to} required disabled={saving}/></label>
        <label>Ώρα έναρξης <input type="time" bind:value={form.start_time} required disabled={saving}/></label>
        <label>Διάρκεια (λεπτά) <input type="number" bind:value={form.duration_minutes} min="15" max="240" step="5" required disabled={saving}/></label>
        <label>Τίτλος <input type="text" bind:value={form.title} maxlength="180" required disabled={saving}/></label>
        <label>Γήπεδο / τοποθεσία <input type="text" bind:value={form.location} maxlength="200" disabled={saving}/></label>
        <label class="full">Σημειώσεις σχεδιασμού <textarea bind:value={form.notes} maxlength="2000" rows="3" disabled={saving}/></label>
      </div>
      <button class="save" type="submit"
        disabled={loading||saving||!validTrainingPlanForm(form)||form.day<from||form.day>to}>
        {saving?'Αποθήκευση…':editing?'Ενημέρωση πρόχειρου':'Δημιουργία ιδιωτικού πρόχειρου'}
      </button>
    </form>
    {#if editing}
      <div class="archive-actions">
        <label><input type="checkbox" bind:checked={archiveConfirmed} disabled={saving}/>
          Επιβεβαιώνω την αρχειοθέτηση του συγκεκριμένου πρόχειρου.</label>
        <button type="button" disabled={!archiveConfirmed||saving||loading}
          onclick={()=>void save(true)}>Αρχειοθέτηση · έκδοση {editing.version}</button>
      </div>
    {/if}
  {/if}
</section>

<style>
  .plan-drafts{margin:25px 0;background:#191b1c;border:1px solid #514127;border-radius:12px;padding:18px;color:#eee}
  .plan-header{display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap}
  .plan-eyebrow{font-size:10px;letter-spacing:.12em;color:#ffca46}
  h2{font-size:22px;line-height:1.2;margin:6px 0}h3{font-size:17px;margin:0}
  .plan-note{font-size:12px;color:#aeb5b6;line-height:1.6}
  .plan-error{color:#ffadad}.plan-notice{color:#b7e8c0}
  button{min-height:44px;border:1px solid #6b5327;border-radius:8px;background:#2b2519;color:#ffca46;padding:9px 12px;cursor:pointer}
  button:disabled{opacity:.45;cursor:not-allowed}
  button:focus-visible,input:focus-visible,textarea:focus-visible{outline:2px solid #ffca46;outline-offset:3px}
  .plan-list{list-style:none;margin:16px 0;padding:0;display:grid;gap:8px}
  .plan-list li{border:1px solid #3e4040;border-radius:9px;padding:12px;display:flex;justify-content:space-between;align-items:center;gap:10px}
  .plan-list li span{display:grid;gap:5px}.plan-list small{color:#adb3b4;font-size:11px}.archived{opacity:.65}
  .plan-form{border-top:1px solid #464039;padding-top:18px;margin-top:16px}
  .plan-fields{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:14px;margin:14px 0}
  .plan-fields label{display:grid;gap:6px;font-size:12px;color:#e1cb9d}
  .plan-fields label.full{grid-column:1/-1}
  .plan-fields input,.plan-fields textarea{min-height:44px;border:1px solid #515355;border-radius:7px;background:#101213;color:#fff;padding:9px;font:inherit;max-width:100%;min-width:0}
  .save{border-color:#bb8a30;background:#493412;color:#fff;font-weight:700}
  .training-distribution{border:1px solid #bd8e3f;background:#211e15;border-radius:9px;padding:14px;margin:16px 0}
  .training-distribution h3{color:#ffca46}
  .training-distribution p{font-size:12px;line-height:1.6}
  .review-confirm{display:flex;gap:9px;align-items:center;font-size:12px;margin:14px 0;color:#f2ddac}
  .review-confirm input{accent-color:#f5a623;min-width:18px;min-height:18px}
  .archive-actions{display:flex;gap:12px;align-items:center;flex-wrap:wrap;margin-top:15px}
  .archive-actions label{font-size:12px}.archive-actions input{accent-color:#f5a623}
  @media(max-width:650px){.plan-fields{grid-template-columns:1fr}.plan-list li{align-items:start;flex-direction:column}}
</style>
