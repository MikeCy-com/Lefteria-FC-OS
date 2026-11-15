<script lang="ts">
  type ManualApproval={
    id:string;bill_id:string;amount_cents:number;
    status:'pending'|'approved_manual'|'rejected';
    reviewed_by:string|null;reversed_manual:boolean;
  };
  type Reversal={
    id:string;target_kind:'payment'|'credit';target_id:string;
    reason:string;requested_by:string;requested_at:string;
    status:'pending'|'approved_manual'|'rejected';
    reviewed_by:string|null;reviewed_at:string|null;
    review_note:string|null;
  };
  let {owner,sessionId,canWrite,canReview,payments,credits,reversals,onSaved}:{
    owner:string;sessionId:string;canWrite:boolean;canReview:boolean;
    payments:ManualApproval[];credits:ManualApproval[];
    reversals:Reversal[];onSaved:()=>void
  }=$props();

  let targetKind=$state<'payment'|'credit'>('payment');
  let targetId=$state('');
  let reason=$state('');
  let clientId=$state<string|null>(null);
  let busy=$state(false);
  let reviewing=$state(false);
  let reviewId=$state('');
  let approve=$state<'approve'|'reject'>('approve');
  let reviewNote=$state('');
  let error=$state('');
  let notice=$state('');
  const euro=(cents:number)=>new Intl.NumberFormat('el-CY',
    {style:'currency',currency:'EUR'}).format(cents/100);
  const validUuid=(input:unknown)=>typeof input==='string'&&
    /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(input);
  const clean=(text:string,min:number,max:number)=>text.length>=min&&
    text.length<=max&&text.trim()===text&&![...text].some(x=>/\p{Cc}/u.test(x));
  const validTargets=$derived((targetKind==='payment'?payments:credits).filter(x=>
    x.status==='approved_manual'&&!x.reversed_manual));
  const openReviews=$derived(reversals.filter(r=>{
    if(r.status!=='pending'||r.requested_by===sessionId)return false;
    const source=(r.target_kind==='payment'?payments:credits)
      .find(x=>x.id===r.target_id);
    return !!source&&source.status==='approved_manual'&&
      source.reviewed_by!==sessionId&&!source.reversed_manual;
  }));
  const pendingTargets=$derived(new Set(reversals.filter(r=>
    r.status==='pending'||r.status==='approved_manual')
    .map(r=>r.target_kind+':'+r.target_id)));
  $effect(()=>{
    void owner;void sessionId;
    targetId='';reviewId='';clientId=null;notice='';error='';
  });
  $effect(()=>{
    void targetKind;
    targetId='';clientId=null;
  });

  async function requestReversal(event:SubmitEvent){
    event.preventDefault();
    const selected=owner,actor=sessionId;
    const target=validTargets.find(x=>x.id===targetId);
    if(!canWrite||busy||!selected||!target||!clean(reason,12,180)||
      pendingTargets.has(targetKind+':'+targetId)){
      error='Επιλέξτε επιβεβαιωμένα ενεργή χειροκίνητη έγκριση και περιγράψτε τη διόρθωση.';
      return;
    }
    clientId??=crypto.randomUUID();
    const key=clientId,kind=targetKind,id=targetId;
    busy=true;error='';notice='';
    try{
      const response=await fetch(
        '/api/admin/finance/supplier-reversals?organization_id='+encodeURIComponent(selected),{
          method:'POST',credentials:'same-origin',cache:'no-store',
          headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
          body:JSON.stringify({
            client_request_id:key,target_kind:kind,target_id:id,reason})
        });
      if(owner!==selected||sessionId!==actor)return;
      if(response.status===409)clientId=null;
      if(!response.ok)throw new Error(response.status===403
        ?'Η άδεια καταχώρισης διορθώσεων δεν ισχύει πλέον.'
        :response.status===409?'Η διόρθωση συγκρούεται με άλλη απόφαση. Ανανεώστε τα στοιχεία.'
        :'Η καταχώριση δεν επαληθεύτηκε. Ελέγξτε την αναφορά πριν την επανάληψη.');
      const raw:unknown=await response.json();
      if(!raw||typeof raw!=='object'||Array.isArray(raw))throw new Error('Μη έγκυρη απάντηση.');
      const result=raw as Record<string,unknown>;
      if(!validUuid(result.id)||result.organization_id!==selected||
        result.target_kind!==kind||result.target_id!==id||
        !['pending','approved_manual','rejected'].includes(String(result.status))||
        result.bank_verified!==false||result.bank_refund_recorded!==false||
        result.posted_to_general_ledger!==false)
        throw new Error('Η απόκριση διόρθωσης δεν επαληθεύτηκε.');
      clientId=null;reason='';targetId='';
      notice='Η αίτηση καταχωρίστηκε. Δεν αλλάζει το υπόλοιπο πριν από ανεξάρτητη έγκριση.';
      onSaved();
    }catch(ex){if(owner===selected)error=ex instanceof Error?ex.message:'Σφάλμα διόρθωσης.';}
    finally{busy=false;}
  }

  async function reviewReversal(event:SubmitEvent){
    event.preventDefault();
    const selected=owner,actor=sessionId,decision=approve==='approve';
    const reversal=openReviews.find(x=>x.id===reviewId);
    if(!canReview||!canWrite||reviewing||!reversal||!clean(reviewNote,12,180)){
      error='Επιλέξτε διόρθωση άλλου χρήστη, που δεν εγκρίνατε αρχικά, και τεκμηριώστε την απόφαση.';
      return;
    }
    if(decision&&!window.confirm(
      'Επαληθεύσατε ανεξάρτητα το λάθος; Η έγκριση θα αλλάξει μόνο την χειροκίνητη λογιστική απεικόνιση και θα παραμείνει αμετάκλητη.'))
      return;
    reviewing=true;error='';notice='';
    try{
      const response=await fetch(
        '/api/admin/finance/supplier-reversals/'+encodeURIComponent(reversal.id)+
        '/review?organization_id='+encodeURIComponent(selected),{
          method:'POST',credentials:'same-origin',cache:'no-store',
          headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
          body:JSON.stringify({approve:decision,review_note:reviewNote})
        });
      if(owner!==selected||sessionId!==actor)return;
      if(!response.ok)throw new Error(response.status===403
        ?'Ο ανεξάρτητος έλεγχος δεν επιτρέπεται στον λογαριασμό σας.'
        :response.status===409?'Η διόρθωση έχει ήδη κριθεί ή υπάρχει προηγούμενη εγκεκριμένη αναστροφή.'
        :'Δεν επιβεβαιώθηκε η απόφαση. Ανανεώστε το μητρώο.');
      const raw:unknown=await response.json();
      if(!raw||typeof raw!=='object'||Array.isArray(raw))throw new Error('Μη έγκυρη απόκριση.');
      const result=raw as Record<string,unknown>;
      if(result.id!==reversal.id||result.organization_id!==selected||
        result.status!==(decision?'approved_manual':'rejected')||
        result.bank_verified!==false||result.bank_refund_recorded!==false||
        result.posted_to_general_ledger!==false)
        throw new Error('Η ανεξάρτητη απόφαση δεν επαληθεύτηκε.');
      reviewId='';reviewNote='';
      notice=decision?'Η διόρθωση εγκρίθηκε χειροκίνητα και το υπόλοιπο επανυπολογίστηκε. Δεν έγινε τραπεζική κίνηση.'
        :'Το αίτημα απορρίφθηκε χωρίς μεταβολή στο υπόλοιπο.';
      onSaved();
    }catch(ex){if(owner===selected)error=ex instanceof Error?ex.message:'Σφάλμα έγκρισης.';}
    finally{reviewing=false;}
  }
</script>

<section class="supplier-reversal-panel" data-testid="supplier-approval-reversals">
  <h3>Διορθώσεις λανθασμένων εγκρίσεων</h3>
  <p class="admin-muted">Ασφαλής αναστροφή της χειροκίνητης επίδρασης εγκεκριμένου
    τεκμηρίου πληρωμής ή πιστωτικού. Τα αρχικά στοιχεία και οι προηγούμενες
    αποφάσεις δεν σβήνονται. Δεν αποτελεί ακύρωση τραπεζικής συναλλαγής,
    επιστροφή χρημάτων, φορολογική πίστωση ή διπλογραφική εγγραφή.</p>
  {#if canWrite}
    <h4>Νέα αίτηση διόρθωσης</h4>
    <form class="reversal-form" onsubmit={requestReversal}>
      <label for="reversal-type">Είδος αρχικής έγκρισης</label>
      <select id="reversal-type" bind:value={targetKind}>
        <option value="payment">Χειροκίνητο αποδεικτικό πληρωμής</option>
        <option value="credit">Πιστωτικό προμηθευτή</option>
      </select>
      <label for="reversal-original">Ενεργή χειροκίνητη έγκριση</label>
      <select id="reversal-original" required bind:value={targetId}>
        <option value="">Επιλέξτε συγκεκριμένη έγκριση</option>
        {#each validTargets.filter(t=>!pendingTargets.has(targetKind+':'+t.id)) as item (item.id)}
          <option value={item.id}>{euro(item.amount_cents)} · {item.id.slice(0,8)}</option>
        {/each}
      </select>
      <label for="reversal-reason">Ακριβής λόγος διόρθωσης</label>
      <input id="reversal-reason" required minlength="12" maxlength="180"
        bind:value={reason} placeholder="Π.χ. διπλή έγκριση ίδιου αποδεικτικού"/>
      <button type="submit" disabled={busy}>
        {busy?'Υποβολή…':'Αίτηση ανεξάρτητου ελέγχου'}
      </button>
    </form>
  {/if}
  <h4>Ιστορικό διορθώσεων</h4>
  {#if reversals.length}
    <ul class="reversal-list">
      {#each reversals as item (item.id)}
        <li>
          <strong>{item.target_kind==='payment'?'Τεκμήριο πληρωμής':'Πιστωτικό'} · {item.target_id.slice(0,8)}</strong>
          <span>{item.status==='pending'?'Προς ανεξάρτητο έλεγχο':
            item.status==='approved_manual'?'Εγκεκριμένη χειροκίνητη αναστροφή':'Απόρριψη διόρθωσης'}</span>
          <small>Αιτία: {item.reason}
            {#if item.review_note} · Έλεγχος: {item.review_note}{/if}</small>
        </li>
      {/each}
    </ul>
    {#if reversals.length===200}
      <p class="admin-muted">Εμφανίζονται οι τελευταίες 200 αιτήσεις για τα τιμολόγια αυτού του μήνα.</p>
    {/if}
  {:else}
    <p>Δεν υπάρχουν αιτήσεις αναστροφής εγκρίσεων σε αυτά τα τιμολόγια.</p>
  {/if}
  {#if canReview&&canWrite}
    <h4>Απόφαση ανεξάρτητου Super Admin</h4>
    {#if openReviews.length}
      <form class="reversal-form" onsubmit={reviewReversal}>
        <label for="reversal-review-target">Αίτηση άλλου χρήστη · διαφορετικός από αρχικό εγκρίνοντα</label>
        <select id="reversal-review-target" required bind:value={reviewId}>
          <option value="">Επιλέξτε εκκρεμή αίτηση</option>
          {#each openReviews as item (item.id)}
            <option value={item.id}>{item.target_kind==='payment'?'Πληρωμή':'Πιστωτικό'} · {item.reason}</option>
          {/each}
        </select>
        <label for="reversal-review-decision">Απόφαση</label>
        <select id="reversal-review-decision" bind:value={approve}>
          <option value="approve">Έγκριση χειροκίνητης αναστροφής</option>
          <option value="reject">Απόρριψη</option>
        </select>
        <label for="reversal-review-note">Επαληθευμένος λόγος και τεκμηρίωση ελέγχου</label>
        <input id="reversal-review-note" required minlength="12" maxlength="180"
          bind:value={reviewNote}/>
        <button type="submit" disabled={reviewing}>
          {reviewing?'Έλεγχος…':'Οριστική απόφαση διόρθωσης'}
        </button>
      </form>
    {:else}
      <p>Δεν υπάρχουν εκκρεμείς αιτήσεις που μπορείτε να εξετάσετε ανεξάρτητα.</p>
    {/if}
  {/if}
  {#if notice}<p role="status">{notice}</p>{/if}
  {#if error}<p role="alert">{error}</p>{/if}
</section>
<style>
  .supplier-reversal-panel{margin-top:1.5rem;border-top:1px solid var(--border-color,#9ca3af);
    padding-top:1.25rem}
  .reversal-form{display:grid;gap:.55rem;max-width:40rem;margin:1rem 0}
  .reversal-form input,.reversal-form select{padding:.75rem;min-height:44px;
    border:1px solid var(--border-color,#9ca3af);border-radius:.55rem;
    background:var(--surface,#fff);color:inherit;max-width:100%}
  .reversal-form button{min-height:44px;padding:.75rem}
  .reversal-list{list-style:none;padding:0;display:grid;gap:.65rem}
  .reversal-list li{display:grid;gap:.35rem;border:1px solid var(--border-color,#9ca3af);
    border-radius:.55rem;padding:.85rem;overflow-wrap:anywhere}
  .reversal-list small,.reversal-list span{opacity:.8}
</style>
