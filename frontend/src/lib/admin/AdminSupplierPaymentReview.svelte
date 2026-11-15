<script lang="ts">
  import {untrack} from 'svelte';
  import AdminSupplierCredits from './AdminSupplierCredits.svelte';
  import AdminSupplierApprovalReversals from './AdminSupplierApprovalReversals.svelte';

  type Bill={
    id:string;supplier_name:string;invoice_reference:string;bill_date:string;
    gross_amount_cents:number;approved_manual_cents:number;
    approved_credit_cents:number;net_payable_cents:number;
    remaining_cents:number;pending_evidence_cents:number;
    city_id:string|null;location_id:string|null;
  };
  type Evidence={
    id:string;bill_id:string;amount_cents:number;payment_date:string;
    payment_method:'bank_transfer'|'cash'|'external_card';
    evidence_reference:string;requested_at:string;requested_by:string;
    status:'pending'|'approved_manual'|'rejected';
    reviewed_at:string|null;review_note:string|null;
    reviewed_by:string|null;reversed_manual:boolean;
  };
  type Credit={id:string;bill_id:string;credit_reference:string;
    credit_date:string;amount_cents:number;reason:string;
    requested_by:string;requested_at:string;
    status:'pending'|'approved_manual'|'rejected';
    reviewed_at:string|null;review_note:string|null;
    reviewed_by:string|null;reversed_manual:boolean;};
  type Reversal={id:string;target_kind:'payment'|'credit';
    target_id:string;reason:string;requested_by:string;requested_at:string;
    status:'pending'|'approved_manual'|'rejected';
    reviewed_by:string|null;reviewed_at:string|null;review_note:string|null};
  type PaymentRegister={
    organization_id:string;month:string;currency:'EUR';
    bank_verified:false;bank_refund_recorded:false;is_general_ledger:false;
    summary:{bill_count:number;obligations_cents:number;
      approved_manual_cents:number;approved_credit_cents:number;
      pending_credit_cents:number;net_payables_cents:number;
      remaining_cents:number;pending_evidence_cents:number};
    bills:Bill[];requests:Evidence[];credit_notes:Credit[];reversals:Reversal[];
  };
  let {owner,sessionId,canWrite,canReview}:{
    owner:string|null;sessionId:string;canWrite:boolean;canReview:boolean
  }=$props();
  const parts=new Intl.DateTimeFormat('en-GB',{timeZone:'Asia/Nicosia',
    year:'numeric',month:'2-digit',day:'2-digit'}).formatToParts(new Date());
  const part=(key:string)=>parts.find(x=>x.type===key)?.value??'';
  let month=$state(part('year')+'-'+part('month'));
  let paymentDate=$state(part('year')+'-'+part('month')+'-'+part('day'));
  let refresh=$state(0);
  let loading=$state(false);
  let saving=$state(false);
  let reviewing=$state(false);
  let error=$state('');
  let notice=$state('');
  let data=$state<PaymentRegister|null>(null);
  let billId=$state('');
  let amount=$state('');
  let method=$state<'bank_transfer'|'cash'|'external_card'>('bank_transfer');
  let evidence=$state('');
  let requestId=$state<string|null>(null);
  let reviewRequest=$state('');
  let reviewNote=$state('');
  let reviewApprove=$state('approve');
  const euro=(cents:number)=>new Intl.NumberFormat('el-CY',
    {style:'currency',currency:'EUR'}).format(cents/100);
  const uuid=(v:unknown)=>typeof v==='string'&&
    /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v);
  const safe=(v:unknown)=>typeof v==='number'&&
    Number.isSafeInteger(v)&&v>=0;
  const monthPattern=/^(20[2-9][0-9]|2100)-(0[1-9]|1[0-2])$/;
  const datePattern=/^\d{4}-\d{2}-\d{2}$/;
  const methods=['bank_transfer','cash','external_card'];
  function verified(raw:unknown,selected:string,period:string):raw is PaymentRegister{
    if(!raw||typeof raw!=='object'||Array.isArray(raw))return false;
    const r=raw as Record<string,unknown>;
    if(r.organization_id!==selected||r.month!==period+'-01'||
      r.currency!=='EUR'||r.bank_verified!==false||
      r.bank_refund_recorded!==false||
      r.is_general_ledger!==false||!r.summary||
      typeof r.summary!=='object'||Array.isArray(r.summary)||
      !['bill_count','obligations_cents','approved_manual_cents',
        'approved_credit_cents','pending_credit_cents','net_payables_cents',
        'remaining_cents','pending_evidence_cents'].every(key=>
        safe((r.summary as Record<string,unknown>)[key]))||
      !Array.isArray(r.bills)||r.bills.length>100||
      !Array.isArray(r.requests)||r.requests.length>200||
      !Array.isArray(r.credit_notes)||r.credit_notes.length>200||
      !Array.isArray(r.reversals)||r.reversals.length>200)return false;
    const summary=r.summary as Record<string,number>;
    if(summary.obligations_cents!==summary.approved_credit_cents+summary.net_payables_cents||
      summary.net_payables_cents!==summary.approved_manual_cents+summary.remaining_cents)
      return false;
    return r.bills.every((x:unknown)=>{
      if(!x||typeof x!=='object'||Array.isArray(x))return false;
      const b=x as Record<string,unknown>;
      return uuid(b.id)&&typeof b.supplier_name==='string'&&
        typeof b.invoice_reference==='string'&&
        typeof b.bill_date==='string'&&datePattern.test(b.bill_date)&&
        ['gross_amount_cents','approved_manual_cents','approved_credit_cents',
          'net_payable_cents','remaining_cents',
          'pending_evidence_cents'].every(k=>safe(b[k]))&&
        (b.city_id===null||uuid(b.city_id))&&
        (b.location_id===null||uuid(b.location_id))&&
        (b.gross_amount_cents as number)===(b.approved_credit_cents as number)+
          (b.net_payable_cents as number)&&
        (b.net_payable_cents as number)===(b.approved_manual_cents as number)+
          (b.remaining_cents as number);
    })&&r.requests.every((x:unknown)=>{
      if(!x||typeof x!=='object'||Array.isArray(x))return false;
      const a=x as Record<string,unknown>;
      return uuid(a.id)&&uuid(a.bill_id)&&safe(a.amount_cents)&&
        (a.amount_cents as number)>0&&uuid(a.requested_by)&&
        typeof a.payment_date==='string'&&datePattern.test(a.payment_date)&&
        typeof a.requested_at==='string'&&
        typeof a.evidence_reference==='string'&&methods.includes(String(a.payment_method))&&
        ['pending','approved_manual','rejected'].includes(String(a.status))&&
        (a.reviewed_at===null||typeof a.reviewed_at==='string')&&
        (a.review_note===null||typeof a.review_note==='string')&&
        (a.reviewed_by===null||uuid(a.reviewed_by))&&
        typeof a.reversed_manual==='boolean'&&
        (!a.reversed_manual||a.status==='approved_manual');
    })&&r.credit_notes.every((x:unknown)=>{
      if(!x||typeof x!=='object'||Array.isArray(x))return false;
      const a=x as Record<string,unknown>;
      return uuid(a.id)&&uuid(a.bill_id)&&uuid(a.requested_by)&&
        typeof a.credit_reference==='string'&&typeof a.reason==='string'&&
        typeof a.credit_date==='string'&&datePattern.test(a.credit_date)&&
        typeof a.requested_at==='string'&&safe(a.amount_cents)&&
        (a.amount_cents as number)>0&&
        ['pending','approved_manual','rejected'].includes(String(a.status))&&
        (a.reviewed_at===null||typeof a.reviewed_at==='string')&&
        (a.review_note===null||typeof a.review_note==='string')&&
        (a.reviewed_by===null||uuid(a.reviewed_by))&&
        typeof a.reversed_manual==='boolean'&&
        (!a.reversed_manual||a.status==='approved_manual');
    })&&r.reversals.every((x:unknown)=>{
      if(!x||typeof x!=='object'||Array.isArray(x))return false;
      const a=x as Record<string,unknown>;
      return uuid(a.id)&&uuid(a.target_id)&&uuid(a.requested_by)&&
        (a.target_kind==='payment'||a.target_kind==='credit')&&
        typeof a.reason==='string'&&typeof a.requested_at==='string'&&
        ['pending','approved_manual','rejected'].includes(String(a.status))&&
        (a.reviewed_by===null||uuid(a.reviewed_by))&&
        (a.reviewed_at===null||typeof a.reviewed_at==='string')&&
        (a.review_note===null||typeof a.review_note==='string');
    });
  }
  $effect(()=>{
    const selected=owner,actor=sessionId,period=month,reload=refresh;
    void actor;void reload;
    data=null;error='';loading=false;
    if(!selected||!monthPattern.test(period))return;
    const ctrl=new AbortController();
    loading=true;
    untrack(()=>{void(async()=>{
      try{
        const response=await fetch(
          '/api/admin/finance/supplier-payments?organization_id='+
          encodeURIComponent(selected)+'&month='+encodeURIComponent(period),{
            credentials:'same-origin',cache:'no-store',signal:ctrl.signal});
        if(!response.ok)throw new Error(response.status===403
          ?'Η πρόσβαση σε αυτό το λογιστικό μητρώο δεν επιτρέπεται.'
          :'Το μητρώο ελέγχων πληρωμής δεν είναι διαθέσιμο.');
        const raw:unknown=await response.json();
        if(!verified(raw,selected,period))throw new Error('Μη επαληθευμένη αναφορά.');
        if(!ctrl.signal.aborted&&owner===selected&&sessionId===actor&&month===period)
          data=raw;
      }catch(e){if(!ctrl.signal.aborted)error=e instanceof Error?e.message:'Αποτυχία φόρτωσης.';}
      finally{if(!ctrl.signal.aborted)loading=false;}
    })();});
    return()=>ctrl.abort();
  });
  $effect(()=>{
    void owner;
    billId='';requestId=null;reviewRequest='';notice='';error='';
  });
  const clean=(x:string,min:number,max:number)=>x.length>=min&&
    x.length<=max&&x.trim()===x&&!/[\u0000-\u001f\u007f]/.test(x);
  function cents(input:string):number|null{
    if(!/^\d{1,7}([.,]\d{1,2})?$/.test(input))return null;
    const [whole,frac='']=input.replace(',','.').split('.');
    const value=Number(whole)*100+Number(frac.padEnd(2,'0'));
    return Number.isSafeInteger(value)&&value>=1&&value<=1000000000?value:null;
  }

  async function submit(e:SubmitEvent){
    e.preventDefault();
    const selected=owner,actor=sessionId,sum=cents(amount);
    const bill=data?.bills.find(b=>b.id===billId);
    if(!selected||!canWrite||saving||!bill||sum===null||
      sum>bill.remaining_cents||!clean(evidence,6,140)||
      !datePattern.test(paymentDate)||paymentDate<bill.bill_date||
      paymentDate>part('year')+'-'+part('month')+'-'+part('day')){
      error='Ελέγξτε παραστατικό, υπόλοιπο, ημερομηνία και αποδεικτικό.';
      return;
    }
    requestId??=crypto.randomUUID();
    const key=requestId;
    saving=true;error='';notice='';
    try{
      const response=await fetch(
        '/api/admin/finance/supplier-payments?organization_id='+encodeURIComponent(selected),{
          method:'POST',credentials:'same-origin',cache:'no-store',
          headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
          body:JSON.stringify({client_request_id:key,bill_id:billId,amount_cents:sum,
            payment_date:paymentDate,payment_method:method,evidence_reference:evidence})
        });
      if(owner!==selected||sessionId!==actor)return;
      if(response.status===409)requestId=null;
      if(!response.ok)throw new Error(response.status===403?'Η πρόσβαση άλλαξε.':
        response.status===409?'Το αίτημα υπάρχει με διαφορετικά στοιχεία.':
        'Δεν επιβεβαιώθηκε η καταχώριση. Ελέγξτε πριν την επανάληψη.');
      const raw:unknown=await response.json();
      if(!raw||typeof raw!=='object'||Array.isArray(raw)||
        (raw as Record<string,unknown>).organization_id!==selected||
        (raw as Record<string,unknown>).bill_id!==billId||
        (raw as Record<string,unknown>).status!=='pending'||
        (raw as Record<string,unknown>).bank_verified!==false||
        (raw as Record<string,unknown>).posted_to_general_ledger!==false||
        !uuid((raw as Record<string,unknown>).id))
        throw new Error('Η απάντηση δεν επαληθεύτηκε.');
      requestId=null;amount='';evidence='';
      notice='Το τεκμήριο καταχωρίστηκε για ανεξάρτητη έγκριση. Δεν θεωρείται εξόφληση.';
      refresh++;
    }catch(ex){if(owner===selected)error=ex instanceof Error?ex.message:'Αποτυχία αποθήκευσης.';}
    finally{saving=false;}
  }

  async function review(e:SubmitEvent){
    e.preventDefault();
    const selected=owner,actor=sessionId,approval=reviewApprove==='approve';
    const request=data?.requests.find(r=>r.id===reviewRequest&&r.status==='pending');
    if(!selected||!canReview||reviewing||!request||request.requested_by===actor||
      !clean(reviewNote,8,180)){
      error='Επιλέξτε ανεξάρτητο, εκκρεμές αίτημα και τεκμηριώστε την απόφαση.';
      return;
    }
    if(approval&&!window.confirm(
      'Έχετε επαληθεύσει ανεξάρτητα το αποδεικτικό εκτός συστήματος; Η απόφαση θα είναι αμετάκλητη.'))
      return;
    reviewing=true;error='';notice='';
    try{
      const response=await fetch(
        '/api/admin/finance/supplier-payments/'+encodeURIComponent(request.id)+
        '/review?organization_id='+encodeURIComponent(selected),{
          method:'POST',credentials:'same-origin',cache:'no-store',
          headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
          body:JSON.stringify({approve:approval,review_note:reviewNote})
        });
      if(owner!==selected||sessionId!==actor)return;
      if(!response.ok)throw new Error(response.status===403
        ?'Η πρόσβαση ή ο κανόνας δεύτερου ελεγκτή δεν επιτρέπει την απόφαση.'
        :response.status===409?'Η απόφαση συγκρούεται με το υπόλοιπο ή παλαιότερη έγκριση.'
        :'Η έγκριση δεν επιβεβαιώθηκε. Ανανεώστε πριν επαναλάβετε.');
      const raw:unknown=await response.json();
      if(!raw||typeof raw!=='object'||Array.isArray(raw)||
        (raw as Record<string,unknown>).id!==request.id||
        (raw as Record<string,unknown>).organization_id!==selected||
        (raw as Record<string,unknown>).status!==
          (approval?'approved_manual':'rejected')||
        (raw as Record<string,unknown>).bank_verified!==false||
        (raw as Record<string,unknown>).posted_to_general_ledger!==false)
        throw new Error('Η επαλήθευση απόφασης απέτυχε.');
      reviewRequest='';reviewNote='';
      notice=approval?'Η χειροκίνητη καταχώριση εγκρίθηκε, όχι ως τραπεζική επιβεβαίωση.'
        :'Το αίτημα απορρίφθηκε χωρίς επίδραση στο υπόλοιπο.';
      refresh++;
    }catch(ex){if(owner===selected)error=ex instanceof Error?ex.message:'Αποτυχία ελέγχου.';}
    finally{reviewing=false;}
  }
  const pending=$derived(data?.requests.filter(r=>
    r.status==='pending'&&r.requested_by!==sessionId)??[]);
</script>

<section class="admin-panel" data-testid="supplier-payment-manual-review">
  <h2>Έλεγχος εξόφλησης προμηθευτών</h2>
  <p class="admin-muted">Καταχώριση στοιχείων εξόφλησης με δεύτερο ελεγκτή.
    Οι εγκεκριμένες χειροκίνητες εγγραφές ΔΕΝ σημαίνουν αυτόματη κίνηση χρημάτων,
    τραπεζική συμφωνία ή ενημέρωση διπλογραφικού καθολικού.</p>
  <label for="supplier-payment-month">Μήνας έκδοσης τιμολογίων</label>
  <input id="supplier-payment-month" type="month" min="2020-01" max="2100-12" bind:value={month}/>
  {#if loading}
    <p role="status">Φόρτωση τεκμηρίων και υπολοίπων…</p>
  {:else if error && !data}
    <p role="alert">{error}</p>
    <button type="button" class="admin-secondary" onclick={()=>refresh++}>Επανάληψη</button>
  {:else if data}
    <div class="settlement-cards">
      <div><strong>{euro(data.summary.obligations_cents)}</strong><span>Σύνολο τιμολογίων</span></div>
      <div><strong>{euro(data.summary.approved_credit_cents)}</strong><span>Εγκεκριμένα πιστωτικά</span></div>
      <div><strong>{euro(data.summary.net_payables_cents)}</strong><span>Καθαρή οφειλή μετά πιστωτικά</span></div>
      <div><strong>{euro(data.summary.approved_manual_cents)}</strong><span>Εγκεκριμένα χειροκίνητα αποδεικτικά</span></div>
      <div><strong>{euro(data.summary.remaining_cents)}</strong><span>Υπόλοιπο μετά τις εγκρίσεις</span></div>
      <div><strong>{euro(data.summary.pending_evidence_cents)}</strong><span>Τεκμήρια εξόφλησης προς έλεγχο</span></div>
      <div><strong>{euro(data.summary.pending_credit_cents)}</strong><span>Πιστωτικά προς έλεγχο (χωρίς μείωση οφειλής)</span></div>
    </div>
    {#if data.summary.bill_count>data.bills.length}
      <p class="admin-muted">Εμφανίζονται τα πρώτα {data.bills.length} τιμολόγια.
        Τα σύνολα περιλαμβάνουν όλα τα τιμολόγια της περιόδου.</p>
    {/if}
    {#if canWrite}
      <h3>Νέο τεκμήριο εξόφλησης</h3>
      <form class="settlement-form" onsubmit={submit}>
        <label for="supplier-payment-bill">Τιμολόγιο και διαθέσιμο υπόλοιπο</label>
        <select id="supplier-payment-bill" bind:value={billId} required>
          <option value="">Επιλέξτε παραστατικό</option>
          {#each data.bills.filter(b=>b.remaining_cents>0) as bill (bill.id)}
            <option value={bill.id}>{bill.supplier_name} · {bill.invoice_reference} ·
              {euro(bill.remaining_cents)}</option>
          {/each}
        </select>
        <label for="supplier-payment-amount">Ποσό αποδεικτικού (EUR)</label>
        <input id="supplier-payment-amount" required inputmode="decimal" bind:value={amount} placeholder="50.00"/>
        <label for="supplier-payment-date">Ημερομηνία αποδεικτικού</label>
        <input id="supplier-payment-date" type="date" required min="2020-01-01" max={part('year')+'-'+part('month')+'-'+part('day')} bind:value={paymentDate}/>
        <label for="supplier-payment-method">Τρόπος πληρωμής που δηλώνεται στο τεκμήριο</label>
        <select id="supplier-payment-method" bind:value={method}>
          <option value="bank_transfer">Τραπεζική μεταφορά</option>
          <option value="cash">Μετρητά · αποδεικτικό</option>
          <option value="external_card">Εξωτερική κάρτα</option>
        </select>
        <label for="supplier-payment-reference">Αριθμός αποδεικτικού ή αναφοράς</label>
        <input id="supplier-payment-reference" required minlength="6" maxlength="140"
          bind:value={evidence} placeholder="Αναφορά τραπεζικής συναλλαγής ή απόδειξης"/>
        <button type="submit" disabled={saving}>{saving?'Καταχώριση…':'Υποβολή για δεύτερο έλεγχο'}</button>
      </form>
    {/if}
    <h3>Ιστορικό αιτημάτων ελέγχου</h3>
    {#if data.requests.length===0}
      <p>Δεν υπάρχουν ακόμη υποβλημένα τεκμήρια για αυτά τα τιμολόγια.</p>
    {:else}
      <ul class="settlement-list">
        {#each data.requests as request (request.id)}
          <li>
            <strong>{data.bills.find(b=>b.id===request.bill_id)?.supplier_name??'Καταχωρισμένο τιμολόγιο'}
              · {euro(request.amount_cents)}</strong>
            <span>{request.reversed_manual?'Αναστράφηκε κατόπιν ανεξάρτητου ελέγχου':
              request.status==='pending'?'Προς ανεξάρτητο έλεγχο':
              request.status==='approved_manual'?'Εγκεκριμένο χειροκίνητα':'Απορρίφθηκε'}
              · {request.payment_date} · {request.payment_method}</span>
            <small>Αναφορά: {request.evidence_reference}
              {#if request.review_note} · Έλεγχος: {request.review_note}{/if}</small>
          </li>
        {/each}
      </ul>
      {#if data.requests.length===200}
        <p class="admin-muted">Προβάλλονται τα 200 νεότερα αιτήματα της περιόδου.</p>
      {/if}
    {/if}
    <AdminSupplierCredits owner={owner ?? ''} sessionId={sessionId} {canWrite} {canReview}
      bills={data.bills} credits={data.credit_notes} onSaved={()=>refresh++}/>
    <AdminSupplierApprovalReversals owner={owner ?? ''} sessionId={sessionId}
      {canWrite} {canReview} payments={data.requests} credits={data.credit_notes}
      reversals={data.reversals} onSaved={()=>refresh++}/>
    {#if canReview&&canWrite}
      <h3>Ανεξάρτητη απόφαση Super Admin</h3>
      <p class="admin-muted">Δεν μπορείτε να εγκρίνετε δικό σας αίτημα. Ελέγξτε το
        αποδεικτικό εκτός συστήματος πριν από την έγκριση.</p>
      {#if pending.length}
        <form class="settlement-form" onsubmit={review}>
          <label for="supplier-review-request">Τεκμήριο τρίτου χρήστη</label>
          <select id="supplier-review-request" bind:value={reviewRequest} required>
            <option value="">Επιλέξτε εκκρεμές</option>
            {#each pending as item (item.id)}
              <option value={item.id}>{item.evidence_reference} · {euro(item.amount_cents)}</option>
            {/each}
          </select>
          <label for="supplier-review-decision">Απόφαση</label>
          <select id="supplier-review-decision" bind:value={reviewApprove}>
            <option value="approve">Έγκριση χειροκίνητης καταχώρισης</option>
            <option value="reject">Απόρριψη</option>
          </select>
          <label for="supplier-review-note">Αιτιολογία και ανεξάρτητος έλεγχος</label>
          <input id="supplier-review-note" required minlength="8" maxlength="180"
            bind:value={reviewNote}/>
          <button type="submit" disabled={reviewing}>
            {reviewing?'Επαλήθευση…':'Καταχώριση τελικής απόφασης'}
          </button>
        </form>
      {:else}
        <p>Δεν υπάρχουν εκκρεμότητες άλλου χρήστη προς αξιολόγηση.</p>
      {/if}
    {/if}
  {/if}
  {#if notice}<p role="status">{notice}</p>{/if}
  {#if error && data}<p role="alert">{error}</p>{/if}
</section>

<style>
  .settlement-cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(12rem,1fr));
    gap:.7rem;margin:1rem 0}
  .settlement-cards>div{border:1px solid var(--border-color,#9ca3af);
    border-radius:.6rem;padding:.9rem;display:grid;gap:.35rem}
  .settlement-cards strong{font-size:1.2rem;font-variant-numeric:tabular-nums}
  .settlement-cards span,.settlement-list small{opacity:.8}
  .settlement-form{display:grid;gap:.5rem;max-width:40rem;margin:1rem 0}
  input,select{padding:.7rem;min-height:44px;border:1px solid var(--border-color,#9ca3af);
    border-radius:.5rem;background:var(--surface,#fff);color:inherit;max-width:40rem}
  .settlement-form button{padding:.75rem;min-height:44px}
  .settlement-list{padding:0;list-style:none;display:grid;gap:.7rem}
  .settlement-list li{display:grid;gap:.25rem;padding:.8rem;
    border:1px solid var(--border-color,#9ca3af);border-radius:.5rem}
</style>
