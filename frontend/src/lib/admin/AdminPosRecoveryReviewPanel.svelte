<script lang="ts">
  import {browser} from '$app/env';

  type Owner='lefteria_fc'|'lefteria_fc_academy';
  type Disposition='balanced_confirmed'|'variance_flagged'|'count_disputed';
  type PendingShift={
    shift_id:string;register_id:string;register_label:string;location_name:string;
    closed_at:string;cashier_name:string;recovery_actor_id:string;
    disposition:Disposition|null;
  };
  type Queue={organization_id:Owner;shifts:PendingShift[];total:number};
  let {owner,actorId,refresh,onExpired,onRevoked}:{
    owner:Owner;actorId:string;refresh:number;
    onExpired:()=>void;onRevoked:()=>void;
  }=$props();

  let queue=$state<Queue|null>(null);
  let loading=$state(false);
  let writing=$state(false);
  let error=$state('');
  let notice=$state('');
  let selectedId=$state('');
  let secondCount=$state('');
  let confirmed=$state(false);
  let refreshLocal=$state(0);
  let requestId=$state('');
  let fingerprint=$state('');
  const selected=$derived(queue?.shifts.find(s=>s.shift_id===selectedId));
  const uuid=(v:unknown)=>typeof v==='string'&&
    /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(v);
  const validId=(v:unknown)=>typeof v==='string'&&/^[A-Za-z0-9_-]{1,80}$/.test(v);
  const countCents=(value:string):number|null=>{
    if(!/^\d{1,6}(?:[.,]\d{1,2})?$/.test(value))return null;
    const [whole,dec='']=value.replace(',','.').split('.');
    const cents=Number(whole)*100+Number(dec.padEnd(2,'0'));
    return Number.isSafeInteger(cents)&&cents>=0&&cents<=100000000?cents:null;
  };
  const euro=(cents:number)=>new Intl.NumberFormat('el-CY',{
    style:'currency',currency:'EUR'}).format(cents/100);
  const date=(v:string)=>new Intl.DateTimeFormat('el-CY',{
    dateStyle:'short',timeStyle:'short'}).format(new Date(v));
  const status=(value:Disposition|null)=>({
    balanced_confirmed:'Επιβεβαιώθηκε χωρίς διαφορά',
    variance_flagged:'Επιβεβαιώθηκε με οικονομική διαφορά',
    count_disputed:'Διαφωνία στην καταμέτρηση · ταμείο σε αναστολή'
  } as Record<Disposition,string>)[value as Disposition]??'Αναμονή δεύτερης καταμέτρησης';
  const validate=(raw:unknown,expected:Owner):raw is Queue=>{
    if(!raw||typeof raw!=='object'||Array.isArray(raw))return false;
    const v=raw as Record<string,unknown>;
    return v.organization_id===expected&&Number.isSafeInteger(v.total)&&
      Number(v.total)>=0&&Array.isArray(v.shifts)&&v.shifts.length<=60&&
      v.shifts.every((item:unknown)=>{
        if(!item||typeof item!=='object'||Array.isArray(item))return false;
        const s=item as Record<string,unknown>;
        return uuid(s.shift_id)&&validId(s.register_id)&&
          typeof s.register_label==='string'&&typeof s.location_name==='string'&&
          typeof s.cashier_name==='string'&&uuid(s.recovery_actor_id)&&
          typeof s.closed_at==='string'&&!Number.isNaN(Date.parse(s.closed_at))&&
          (s.disposition===null||
            ['balanced_confirmed','variance_flagged','count_disputed'].includes(String(s.disposition)));
      });
  };

  $effect(()=>{
    const expectedOwner=owner,expectedActor=actorId;
    void refresh;void refreshLocal;
    queue=null;error='';loading=false;
    if(!browser||!expectedActor)return;
    const abort=new AbortController();
    loading=true;
    void (async()=>{
      try{
        const response=await fetch('/api/admin/pos/sites/recovery-review-queue?'+
          new URLSearchParams({organization_id:expectedOwner}),{
          credentials:'same-origin',cache:'no-store',signal:abort.signal
        });
        if(abort.signal.aborted)return;
        if(response.status===401){onExpired();return;}
        if(response.status===403){onRevoked();return;}
        if(!response.ok)throw Error('Δεν είναι διαθέσιμες οι ανακτήσεις βάρδιας.');
        const raw:unknown=await response.json();
        if(!validate(raw,expectedOwner))throw Error('Μη επαληθευμένες βάρδιες.');
        if(!abort.signal.aborted&&owner===expectedOwner&&actorId===expectedActor){
          queue=raw;
          if(!raw.shifts.some(s=>s.shift_id===selectedId))
            selectedId=raw.shifts.find(s=>s.disposition===null)?.shift_id??'';
        }
      }catch(e){
        if(!abort.signal.aborted&&owner===expectedOwner&&actorId===expectedActor)
          error=e instanceof Error?e.message:'Σφάλμα λίστας βαρδιών.';
      }finally{if(!abort.signal.aborted)loading=false;}
    })();
    return()=>abort.abort();
  });

  async function submitCount(){
    const shift=selected;
    const counted=countCents(secondCount);
    if(!browser||writing||!shift||shift.disposition!==null||
      shift.recovery_actor_id===actorId||!confirmed||counted===null)return;
    const expectedOwner=owner,expectedActor=actorId,shiftId=shift.shift_id;
    const nextFingerprint=JSON.stringify([expectedOwner,expectedActor,shiftId,counted]);
    if(nextFingerprint!==fingerprint||!uuid(requestId)){
      requestId=crypto.randomUUID();
      fingerprint=nextFingerprint;
    }
    const currentRequest=requestId;
    writing=true;error='';notice='';
    try{
      const response=await fetch('/api/admin/pos/sites/shifts/'+
        encodeURIComponent(shiftId)+'/independent-count?'+
        new URLSearchParams({organization_id:expectedOwner}),{
          method:'POST',credentials:'same-origin',cache:'no-store',
          headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
          body:JSON.stringify({requestId:currentRequest,
            independentlyCountedCashCents:counted})
        });
      if(owner!==expectedOwner||actorId!==expectedActor||selectedId!==shiftId)return;
      if(response.status===401){onExpired();return;}
      if(!response.ok)throw Error(response.status===403
        ?'Η δεύτερη καταμέτρηση πρέπει να γίνει από διαφορετικό Super Admin.'
        :response.status===409?'Η βάρδια έχει ήδη ελεγχθεί ή έχει αλλάξει.'
        :'Η ανεξάρτητη καταμέτρηση απορρίφθηκε.');
      const raw:unknown=await response.json();
      if(!raw||typeof raw!=='object'||Array.isArray(raw))
        throw Error('Μη επαληθευμένη επιβεβαίωση.');
      const v=raw as Record<string,unknown>;
      if(v.organization_id!==expectedOwner||v.shift_id!==shiftId||
        v.register_id!==shift.register_id||v.reviewer_id!==expectedActor||
        v.second_count_cents!==counted||
        !Number.isSafeInteger(v.first_count_cents)||
        !Number.isSafeInteger(v.expected_cash_cents)||
        !Number.isSafeInteger(v.variance_cents)||
        v.variance_cents!==Number(v.first_count_cents)-Number(v.expected_cash_cents)||
        v.difference_between_counts_cents!==counted-Number(v.first_count_cents)||
        !['balanced_confirmed','variance_flagged','count_disputed'].includes(String(v.disposition))||
        v.terminal_hold!==(v.disposition==='count_disputed')||
        v.requires_investigation!==(v.disposition!=='balanced_confirmed')||
        typeof v.replay!=='boolean')
        throw Error('Απορρίφθηκε μη έγκυρο αποτέλεσμα οικονομικού ελέγχου.');
      const result=v.disposition as Disposition;
      notice=status(result)+'. Πρώτη μέτρηση: '+euro(Number(v.first_count_cents))+
        ', δεύτερη: '+euro(counted)+', απόκλιση βιβλίου: '+euro(Number(v.variance_cents))+
        '. '+(v.requires_investigation?'Χρειάζεται έρευνα, χωρίς αυτόματη τακτοποίηση.':
          'Ο διπλός έλεγχος συμφωνεί με το βιβλίο.');
      secondCount='';confirmed=false;fingerprint='';requestId='';
      refreshLocal++;
    }catch(e){
      if(owner===expectedOwner&&actorId===expectedActor)
        error=e instanceof Error?e.message:'Η καταμέτρηση απέτυχε.';
    }finally{writing=false;}
  }
</script>

<section class="admin-panel review-panel" data-testid="pos-independent-recovery-count">
  <div class="review-head">
    <div>
      <p class="admin-eyebrow">POS · ΔΥΟ ΥΠΕΥΘΥΝΟΙ · ΕΛΕΓΧΟΣ ΜΕΤΡΗΤΩΝ</p>
      <h3>5. Ανεξάρτητη δεύτερη καταμέτρηση</h3>
      <p class="admin-muted">Διαφορετικός Super Admin καταμετρά τα ίδια φυσικά μετρητά.
        Το πρώτο ποσό παραμένει κρυφό μέχρι να καταχωριστεί η δεύτερη μέτρηση.
        Η διαφωνία δεσμεύει το συγκεκριμένο ταμείο.</p>
    </div>
    <button type="button" class="admin-secondary" onclick={()=>refreshLocal++}
      disabled={loading||writing}>Ανανέωση</button>
  </div>
  {#if loading}<p role="status">Έλεγχος βαρδιών ανά οργανισμό…</p>{/if}
  {#if error}<p role="alert">{error}</p>{/if}
  {#if notice}<p role="status">{notice}</p>{/if}
  {#if queue}
    <p class="admin-muted">{queue.total} βάρδιες έκτακτου κλεισίματος στον επιλεγμένο οργανισμό.</p>
    {#if queue.shifts.length}
      <label>Επιλογή βάρδιας
        <select bind:value={selectedId} disabled={writing}>
          <option value="">Επιλέξτε βάρδια</option>
          {#each queue.shifts as shift (shift.shift_id)}
            <option value={shift.shift_id}>
              {shift.location_name} · {shift.register_label} · {date(shift.closed_at)}
              · {status(shift.disposition)}
            </option>
          {/each}
        </select>
      </label>
    {/if}
    {#if selected}
      <p class="admin-muted">{selected.location_name} · {selected.register_label}
        · ταμίας {selected.cashier_name}. {status(selected.disposition)}.</p>
      {#if selected.disposition===null&&selected.recovery_actor_id===actorId}
        <p role="status">Εσείς καταχωρίσατε το πρώτο κλείσιμο. Απαιτείται
          δεύτερος, διαφορετικός Super Admin.</p>
      {:else if selected.disposition===null}
        <form class="review-count-form" onsubmit={event=>{event.preventDefault();void submitCount();}}>
          <label>Ανεξάρτητα μετρημένα μετρητά (€)
            <input type="text" inputmode="decimal" maxlength="10"
              autocomplete="off" bind:value={secondCount} placeholder="π.χ. 12,00"
              required disabled={writing}/>
          </label>
          <label class="review-confirm">
            <input type="checkbox" bind:checked={confirmed} disabled={writing}/>
            Επιβεβαιώνω ότι πραγματοποίησα αυτοτελή φυσική καταμέτρηση, χωρίς
            να βασιστώ στην πρώτη μέτρηση.
          </label>
          <button type="submit" class="admin-secondary"
            disabled={writing||!confirmed||countCents(secondCount)===null}>
            {writing?'Καταχώριση…':'Επιβεβαίωση δεύτερης καταμέτρησης'}
          </button>
        </form>
      {/if}
    {:else if queue.total===0}
      <p class="admin-muted">Δεν υπάρχουν βάρδιες προς έλεγχο.</p>
    {/if}
  {/if}
</section>

<style>
  .review-panel{display:grid;gap:12px;margin-top:18px}
  .review-head{display:flex;justify-content:space-between;align-items:start;flex-wrap:wrap;gap:12px}
  .review-panel h3{margin:0}
  .review-panel label{display:grid;gap:7px;max-width:500px;font-size:.88rem}
  .review-panel select,.review-panel input[type=text]{
    padding:11px 12px;border:1px solid #57514b;border-radius:9px;
    background:#161616;color:#f6f1e8;width:100%
  }
  .review-count-form{display:grid;gap:12px;max-width:500px}
  .review-confirm{display:flex!important;flex-direction:row;align-items:start;gap:10px}
  .review-confirm input{min-width:auto;margin-top:4px}
</style>
