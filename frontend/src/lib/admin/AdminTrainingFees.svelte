<script lang="ts">
  import { untrack } from 'svelte';

  type Fee={
    id:string;player_id:string;child_name:string;fee_month:string;amount_cents:number;
    currency:'EUR';status:'issued'|'paid'|'review_required'|'cancelled';payer_email:string;
    cancelled_at:string|null;cancel_reason:string|null;cancel_eligible?:boolean;
    payer_first_name:string;payer_last_name:string;
    payment_request_id:string|null;payment_status:string;checkout_url:string|null;
    sms_status:'reserved'|'accepted'|'unknown'|null;
  };
  let { owner,configured,canWrite,sessionId,focusPlayerId='' }:{
    owner:string|null;configured:boolean;canWrite:boolean;sessionId:string;focusPlayerId?:string
  }=$props();
  type RosterPlayer={id:string;name:string};
  let rosterPlayers=$state<RosterPlayer[]>([]);
  let rosterError=$state(false);
  let fees=$state<Fee[]>([]);
  // A focused player gets a complete authorized per-child ledger, never a
  // client-side subset of the latest organization-wide invoices.
  const focus = $derived(/^[^\x00-\x1f\x7f]{1,128}$/.test(focusPlayerId)&&focusPlayerId===focusPlayerId.trim() ? focusPlayerId : '');
  const invalidFocus=$derived(focusPlayerId!==''&&!focus);
  type LifecycleReview={is_active:boolean;is_archived:boolean;is_transferred:boolean;group_memberships:number;recorded_guardian_bindings:number;unrevoked_guardian_permissions:number};
  function validLifecycle(value:unknown,active:boolean):value is LifecycleReview{
    if(!value||typeof value!=='object'||Array.isArray(value))return false;
    const r=value as Record<string,unknown>;
    return ['is_active','is_archived','is_transferred'].every(k=>typeof r[k]==='boolean')&&
      (r.is_active===true&&r.is_archived===false&&r.is_transferred===false)===active&&
      ['group_memberships','recorded_guardian_bindings','unrevoked_guardian_permissions'].every(k=>
        typeof r[k]==='number'&&Number.isSafeInteger(r[k])&&(r[k] as number)>=0);
  }
  let verifiedPlayer=$state<{id:string;name:string;active:boolean;month:string;lifecycle:LifecycleReview|null}|null>(null);
  const visibleFees = $derived(focus ? fees.filter(f=>f.player_id===focus) : fees);
  const focusName=$derived(verifiedPlayer?.id===focus?verifiedPlayer.name:'Επιλεγμένος παίκτης');
  const departureReview=$derived.by(()=>{
    const sum=(rows:Fee[])=>rows.reduce((total,row)=>total+row.amount_cents,0);
    const open=visibleFees.filter(row=>row.status==='issued'||row.status==='review_required');
    const eligible=open.filter(row=>row.cancel_eligible===true);
    const review=open.filter(row=>row.cancel_eligible!==true);
    return {open:sum(open),eligible:sum(eligible),eligibleCount:eligible.length,
      review:sum(review),reviewCount:review.length,
      paid:sum(visibleFees.filter(row=>row.status==='paid')),
      cancelled:sum(visibleFees.filter(row=>row.status==='cancelled'))};
  });
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
  let checkoutRequestIds=$state<Record<string,string>>({});
  let sendingId=$state<string|null>(null);
  let attemptIds=$state<Record<string,string>>({});
  let cashIds=$state<Record<string,string>>({});
  let cashBusy=$state<string|null>(null);
  let cancellationDraft=$state<string|null>(null);
  let cancellationReason=$state('');
  let batchReason=$state(''),batchConfirm=$state(''),batchBusy=$state(false);
  const batchFees=$derived(visibleFees.filter(row=>row.cancel_eligible===true&&row.status==='issued'&&row.player_id===focus));
  const batchKey=$derived(JSON.stringify([owner,sessionId,focus,batchFees.map(f=>f.id).sort(),batchReason.trim()]));
  let cancelling=$state<string|null>(null);
  let cancellationRequestIds=$state<Record<string,string>>({});
  // Eligibility is also checked transactionally in PostgreSQL. This
  // client-side hint never grants permission or authorizes cancellation.
  const localNow=new Intl.DateTimeFormat('en-GB',
    {timeZone:'Asia/Nicosia',year:'numeric',month:'2-digit'}).formatToParts(new Date());
  const currentFeeMonth=(localNow.find(p=>p.type==='year')?.value??'')+'-'+
    (localNow.find(p=>p.type==='month')?.value??'')+'-01';
  let downloading=$state(false);
  let selectedInvoiceIds=$state<string[]>([]);

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
      typeof f.fee_month==='string'&&/^(?:20\d{2}|2100)-(0[1-9]|1[0-2])-01$/.test(f.fee_month)&&
      typeof f.amount_cents==='number'&&Number.isSafeInteger(f.amount_cents)&&f.amount_cents>0&&
      f.amount_cents<=100000000&&f.currency==='EUR'&&
      (f.status==='issued'||f.status==='paid'||f.status==='review_required'||f.status==='cancelled')&&
       (f.cancelled_at===null || typeof f.cancelled_at==='string')&&
       (f.cancel_reason===null || typeof f.cancel_reason==='string')&&
      typeof f.payer_email==='string'&&typeof f.payer_first_name==='string'&&
      typeof f.payer_last_name==='string'&&typeof f.payment_status==='string'&&
      (f.payment_request_id===null||typeof f.payment_request_id==='string')&&
      (f.checkout_url===null||secure(f.checkout_url))&&
      (f.sms_status===null||f.sms_status==='reserved'||f.sms_status==='accepted'||f.sms_status==='unknown');
  }
  $effect(()=>{
    const selected=owner;const user=sessionId;const allowed=canWrite;
    void user;void focus;rosterPlayers=[];rosterError=false;
    if(selected!=='lefteria_fc_academy'||!allowed||focusPlayerId)return;
    let active=true;
    untrack(()=>{void(async()=>{
      try{
        const response=await fetch('/api/admin/football/players?organization_id='+
          encodeURIComponent(selected)+'&offset=0&limit=50',
          {credentials:'same-origin',cache:'no-store'});
        if(!response.ok)throw new Error('Roster unavailable');
        const result:unknown=await response.json();
        if(!result||typeof result!=='object'||Array.isArray(result))throw new Error('Bad roster');
        const data=result as Record<string,unknown>;
        if(data.organization_id!==selected||data.ready!==true||
          !Array.isArray(data.players)||data.players.length>50)
          throw new Error('Unverified roster');
        const players=data.players.filter((item:unknown):item is RosterPlayer=>{
          if(!item||typeof item!=='object'||Array.isArray(item))return false;
          const player=item as Record<string,unknown>;
          return typeof player.id==='string'&&player.id.length>=1&&player.id.length<=128&&
            typeof player.name==='string'&&player.name.length>0&&player.name.length<=200;
        });
        if(players.length!==data.players.length)throw new Error('Roster validation failed');
        if(active)rosterPlayers=players;
      }catch{if(active)rosterError=true;}
    })();});
    return()=>{active=false;};
  });

  $effect(()=>{
    // Change of finance organization or account invalidates any previous
    // success message or invoice selection; a normal list refresh does not.
    void owner;void sessionId;void focus;
    message='';selectedInvoiceIds=[];playerId=focus;issuingId=null;
    cancellationDraft=null;cancellationReason='';batchReason='';batchConfirm='';
  });

  $effect(()=>{
    const selected=owner;const user=sessionId;const target=focus;const bad=invalidFocus;void user;void refresh;
    status='idle';fees=[];verifiedPlayer=null;
    if(selected!=='lefteria_fc_academy')return;
    if(bad){status='error';message='Μη έγκυρος κωδικός αθλητή.';return;}
    let active=true;
    status='loading';
    untrack(()=>{void(async()=>{
      try{
        const response=await fetch('/api/admin/finance/payabl/training-fees/?'+new URLSearchParams({organization_id:selected,...(target?{player_id:target}:{})}).toString(),{
          cache:'no-store',credentials:'same-origin'
        });
        if(!response.ok)throw new Error('Δεν είναι διαθέσιμος ο κατάλογος μηνών.');
        const raw:unknown=await response.json();
        if(!raw||typeof raw!=='object'||Array.isArray(raw)||
          !('organization_id' in raw)||raw.organization_id!==selected||
          !('fees' in raw)||!Array.isArray(raw.fees)||raw.fees.length>(target?1000:100)||!raw.fees.every(validFee))
          throw new Error('Μη επαληθευμένα στοιχεία τιμολόγησης.');
        if(!active)return;
        if(target){
          const scoped=raw as Record<string,unknown>;
          if(scoped.player_id!==target||typeof scoped.player_name!=='string'||!scoped.player_name||
            scoped.player_name.length>200||typeof scoped.player_active!=='boolean'||
            typeof scoped.current_month!=='string'||!/^\d{4}-(0[1-9]|1[0-2])-01$/.test(scoped.current_month)||
            !raw.fees.every(f=>f.player_id===target&&typeof f.cancel_eligible==='boolean'))
            throw new Error('Μη επαληθευμένος πλήρης φάκελος αθλητή.');
          const lifecycle=scoped.lifecycle_review;
          if(lifecycle!==undefined&&!validLifecycle(lifecycle,scoped.player_active))
            throw Error('Μη επαληθευμένη κατάσταση αθλητή.');
          verifiedPlayer={id:target,name:scoped.player_name,active:scoped.player_active,month:scoped.current_month,
            lifecycle:validLifecycle(lifecycle,scoped.player_active)?lifecycle:null};
        }
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
    checkoutRequestIds[fee.id]??=crypto.randomUUID();
    try{
      const output=await post('/api/admin/finance/payabl/payment-requests?organization_id='+origin,{
        client_request_id:checkoutRequestIds[fee.id],
        fee_issue_id:fee.id,
        amount_cents:fee.amount_cents,currency:'EUR',
        purpose:'Academy training '+fee.fee_month.slice(0,7),
        payer_email:fee.payer_email,payer_first_name:fee.payer_first_name,
        payer_last_name:fee.payer_last_name
      });
      if(owner!==origin)return;
      if(output.status!=='ready'||!secure(output.checkout_url))
        throw new Error('Η κατάσταση δημιουργίας συνδέσμου απαιτεί έλεγχο.');
      delete checkoutRequestIds[fee.id];
      message='Ο σύνδεσμος δημιουργήθηκε, αλλά δεν σημαίνει εξόφληση. Μπορείτε να τον αποστείλετε με SMS.';
      refresh+=1;
    }finally{pending=false;}
  }

  async function issue(event:SubmitEvent){
    event.preventDefault();
    if(!canWrite||pending||owner!=='lefteria_fc_academy'||invalidFocus||
      (focus&&(verifiedPlayer?.id!==focus||!verifiedPlayer.active||playerId!==focus)))return;
    const total=cents(amount);
    if(!total||!/^(?:20\d{2}|2100)-(0[1-9]|1[0-2])$/.test(month)||
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
      refresh+=1;
    }catch(error){message=error instanceof Error?error.message:'Η έκδοση δεν επιβεβαιώθηκε.';}
    finally{pending=false;}
  }

  async function recordCash(fee:Fee) {
    if(!canWrite||pending||cashBusy||owner!=='lefteria_fc_academy'||
      fee.status!=='issued'||fee.payment_request_id!==null)return;
    if(!window.confirm('Επιβεβαιώστε ότι εισπράχθηκε ολόκληρο το ποσό '+
      money(fee.amount_cents)+' σε μετρητά για '+fee.child_name+
      ' ('+fee.fee_month.slice(0,7)+'). Η ενέργεια καταχωρεί απόδειξη είσπραξης.'))return;
    cashBusy=fee.id;message='';
    cashIds[fee.id]??=crypto.randomUUID();
    try{
      const result=await post('/api/admin/finance/payabl/training-fees/'+fee.id+
        '/cash?organization_id='+owner,{client_request_id:cashIds[fee.id],notes:''});
      if(result.status!=='paid'||result.payment_method!=='cash'||result.fee_issue_id!==fee.id)
        throw new Error('Η πληρωμή με μετρητά δεν επιβεβαιώθηκε.');
      delete cashIds[fee.id];
      message='Η είσπραξη μετρητών καταχωρίστηκε. Η απόδειξη είναι διαθέσιμη ως PDF.';
      refresh+=1;
    }catch(error){
      message=error instanceof Error?error.message:'Η είσπραξη απαιτεί έλεγχο.';
    }finally{cashBusy=null;}
  }


  function canCancelFuture(fee:Fee):boolean {
    return canWrite && fee.status==='issued' && fee.payment_request_id===null &&
      fee.sms_status===null && (focus?verifiedPlayer?.id===focus&&fee.player_id===focus&&fee.cancel_eligible===true:fee.fee_month>currentFeeMonth);
  }

  async function cancelPlayerFutureFees(){
    const reason=batchReason.trim(),target=focus,actor=sessionId,currentOwner=owner;
    const ids=batchFees.map(f=>f.id).sort();
    if(!canWrite||!target||verifiedPlayer?.id!==target||status!=='ready'||batchBusy||cancelling||pending||
      cashBusy||sendingId||!ids.length||ids.length>120||batchConfirm!==batchKey||
      reason.length<8||reason.length>240||/[\x00-\x1f\x7f]/.test(reason))return;
    if(!window.confirm('Ακύρωση '+ids.length+' επιλέξιμων μελλοντικών χρεώσεων για '+focusName+'; Δεν διαγράφονται πληρωμές.'))return;
    batchBusy=true;batchConfirm='';message='';
    try{
      const result=await post('/api/admin/finance/payabl/training-fees/cancel-player-future?organization_id=lefteria_fc_academy',
        {playerId:target,feeIds:ids,requestIds:ids.map(()=>crypto.randomUUID()),reason});
      if(owner!==currentOwner||sessionId!==actor||focus!==target)return;
      if(result.organization_id!==currentOwner||result.player_id!==target||!Array.isArray(result.fees)||
        result.fees.length!==ids.length||!result.fees.every((f:unknown)=>!!f&&typeof f==='object'&&
          'id' in f&&typeof f.id==='string'&&ids.includes(f.id)&&'player_id' in f&&f.player_id===target&&
          'status' in f&&f.status==='cancelled')||
        new Set(result.fees.map((f:{id:string})=>f.id)).size!==ids.length)
        throw Error('Η μαζική ακύρωση δεν επιβεβαιώθηκε.');
      batchReason='';message='Ακυρώθηκαν '+ids.length+' μελλοντικές χρεώσεις. Το ιστορικό παραμένει.';
    }catch(e){if(owner===currentOwner&&sessionId===actor&&focus===target)
      message='Ελέγξτε το ενημερωμένο ιστορικό πριν από νέα προσπάθεια. '+(e instanceof Error?e.message:'');}
    finally{batchBusy=false;if(owner===currentOwner&&sessionId===actor&&focus===target)refresh++;}
  }

  async function cancelFuture(fee:Fee) {
    const reason=cancellationReason.trim();
    if(!canCancelFuture(fee)||cancelling||batchBusy||pending||reason.length<8||
       reason.length>240||/[\x00-\x1f\x7f]/.test(reason)) {
      message='Συμπληρώστε συγκεκριμένη αιτιολογία 8–240 χαρακτήρων.';
      return;
    }
    if(!window.confirm('Ακύρωση ΜΟΝΟ της μελλοντικής, ανεξόφλητης χρέωσης '+fee.fee_month.slice(0,7)+
      ' για '+fee.child_name+'; Θα διατηρηθεί στο ιστορικό, χωρίς διαγραφή εισπράξεων;'))return;
    cancelling=fee.id;message='';
    cancellationRequestIds[fee.id]??=crypto.randomUUID();
    try {
      const result=await post('/api/admin/finance/payabl/training-fees/'+fee.id+
        '/cancel?organization_id=lefteria_fc_academy',{
          client_request_id:cancellationRequestIds[fee.id],reason
        });
      if(result.id!==fee.id||result.status!=='cancelled')
        throw new Error('Η ακύρωση δεν επαληθεύτηκε.');
      delete cancellationRequestIds[fee.id];
      cancellationDraft=null;cancellationReason='';
      selectedInvoiceIds=selectedInvoiceIds.filter(id=>id!==fee.id);
      message=result.replay===true?'Η ακύρωση είχε ήδη καταχωριστεί.':
        'Η μελλοντική χρέωση ακυρώθηκε. Το ιστορικό και οι εισπράξεις δεν διαγράφηκαν.';
      refresh+=1;
    }catch(error){
      message=error instanceof Error?error.message:'Απαιτείται οικονομικός έλεγχος πριν την ακύρωση.';
    }finally{cancelling=null;}
  }

  async function downloadInvoice(ids:string[]) {
    if(downloading||owner!=='lefteria_fc_academy'||ids.length<1||ids.length>20||
      ids.some(id=>!/^[0-9a-f-]{36}$/i.test(id)))return;
    downloading=true;message='';
    try {
      const path=ids.length===1
        ? '/api/admin/finance/payabl/invoices/'+ids[0]+'.pdf'
        : '/api/admin/finance/payabl/invoices/multi.pdf?ids='+encodeURIComponent(ids.join(','));
      const glue=path.includes('?')?'&':'?';
      const response=await fetch(path+glue+'organization_id=lefteria_fc_academy',
        {credentials:'same-origin',cache:'no-store',headers:{Accept:'application/pdf'}});
      if(!response.ok||!response.headers.get('content-type')?.startsWith('application/pdf'))
        throw new Error('Το επίσημο PDF δεν είναι διαθέσιμο.');
      const file=await response.blob();
      if(file.size<500||file.size>2_000_000)throw new Error('Μη έγκυρο μέγεθος PDF.');
      const signature=await file.slice(0,5).text();
      if(signature!=='%PDF-')throw new Error('Μη έγκυρο αρχείο PDF.');
      const url=URL.createObjectURL(file);
      const anchor=document.createElement('a');anchor.href=url;
      anchor.download=ids.length===1?'invoice-'+ids[0].slice(0,8)+'.pdf':
        'academy-invoices-'+ids.length+'.pdf';
      document.body.appendChild(anchor);anchor.click();anchor.remove();
      window.setTimeout(()=>URL.revokeObjectURL(url),30000);
    }catch(error){message=error instanceof Error?error.message:'Αδύνατη η λήψη PDF.';}
    finally{downloading=false;}
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
    {#if focus}
      <div class="fee-player-context" data-testid="admin-fee-player-context">
        <strong>Χρεώσεις: {focusName}</strong>
        <span>{visibleFees.length} {visibleFees.length===1?'χρέωση':'χρεώσεις'}</span>
        <a href="/admin/finance?organization_id=lefteria_fc_academy">Όλες οι χρεώσεις της Ακαδημίας ↗</a>
      </div>
    {/if}
    {#if focus&&status==='ready'&&verifiedPlayer?.id===focus}
      <section id="academy-departure-review" class="departure-review" data-testid="admin-player-departure-review">
        <h3>Οικονομικός έλεγχος αθλητή · πριν από αποχώρηση</h3>
        <p>Πλήρες ιστορικό μηνιαίων χρεώσεων · {verifiedPlayer.active?'Ενεργός αθλητής':'Μη ενεργός αθλητής'}. Δεν μεταβάλλεται η κατάσταση του ρόστερ.</p>
        {#if verifiedPlayer.lifecycle}
          <div class="departure-lifecycle" data-testid="admin-player-lifecycle-preflight">
            <h4>Κατάσταση ρόστερ & συνδέσεων</h4>
            <div class="departure-totals">
              <div><small>Κατάσταση</small><strong>{verifiedPlayer.lifecycle.is_transferred?'Μεταγραφή':verifiedPlayer.lifecycle.is_archived?'Αρχειοθετημένος':verifiedPlayer.lifecycle.is_active?'Ενεργός':'Ανενεργός'}</strong></div>
              <div><small>Διατηρημένες αναθέσεις τμημάτων</small><strong>{verifiedPlayer.lifecycle.group_memberships}</strong></div>
              <div><small>Καταγεγραμμένες συνδέσεις κηδεμόνα</small><strong>{verifiedPlayer.lifecycle.recorded_guardian_bindings}</strong></div>
              <div><small>Μη ανακληθείσες παραχωρήσεις κηδεμόνα</small><strong>{verifiedPlayer.lifecycle.unrevoked_guardian_permissions}</strong></div>
            </div>
            <p class="admin-muted">Οι αριθμοί δείχνουν αποθηκευμένες συνδέσεις, όχι επιβεβαιωμένη ενεργή πρόσβαση. Η αλλαγή κατάστασης και η επανενεργοποίηση απαιτούν ξεχωριστό έλεγχο.</p>
            {#if verifiedPlayer.active}
              <a class="admin-secondary" href={'/admin/groups?'+new URLSearchParams({organization_id:'lefteria_fc_academy',view:'players',player_id:focus,section:'profile'}).toString()}>Επιστροφή στον φάκελο αθλητή / κηδεμόνα ›</a>
            {/if}
          </div>
        {:else}
          <p role="status">Ο έλεγχος κατάστασης και συνδέσεων δεν είναι διαθέσιμος σε αυτήν την έκδοση.</p>
        {/if}
        <div class="departure-totals">
          <div><small>Ανεξόφλητες χρεώσεις / σε έλεγχο</small><strong>{money(departureReview.open)}</strong></div>
          <div><small>Μελλοντικές προς ακύρωση ({departureReview.eligibleCount})</small><strong>{money(departureReview.eligible)}</strong></div>
          <div><small>Απαιτούν ξεχωριστό έλεγχο ({departureReview.reviewCount})</small><strong>{money(departureReview.review)}</strong></div>
          <div><small>Πληρωμένες</small><strong>{money(departureReview.paid)}</strong></div>
          <div><small>Ήδη ακυρωμένες</small><strong>{money(departureReview.cancelled)}</strong></div>
        </div>
        {#if canWrite&&batchFees.length}
          <div class="departure-lifecycle" data-testid="admin-player-batch-future-fees">
            <h4>Μαζική ακύρωση επιλέξιμων μηνών</h4>
            <p>{batchFees.length} χρεώσεις · {money(departureReview.eligible)}. {batchFees.map(f=>f.fee_month.slice(0,7)).join(', ')}</p>
            <label>Αιτιολογία <textarea bind:value={batchReason} rows="2" maxlength="240" disabled={batchBusy}/></label>
            <label><input type="checkbox" checked={batchConfirm===batchKey} disabled={batchBusy||batchReason.trim().length<8}
              onchange={e=>batchConfirm=e.currentTarget.checked?batchKey:''}/> Επιβεβαιώνω την ακύρωση μόνο των παραπάνω μηνών.</label>
            <button type="button" class="admin-secondary" disabled={batchBusy||!!cancelling||pending||!!cashBusy||!!sendingId||batchConfirm!==batchKey||batchFees.length>120}
              onclick={()=>void cancelPlayerFutureFees()}>{batchBusy?'Έλεγχος…':'Ακύρωση επιλέξιμων χρεώσεων'}</button>
            <p class="admin-muted">Αν μία χρέωση δεν είναι πλέον επιλέξιμη, η συναλλαγή απορρίπτεται συνολικά.</p>
          </div>
        {/if}
        <p>Ακυρώστε μόνο επιλέξιμους μελλοντικούς μήνες με τις υπάρχουσες ενέργειες. Οι πληρωμένες, τρέχουσες ή συνδεδεμένες με πληρωμή χρεώσεις δεν διαγράφονται. Ελέγξτε επίσης την πρόσβαση κηδεμόνων πριν από αποχώρηση ή επανενεργοποίηση.</p>
      </section>
    {/if}
    <p>Οι ακυρωμένες μελλοντικές χρεώσεις παραμένουν στο ιστορικό, δεν
      υπολογίζονται ως ενεργές οφειλές και δεν επηρεάζουν τις εισπράξεις.</p>
    <p>Εκδίδετε μόνο τους μήνες που επιλέγετε. Δεν δημιουργούνται αυτόματα 12 χρεώσεις.
      Κάθε μήνας αντιστοιχίζεται στον επαληθευμένο γονέα και στο συγκεκριμένο παιδί.</p>
    {#if canWrite&&!invalidFocus&&(!focus||(status==='ready'&&verifiedPlayer?.id===focus&&verifiedPlayer.active))}
      <form class="fee-form" onsubmit={issue}>
        <label for="academy-fee-player">ID παιδιού από το Ρόστερ Ακαδημίας</label>
        <input id="academy-fee-player" bind:value={playerId} maxlength="128" required readonly={!!focus}
          list="academy-fee-player-options" placeholder="Επιλογή παιδιού από το ρόστερ"/>
        <datalist id="academy-fee-player-options">
          {#each rosterPlayers as player (player.id)}
            <option value={player.id}>{player.name}</option>
          {/each}
        </datalist>
        {#if !focus&&rosterError}
          <small>Το ρόστερ δεν φορτώθηκε. Χρησιμοποιήστε το ID από το Ρόστερ Ακαδημίας.</small>
        {:else if !focus&&rosterPlayers.length===50}
          <small>Εμφανίζονται οι πρώτοι 50 παίκτες. Για επιπλέον παιδιά,
            χρησιμοποιήστε το ID από το Ρόστερ Ακαδημίας.</small>
        {/if}
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
      <p class="admin-muted">Μετά την έκδοση, επιλέγετε μετρητά ή σύνδεσμο πληρωμής με κάρτα.
        Η δημιουργία κάρτας δεν γίνεται αυτόματα, ώστε να μη μπλοκάρεται η
        παραλαβή μετρητών. SMS στέλνεται μόνο με ρητή ενέργεια.</p>
    {/if}
    {#if message}<p role="status">{message}</p>{/if}
    {#if status==='loading'}<p role="status">Ανάκτηση χρεώσεων Ακαδημίας…</p>
    {:else if status==='error'}<p role="alert">{message}</p>
    {:else if status==='ready'}
      {#if visibleFees.length===0}<p>{focus?'Δεν υπάρχουν καταχωρισμένες χρεώσεις για αυτόν τον παίκτη.':'Δεν υπάρχουν χρεώσεις μήνα για την Ακαδημία.'}</p>
      {:else}
        <ul class="fee-list">
        {#each visibleFees as fee (fee.id)}
          <li>
            <label class="fee-choice"><input type="checkbox"
              checked={selectedInvoiceIds.includes(fee.id)}
              disabled={downloading||fee.status==='cancelled'||(!selectedInvoiceIds.includes(fee.id)&&selectedInvoiceIds.length>=20)}
              onchange={event=>{
                selectedInvoiceIds=event.currentTarget.checked
                  ? [...selectedInvoiceIds,fee.id]
                  : selectedInvoiceIds.filter(id=>id!==fee.id);
              }} aria-label={'Επιλογή παραστατικού '+fee.child_name+' '+fee.fee_month.slice(0,7)}/></label>
            <div><strong>{fee.child_name} · {fee.fee_month.slice(0,7)}</strong>
              <small>ID: {fee.player_id}</small>
              <span>{money(fee.amount_cents)} · {fee.status==='paid'?'Εξοφλημένο':
                fee.status==='review_required'?'Χρειάζεται συμφωνία':
                fee.status==='cancelled'?'Ακυρωμένη · δεν είναι οφειλή':
                fee.payment_status==='awaiting_settlement'?'Αναμονή εκκαθάρισης':
                fee.payment_status==='ready'?'Έτοιμο για πληρωμή':'Οφειλή'}</span>
              <small>Πληρωτής: {fee.payer_email}</small>
              {#if fee.status==='cancelled'}
                <small data-testid="admin-fee-cancellation-audit">
                  Αιτιολογία: {fee.cancel_reason ?? 'Δεν υπάρχει'} ·
                  Καταχώριση: {fee.cancelled_at ? new Date(fee.cancelled_at).toLocaleDateString('el-CY') : '—'}
                </small>
              {/if}
            </div>
            <div class="fee-actions">
              <button type="button" class="fee-pdf" disabled={downloading||fee.status==='cancelled'}
                onclick={()=>void downloadInvoice([fee.id])}
                data-testid="admin-download-invoice">Λήψη PDF παραστατικού</button>
              {#if canCancelFuture(fee)}
                {#if cancellationDraft===fee.id}
                  <div class="fee-cancel-editor" data-testid="admin-fee-cancel-editor">
                    <label for={'future-fee-cancel-'+fee.id}>Αιτιολογία ακύρωσης</label>
                    <textarea id={'future-fee-cancel-'+fee.id} bind:value={cancellationReason}
                      maxlength="240" rows="3" placeholder="Π.χ. Αποχώρηση αθλητή από την Ακαδημία"></textarea>
                    <p>Ακυρώνει μόνο τον συγκεκριμένο μελλοντικό μήνα. Δεν διαγράφει ιστορικό και δεν επιστρέφει χρήματα.</p>
                    <button type="button" disabled={cancelling!==null||pending||
                        cancellationReason.trim().length<8}
                      onclick={()=>void cancelFuture(fee)}>
                      {cancelling===fee.id?'Ακύρωση…':'Επιβεβαίωση ακύρωσης'}
                    </button>
                    <button type="button" class="admin-secondary" disabled={cancelling!==null}
                      onclick={()=>{cancellationDraft=null;cancellationReason='';}}>Επιστροφή</button>
                  </div>
                {:else}
                  <button type="button" class="admin-secondary"
                    data-testid="admin-cancel-future-fee" disabled={cancelling!==null||pending}
                    onclick={()=>{cancellationDraft=fee.id;cancellationReason='';}}>
                    Ακύρωση μελλοντικής χρέωσης
                  </button>
                {/if}
              {/if}
              {#if fee.status==='issued'&&fee.payment_request_id===null&&canWrite}
                <button type="button" disabled={cashBusy!==null||pending}
                  data-testid="admin-record-cash"
                  onclick={()=>void recordCash(fee)}>
                  {cashBusy===fee.id?'Καταχώριση…':'Καταχώριση μετρητών'}
                </button>
              {/if}
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
      {#if selectedInvoiceIds.length>0}
        <button type="button" class="admin-secondary" disabled={downloading}
          onclick={()=>void downloadInvoice(selectedInvoiceIds)}
          data-testid="admin-download-multi-invoice">
          Λήψη ενιαίου PDF · {selectedInvoiceIds.length} παραστατικά
        </button>
      {/if}
      <button type="button" class="admin-secondary" onclick={()=>refresh+=1}>Ανανέωση χρεώσεων</button>
    {/if}
  </section>
{/if}

<style>
  .academy-fees{margin-top:18px}
  .fee-player-context{display:flex;gap:12px;flex-wrap:wrap;align-items:center;
    margin:16px 0;padding:13px;border:1px solid #937538;border-radius:7px;background:#f6bb3810}
  .fee-player-context strong{font-size:17px;color:#ffca46}
  .fee-player-context span{color:#ddd;font-size:13px}
  .fee-player-context a{margin-left:auto;color:#ffca46;font-weight:700;min-height:44px;display:inline-flex;align-items:center}
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
  .fee-choice input{width:17px;height:17px;cursor:pointer}
  .fee-cancel-editor{display:grid;gap:8px;border:1px solid var(--border-color,#827553);
    border-radius:9px;padding:12px;max-width:25rem}
  .fee-cancel-editor textarea{min-height:88px;background:var(--surface,#171a1c);
    color:inherit;border:1px solid var(--border-color,#777);border-radius:7px;padding:10px}
  .fee-cancel-editor p{font-size:12px;margin:0}
  .fee-pdf{background:transparent;color:inherit}
  .departure-review{display:grid;gap:12px;padding:14px;border:1px solid var(--admin-line,#555);border-radius:8px;margin:12px 0}
  .departure-review h3,.departure-review p{margin:0}
  .departure-lifecycle{display:grid;gap:10px;padding:12px;border:1px solid var(--admin-line);border-radius:8px}
  .departure-lifecycle h4{margin:0}
  .departure-lifecycle a{justify-self:start}
  .departure-totals{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,180px),1fr));gap:10px}
  .departure-totals>div{display:grid;gap:6px;border:1px solid var(--admin-line,#555);padding:10px;border-radius:6px}
</style>
