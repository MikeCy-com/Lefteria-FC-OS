<script lang="ts">
  import {untrack} from 'svelte';

  type Bill={id:string;supplier_name:string;invoice_reference:string;
    bill_date:string;remaining_cents:number};
  type Credit={id:string;bill_id:string;credit_reference:string;
    credit_date:string;amount_cents:number;reason:string;
    requested_by:string;requested_at:string;
    status:'pending'|'approved_manual'|'rejected';
    reviewed_at:string|null;review_note:string|null};
  let {owner,sessionId,canWrite,canReview,bills,credits,onSaved}:{
    owner:string;sessionId:string;canWrite:boolean;canReview:boolean;
    bills:Bill[];credits:Credit[];onSaved:()=>void
  }=$props();
  const dateParts=new Intl.DateTimeFormat('en-GB',{timeZone:'Asia/Nicosia',
    year:'numeric',month:'2-digit',day:'2-digit'}).formatToParts(new Date());
  const part=(name:string)=>dateParts.find(x=>x.type===name)?.value??'';
  const today=part('year')+'-'+part('month')+'-'+part('day');
  let creditDate=$state(today);
  let billId=$state('');
  let reference=$state('');
  let reason=$state('');
  let amount=$state('');
  let requestId=$state<string|null>(null);
  let saving=$state(false);
  let reviewing=$state(false);
  let error=$state('');
  let notice=$state('');
  let approvalId=$state('');
  let reviewNote=$state('');
  let decision=$state<'approve'|'reject'>('approve');
  const euro=(n:number)=>new Intl.NumberFormat('el-CY',
    {style:'currency',currency:'EUR'}).format(n/100);
  const uuid=(s:unknown)=>typeof s==='string'&&
    /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(s);
  const clean=(s:string,min:number,max:number)=>s.length>=min&&s.length<=max&&
    s.trim()===s&&![...s].some(x=>/\p{Cc}/u.test(x));
  const available=$derived(credits.filter(c=>c.status==='pending'&&
    c.requested_by!==sessionId));
  const money=(s:string):number|null=>{
    if(!/^\d{1,7}([.,]\d{1,2})?$/.test(s))return null;
    const [whole,part='']=s.replace(',','.').split('.');
    const n=Number(whole)*100+Number(part.padEnd(2,'0'));
    return Number.isSafeInteger(n)&&n>0&&n<=1000000000?n:null;
  };
  $effect(()=>{
    void owner;void sessionId;
    billId='';requestId=null;approvalId='';notice='';error='';
  });

  async function submit(e:SubmitEvent){
    e.preventDefault();
    const selected=owner,actor=sessionId,sum=money(amount);
    const bill=bills.find(b=>b.id===billId);
    if(!canWrite||saving||!bill||sum===null||sum>bill.remaining_cents||
      !clean(reference,4,80)||!clean(reason,8,180)||
      !/^\d{4}-\d{2}-\d{2}$/.test(creditDate)||
      creditDate<bill.bill_date||creditDate>today){
      error='Επιβεβαιώστε αριθμό πιστωτικού, ποσό, ημερομηνία, αιτιολογία και ανεξόφλητο υπόλοιπο.';
      return;
    }
    requestId??=crypto.randomUUID();
    const key=requestId;
    saving=true;error='';notice='';
    try{
      const result=await fetch(
        '/api/admin/finance/supplier-credits?organization_id='+encodeURIComponent(selected),{
          method:'POST',credentials:'same-origin',cache:'no-store',
          headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
          body:JSON.stringify({client_request_id:key,bill_id:bill.id,
            credit_reference:reference,credit_date:creditDate,
            amount_cents:sum,reason})
        });
      if(owner!==selected||sessionId!==actor)return;
      if(result.status===409)requestId=null;
      if(!result.ok)throw new Error(result.status===403?'Η πρόσβαση ανακλήθηκε.':
        result.status===409?'Το πιστωτικό υπάρχει με άλλη αναφορά ή διαφορετικά στοιχεία.':
        'Η υποβολή δεν επαληθεύτηκε. Ελέγξτε το ιστορικό.');
      const raw:unknown=await result.json();
      if(!raw||typeof raw!=='object'||Array.isArray(raw)||
        (raw as Record<string,unknown>).organization_id!==selected||
        (raw as Record<string,unknown>).bill_id!==bill.id||
        !uuid((raw as Record<string,unknown>).id)||
        (raw as Record<string,unknown>).bank_refund_recorded!==false||
        (raw as Record<string,unknown>).posted_to_general_ledger!==false||
        !['pending','approved_manual','rejected'].includes(
          String((raw as Record<string,unknown>).status)))
        throw new Error('Μη επαληθευμένη απόκριση πιστωτικού.');
      requestId=null;reference='';reason='';amount='';
      notice='Το πιστωτικό καταχωρίστηκε. Η μείωση οφειλής απαιτεί δεύτερο έλεγχο.';
      onSaved();
    }catch(ex){if(owner===selected)error=ex instanceof Error?ex.message:'Σφάλμα πιστωτικού.';}
    finally{saving=false;}
  }
  async function review(e:SubmitEvent){
    e.preventDefault();
    const selected=owner,actor=sessionId,approve=decision==='approve';
    const credit=available.find(c=>c.id===approvalId);
    if(!canReview||!canWrite||reviewing||!credit||
      !clean(reviewNote,8,180)){
      error='Επιλέξτε πιστωτικό τρίτου χρήστη και καταγράψτε τον ανεξάρτητο έλεγχο.';
      return;
    }
    if(approve&&!window.confirm(
      'Επιβεβαιώσατε το πιστωτικό με τον προμηθευτή; Η έγκριση θα μειώσει την οφειλή και είναι αμετάκλητη.'))
      return;
    reviewing=true;error='';notice='';
    try{
      const result=await fetch(
        '/api/admin/finance/supplier-credits/'+encodeURIComponent(credit.id)+
        '/review?organization_id='+encodeURIComponent(selected),{
          method:'POST',credentials:'same-origin',cache:'no-store',
          headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
          body:JSON.stringify({approve,review_note:reviewNote})
        });
      if(owner!==selected||sessionId!==actor)return;
      if(!result.ok)throw new Error(result.status===403?'Δεν επιτρέπεται ο δεύτερος έλεγχος.':
        result.status===409?'Το πιστωτικό ξεπερνά την ανεξόφλητη οφειλή ή έχει ήδη κριθεί.':
        'Δεν επαληθεύτηκε η απόφαση. Ανανεώστε.');
      const raw:unknown=await result.json();
      if(!raw||typeof raw!=='object'||Array.isArray(raw)||
        (raw as Record<string,unknown>).id!==credit.id||
        (raw as Record<string,unknown>).organization_id!==selected||
        (raw as Record<string,unknown>).status!==
          (approve?'approved_manual':'rejected')||
        (raw as Record<string,unknown>).bank_refund_recorded!==false||
        (raw as Record<string,unknown>).posted_to_general_ledger!==false)
        throw new Error('Η απάντηση έγκρισης δεν επαληθεύτηκε.');
      approvalId='';reviewNote='';
      notice=approve?'Το πιστωτικό εγκρίθηκε χειροκίνητα και μειώνει τη μη εξοφλημένη υποχρέωση.':
        'Το αίτημα απορρίφθηκε χωρίς αλλαγή στην οφειλή.';
      onSaved();
    }catch(ex){if(owner===selected)error=ex instanceof Error?ex.message:'Σφάλμα έγκρισης.';}
    finally{reviewing=false;}
  }
</script>

<section class="supplier-credit-panel" data-testid="supplier-credit-corrections">
  <h3>Πιστωτικά προμηθευτών · διορθώσεις υποχρεώσεων</h3>
  <p class="admin-muted">Κάθε πιστωτικό συνδέεται με το αρχικό τιμολόγιο.
    Η υποβολή δεν αλλάζει το υπόλοιπο. Η ανεξάρτητη έγκριση μειώνει μόνο την
    χειροκίνητη οφειλή και ΔΕΝ επιβεβαιώνει επιστροφή χρημάτων ή ΦΠΑ.</p>
  {#if canWrite}
    <h4>Καταχώριση πιστωτικού</h4>
    <form class="credit-form" onsubmit={submit}>
      <label for="credit-bill">Αρχικό τιμολόγιο</label>
      <select id="credit-bill" bind:value={billId} required>
        <option value="">Επιλέξτε παραστατικό με οφειλή</option>
        {#each bills.filter(b=>b.remaining_cents>0) as bill (bill.id)}
          <option value={bill.id}>{bill.supplier_name} · {bill.invoice_reference} ·
            {euro(bill.remaining_cents)}</option>
        {/each}
      </select>
      <label for="credit-reference">Αριθμός πιστωτικού προμηθευτή</label>
      <input id="credit-reference" required minlength="4" maxlength="80" bind:value={reference}/>
      <label for="credit-date">Ημερομηνία πιστωτικού</label>
      <input id="credit-date" type="date" min="2020-01-01" max={today} required bind:value={creditDate}/>
      <label for="credit-amount">Ποσό πιστωτικού (EUR)</label>
      <input id="credit-amount" inputmode="decimal" required placeholder="25.00" bind:value={amount}/>
      <label for="credit-reason">Αιτιολογία πιστωτικού / επιστροφής</label>
      <input id="credit-reason" required minlength="8" maxlength="180" bind:value={reason}/>
      <button type="submit" disabled={saving}>
        {saving?'Καταχώριση…':'Υποβολή πιστωτικού για έγκριση'}
      </button>
    </form>
  {/if}
  <h4>Ιστορικό πιστωτικών</h4>
  {#if credits.length}
    <ul class="credit-list">
      {#each credits as credit (credit.id)}
        <li>
          <strong>{credit.credit_reference} · {euro(credit.amount_cents)}</strong>
          <span>{credit.status==='pending'?'Σε αναμονή ελέγχου':
            credit.status==='approved_manual'?'Εγκεκριμένο πιστωτικό':'Απορρίφθηκε'}
            · {credit.credit_date}</span>
          <small>{credit.reason}
            {#if credit.review_note} · Αιτιολογία ελεγκτή: {credit.review_note}{/if}</small>
        </li>
      {/each}
    </ul>
    {#if credits.length===200}
      <p class="admin-muted">Προβάλλονται τα 200 νεότερα πιστωτικά αυτής της περιόδου.</p>
    {/if}
  {:else}
    <p>Δεν υπάρχουν πιστωτικά για τα τιμολόγια αυτού του μήνα.</p>
  {/if}
  {#if canReview&&canWrite}
    <h4>Ανεξάρτητος έλεγχος Super Admin</h4>
    {#if available.length}
      <form class="credit-form" onsubmit={review}>
        <label for="credit-review-request">Πιστωτικό άλλου χρήστη</label>
        <select id="credit-review-request" bind:value={approvalId} required>
          <option value="">Επιλέξτε εκκρεμές πιστωτικό</option>
          {#each available as credit (credit.id)}
            <option value={credit.id}>{credit.credit_reference} · {euro(credit.amount_cents)}</option>
          {/each}
        </select>
        <label for="credit-review-decision">Απόφαση</label>
        <select id="credit-review-decision" bind:value={decision}>
          <option value="approve">Έγκριση πιστωτικού</option>
          <option value="reject">Απόρριψη</option>
        </select>
        <label for="credit-review-note">Τεκμηριωμένος έλεγχος προμηθευτή</label>
        <input id="credit-review-note" required minlength="8" maxlength="180" bind:value={reviewNote}/>
        <button type="submit" disabled={reviewing}>
          {reviewing?'Έλεγχος…':'Οριστική απόφαση πιστωτικού'}
        </button>
      </form>
    {:else}
      <p>Δεν υπάρχουν εκκρεμή πιστωτικά άλλου χρήστη.</p>
    {/if}
  {/if}
  {#if notice}<p role="status">{notice}</p>{/if}
  {#if error}<p role="alert">{error}</p>{/if}
</section>
<style>
  .supplier-credit-panel{margin-top:1.25rem;border-top:1px solid var(--border-color,#9ca3af);
    padding-top:1.25rem}
  .credit-form{display:grid;gap:.5rem;max-width:40rem;margin:1rem 0}
  .credit-form input,.credit-form select{padding:.7rem;min-height:44px;
    max-width:40rem;border:1px solid var(--border-color,#9ca3af);
    border-radius:.5rem;background:var(--surface,#fff);color:inherit}
  .credit-form button{min-height:44px;padding:.75rem}
  .credit-list{padding:0;list-style:none;display:grid;gap:.6rem}
  .credit-list li{display:grid;gap:.25rem;padding:.8rem;
    border:1px solid var(--border-color,#9ca3af);border-radius:.5rem}
  .credit-list span,.credit-list small{opacity:.8}
</style>
