<script lang="ts">
  import { untrack } from 'svelte';

  type Fee={
    id:string;player_id:string;child_name:string;fee_month:string;amount_cents:number;
    currency:'EUR';status:'issued'|'paid'|'review_required';payer_email:string;
    payer_first_name:string;payer_last_name:string;
    payment_request_id:string|null;payment_status:string;checkout_url:string|null;
    sms_status:'reserved'|'accepted'|'unknown'|null;
  };
  let { owner,configured,canWrite,sessionId }:{
    owner:string|null;configured:boolean;canWrite:boolean;sessionId:string
  }=$props();
  let fees=$state<Fee[]>([]);
  let status=$state<'idle'|'loading'|'ready'|'error'>('idle');
  let pending=$state(false);
  let message=$state('');
  let refresh=$state(0);
  let playerId=$state('');
  let month=$state('');
  let amount=$state('');
  let email=$state('');
  let first=$state('');
  let last=$state('');
  let issuingId=$state<string|null>(null);
  let requestId=$state<string|null>(null);
  let sendingId=$state<string|null>(null);
  let attemptIds=$state<Record<string,string>>({});

  const money=(cents:number)=>new Intl.NumberFormat('el-CY',{style:'currency',currency:'EUR'}).format(cents/100);
  const secure=(value:unknown):value is string=>{
    if(typeof value!=='string'||value.length>2048)return false;
    try{const url=new URL(value);return url.protocol==='https:'&&!url.username&&!url.password&&url.port===''&&
      (url.hostname==='sandbox.payabl.com'||url.hostname==='pay4.sandbox.payabl.com');}
    catch{return false;}
  };
  function validFee(value:unknown):value is Fee{
    if(!value||typeof value!=='object'||Array.isArray(value))return false;
    const f=value as Record<string,unknown>;
    return typeof f.id==='string'&&/^[0-9a-f-]{36}$/i.test(f.id)&&
      typeof f.player_id==='string'&&typeof f.child_name==='string'&&
      typeof f.fee_month==='string'&&/^20\d{2}-(0[1-9]|1[0-2])-01$/.test(f.fee_month)&&
      typeof f.amount_cents==='number'&&Number.isSafeInteger(f.amount_cents)&&f.amount_cents>0&&
      f.amount_cents<=100000000&&f.currency==='EUR'&&
      (f.status==='issued'||f.status==='paid'||f.status==='review_required')&&
      typeof f.payer_email==='string'&&typeof f.payer_first_name==='string'&&
      typeof f.payer_last_name==='string'&&typeof f.payment_status==='string'&&
      (f.payment_request_id===null||typeof f.payment_request_id==='string')&&
      (f.checkout_url===null||secure(f.checkout_url))&&
      (f.sms_status===null||f.sms_status==='reserved'||f.sms_status==='accepted'||f.sms_status==='unknown');
  }
  $effect(()=>{
    const selected=owner;const user=sessionId;void user;void refresh;
    status='idle';fees=[];message='';
    if(selected!=='lefteria_fc_academy')return;
    let active=true;
    status='loading';
    untrack(()=>{void(async()=>{
      try{
        const response=await fetch('/api/admin/finance/payabl/training-fees/?organization_id='+encodeURIComponent(selected),{
          cache:'no-store',credentials:'same-origin'
        });
        if(!response.ok)throw new Error('Δεν είναι διαθέσιμος ο κατάλογος μηνών.');
        const raw:unknown=await response.json();
        if(!raw||typeof raw!=='object'||Array.isArray(raw)||
          !('organization_id' in raw)||raw.organization_id!==selected||
          !('fees' in raw)||!Array.isArray(raw.fees)||raw.fees.length>100||!raw.fees.every(validFee))
          throw new Error('Μη επαληθευμένα στοιχεία τιμολόγησης.');
        if(!active)return;
        fees=raw.fees;status='ready';
      }catch(error){if(active){status='error';message=error instanceof Error?error.message:'Σφάλμα';}}
    })();});
    return()=>{active=false;};
  });

  function cents(value:string):number|null{
    if(!/^\d{1,6}(?:[.,]\d{1,2})?$/.test(value))return null;
    const [whole,fraction='']=value.replace(',','.').split('.');
    const total=Number(whole)*100+Number(fraction.padEnd(2,'0'));
    return Number.isSafeInteger(total)&&total>=1&&total<=100000000?total:null;
  }
  async function post(path:string,body:unknown):Promise<Record<string,unknown>>{
    const response=await fetch(path,{
      method:'POST',cache:'no-store',credentials:'same-origin',
      headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
      body:JSON.stringify(body)
    });
    if(!response.ok)throw new Error(
      response.status===409?'Ο μήνας ή το αίτημα έχει ήδη καταχωριστεί διαφορετικά. Ελέγξτε τις εγγραφές.':
      'Η ενέργεια δεν επιβεβαιώθηκε. Ελέγξτε τις εγγραφές πριν την επανάληψη.');
    const raw:unknown=await response.json();
    if(!raw||typeof raw!=='object'||Array.isArray(raw))throw new Error('Μη έγκυρη απάντηση.');
    return raw as Record<string,unknown>;
  }
  async function createCheckout(fee:Fee|Record<string,unknown>){
    if(!configured||!canWrite||owner!=='lefteria_fc_academy')return;
    if(typeof fee.id!=='string'||typeof fee.fee_month!=='string'||
      typeof fee.amount_cents!=='number'||typeof fee.payer_email!=='string'||
      typeof fee.payer_first_name!=='string'||typeof fee.payer_last_name!=='string')
      throw new Error('Μη επαληθευμένη χρέωση.');
    if(fee.payment_request_id)throw new Error('Υπάρχει ήδη αίτημα πληρωμής. Πρέπει να ελεγχθεί.');
    const origin=owner;
    pending=true;message='';
    requestId??=crypto.randomUUID();
    try{
      const output=await post('/api/admin/finance/payabl/payment-requests?organization_id='+origin,{
        client_request_id:requestId,
        fee_issue_id:fee.id,
        amount_cents:fee.amount_cents,currency:'EUR',
        purpose:'Academy training '+fee.fee_month.slice(0,7),
        payer_email:fee.payer_email,payer_first_name:fee.payer_first_name,
        payer_last_name:fee.payer_last_name
      });
      if(owner!==origin)return;
      if(output.status!=='ready'||!secure(output.checkout_url))
        throw new Error('Η κατάσταση δημιουργίας συνδέσμου απαιτεί έλεγχο.');
      requestId=null;
      message='Ο σύνδεσμος δημιουργήθηκε, αλλά δεν σημαίνει εξόφληση. Μπορείτε να τον αποστείλετε με SMS.';
      refresh+=1;
    }finally{pending=false;}
  }

  async function issue(event:SubmitEvent){
    event.preventDefault();
    if(!canWrite||pending||owner!=='lefteria_fc_academy')return;
    const total=cents(amount);
    if(!total||!/^20\d{2}-(0[1-9]|1[0-2])$/.test(month)||
       playerId.length<1||playerId.length>128||
       !/^[\x20-\x7e]{3,50}$/.test(email)||!email.includes('@')||
       !/^[\x20-\x7e]{1,50}$/.test(first)||!/^[\x20-\x7e]{1,50}$/.test(last)){
      message='Συμπληρώστε έναν μήνα, επαληθευμένο ID παιδιού και δοκιμαστικά στοιχεία πληρωτή στα λατινικά.';
      return;
    }
    issuingId??=crypto.randomUUID();
    pending=true;message='';
    try{
      const result=await post('/api/admin/finance/payabl/training-fees/?organization_id='+owner,{
        client_request_id:issuingId,player_id:playerId,fee_month:month,
        amount_cents:total,payer_email:email,payer_first_name:first,payer_last_name:last
      });
      if(typeof result.id!=='string'||result.amount_cents!==total)
        throw new Error('Δεν επαληθεύτηκε η έκδοση του μήνα.');
      issuingId=null;
      message=result.created===true?'Ο μήνας εκδόθηκε επιτυχώς.':'Αυτός ο μήνας υπάρχει ήδη και δεν εκδόθηκε ξανά.';
      if(configured&&result.created===true){
        pending=false;
        await createCheckout(result);
      }
      refresh+=1;
    }catch(error){message=error instanceof Error?error.message:'Η έκδοση δεν επιβεβαιώθηκε.';}
    finally{pending=false;}
  }

  async function sendSms(fee:Fee){
    if(!canWrite||!configured||pending||sendingId||owner!=='lefteria_fc_academy'||
       fee.status!=='issued'||fee.payment_status!=='ready'||!secure(fee.checkout_url)||
       fee.sms_status!==null)return;
    sendingId=fee.id;message='';
    attemptIds[fee.id]??=crypto.randomUUID();
    try{
      const output=await post('/api/admin/finance/payabl/training-fees/'+fee.id+
        '/sms?organization_id='+owner,{client_request_id:attemptIds[fee.id]});
      if(output.state!=='accepted')throw new Error('Η κατάσταση SMS απαιτεί έλεγχο· μην επαναλάβετε αυτόματα.');
      delete attemptIds[fee.id];
      message='Το Twilio αποδέχτηκε το SMS για αποστολή. Η παράδοση δεν έχει ακόμη επιβεβαιωθεί.';
      refresh+=1;
    }catch(error){message=error instanceof Error?error.message:'Το SMS δεν επιβεβαιώθηκε.';}
    finally{sendingId=null;}
  }
</script>

{#if owner==='lefteria_fc_academy'}
  <section class="admin-panel academy-fees" data-testid="academy-issued-training-fees">
    <h2>Μηνιαίες χρεώσεις Ακαδημίας · ανά παιδί</h2>
    <p>Εκδίδετε μόνο τους μήνες που επιλέγετε. Δεν δημιουργούνται αυτόματα 12 χρεώσεις.
      Κάθε μήνας αντιστοιχίζεται στον επαληθευμένο γονέα και στο συγκεκριμένο παιδί.</p>
    {#if canWrite}
      <form class="fee-form" onsubmit={issue}>
        <label for="academy-fee-player">ID παιδιού από το Ρόστερ Ακαδημίας</label>
        <input id="academy-fee-player" bind:value={playerId} maxlength="128" required placeholder="Επαληθευμένο ID παιδιού"/>
        <label for="academy-fee-month">Μήνας προπόνησης</label>
        <input id="academy-fee-month" type="month" bind:value={month} required min="2020-01" max="2100-12"/>
        <label for="academy-fee-amount">Ποσό σε EUR</label>
        <input id="academy-fee-amount" bind:value={amount} required placeholder="40.00" inputmode="decimal"/>
        <label for="academy-fee-email">Email γονέα (για πληρωμή Payabl.)</label>
        <input id="academy-fee-email" type="email" bind:value={email} required maxlength="50" placeholder="test@example.com"/>
        <label for="academy-fee-first">Όνομα στα λατινικά</label>
        <input id="academy-fee-first" bind:value={first} required maxlength="50" placeholder="Test"/>
        <label for="academy-fee-last">Επώνυμο στα λατινικά</label>
        <input id="academy-fee-last" bind:value={last} required maxlength="50" placeholder="Parent"/>
        <button type="submit" disabled={pending}>{pending?'Καταχώριση…':'Έκδοση επιλεγμένου μήνα'}</button>
      </form>
      <p class="admin-muted">Η έκδοση συνδέσμου θα εκτελεστεί αυτόματα μετά την έκδοση του μήνα
        όταν το Payabl sandbox είναι ενεργό. SMS στέλνεται μόνο με ρητή ενέργεια.</p>
    {/if}
    {#if message}<p role="status">{message}</p>{/if}
    {#if status==='loading'}<p role="status">Ανάκτηση χρεώσεων Ακαδημίας…</p>
    {:else if status==='error'}<p role="alert">{message}</p>
    {:else if status==='ready'}
      {#if fees.length===0}<p>Δεν υπάρχουν χρεώσεις μήνα για την Ακαδημία.</p>
      {:else}
        <ul class="fee-list">
        {#each fees as fee (fee.id)}
          <li>
            <div><strong>{fee.child_name} · {fee.fee_month.slice(0,7)}</strong>
              <small>ID: {fee.player_id}</small>
              <span>{money(fee.amount_cents)} · {fee.status==='paid'?'Εξοφλημένο':
                fee.status==='review_required'?'Χρειάζεται συμφωνία':
                fee.payment_status==='awaiting_settlement'?'Αναμονή εκκαθάρισης':
                fee.payment_status==='ready'?'Έτοιμο για πληρωμή':'Οφειλή'}</span>
              <small>Πληρωτής: {fee.payer_email}</small>
            </div>
            <div class="fee-actions">
              {#if fee.status==='issued'&&fee.payment_status==='ready'&&secure(fee.checkout_url)}
                <a href={fee.checkout_url} target="_blank" rel="noopener noreferrer">Σύνδεσμος πληρωμής</a>
                {#if canWrite&&fee.sms_status===null}
                  <button type="button" disabled={sendingId!==null||pending}
                    onclick={()=>void sendSms(fee)}>
                    {sendingId===fee.id?'Αποστολή…':'Αποστολή συνδέσμου με Twilio SMS'}
                  </button>
                {:else if fee.sms_status==='accepted'}
                  <small>SMS αποδεκτό από Twilio · όχι επιβεβαιωμένη παράδοση</small>
                {:else if fee.sms_status}
                  <small>SMS: χρειάζεται έλεγχος πριν από επανάληψη</small>
                {/if}
              {:else if canWrite&&configured&&fee.status==='issued'&&fee.payment_request_id===null}
                <button type="button" disabled={pending}
                  onclick={()=>void createCheckout(fee).catch(error=>{
                    message=error instanceof Error?error.message:'Σφάλμα δημιουργίας συνδέσμου';
                  })}>Δημιουργία συνδέσμου πληρωμής</button>
              {:else if fee.status==='issued'&&fee.payment_status==='initiating'}
                <small>Εκκρεμεί αρχικοποίηση · ελέγξτε την συναλλαγή, όχι νέα απόπειρα</small>
              {/if}
            </div>
          </li>
        {/each}
        </ul>
      {/if}
      <button type="button" class="admin-secondary" onclick={()=>refresh+=1}>Ανανέωση χρεώσεων</button>
    {/if}
  </section>
{/if}

<style>
  .academy-fees{margin-top:18px}
  .fee-form{display:grid;grid-template-columns:minmax(0,1fr);gap:8px;max-width:34rem;margin:20px 0}
  .fee-form label{font-size:14px;font-weight:650}
  .fee-form input{padding:11px 13px;background:var(--surface,#fff);color:inherit;
    border:1px solid var(--border-color,#999);border-radius:8px}
  .fee-form button,.fee-actions button{border:1px solid #9a792e;border-radius:8px;padding:11px 15px;cursor:pointer}
  .fee-form button{background:#cba75d;color:#0b1010;font-weight:700}
  .fee-form button:disabled,.fee-actions button:disabled{opacity:.5;cursor:not-allowed}
  .fee-list{padding:0;list-style:none;display:grid;gap:10px}
  .fee-list li{display:flex;flex-wrap:wrap;align-items:center;justify-content:space-between;
    gap:15px;border:1px solid #8e825b55;border-radius:12px;padding:15px}
  .fee-list li>div{display:grid;gap:6px}
  .fee-list span{font-weight:650}.fee-list small{opacity:.75}
  .fee-actions{display:flex;flex-direction:column;gap:8px;align-items:flex-start}
  .fee-actions a{font-weight:700}
</style>
