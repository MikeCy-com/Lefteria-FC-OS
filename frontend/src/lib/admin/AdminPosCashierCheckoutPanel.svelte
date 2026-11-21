<script lang="ts">
  import {browser} from '$app/env';
  type Owner='lefteria_fc'|'lefteria_fc_academy';
  type Item={product_id:string;variant_id:string;product_name:string;
    variant_name:string;price_cents:number;available:number};
  type Stock={organization_id:Owner;register_id:string;shift_id:string;
    location_id:string;location_name:string;fiscal_policies_approved:boolean;
    limit:150;items:Item[]};
  type Prepared={sale_id:string;total_cents:number};
  type Done={receipt_id:string;receipt_number:number;total_cents:number;
    change_cents:number};

  let {owner,actorId,registerId,shiftId,onExpired,onFinished}:{
    owner:Owner;actorId:string;registerId:string;shiftId:string;
    onExpired:()=>void;onFinished:()=>void;
  }=$props();
  let stock=$state<Stock|null>(null);
  let selected=$state<Record<string,number>>({});
  let preparing=$state(false);
  let loading=$state(false);
  let error=$state('');
  let notice=$state('');
  let prepared=$state<Prepared|null>(null);
  let complete=$state<Done|null>(null);
  let cashAmount=$state('');
  let cashConfirmed=$state(false);
  let tenderFrozen=$state(false);
  let prepId=$state('');
  let prepFingerprint=$state('');
  let tenderId=$state('');
  let cancelId=$state('');
  let refresh=$state(0);
  let lastScope='';
  const scope=$derived(owner+':'+actorId+':'+registerId+':'+shiftId);
  const key=(item:Item)=>item.product_id+':'+item.variant_id;
  const uuid=(v:unknown)=>typeof v==='string'&&
    /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(v);
  const validId=(v:unknown)=>typeof v==='string'&&/^[A-Za-z0-9_-]{1,80}$/.test(v);
  const euro=(cents:number)=>new Intl.NumberFormat('el-CY',{
    style:'currency',currency:'EUR'}).format(cents/100);
  const cents=(value:string):number|null=>{
    if(!/^\d{1,6}(?:[.,]\d{1,2})?$/.test(value))return null;
    const [integer,decimal='']=value.replace(',','.').split('.');
    const result=Number(integer)*100+Number(decimal.padEnd(2,'0'));
    return Number.isSafeInteger(result)&&result<=100000000?result:null;
  };
  const picked=$derived((stock?.items??[]).map(item=>({
    item,quantity:selected[key(item)]??0
  })).filter(x=>Number.isInteger(x.quantity)&&x.quantity>=1&&
    x.quantity<=Math.min(99,x.item.available)));
  const estimate=$derived(picked.reduce((total,x)=>total+x.quantity*x.item.price_cents,0));
  const validBasket=$derived(picked.length>=1&&picked.length<=30&&estimate<=100000000);
  const canPrepare=$derived(!!stock?.fiscal_policies_approved&&validBasket&&
    !prepared&&!complete&&!preparing);
  const stockResponse=(raw:unknown):raw is Stock=>{
    if(!raw||typeof raw!=='object'||Array.isArray(raw))return false;
    const v=raw as Record<string,unknown>;
    return v.organization_id===owner&&v.register_id===registerId&&
      v.shift_id===shiftId&&validId(v.location_id)&&
      typeof v.location_name==='string'&&
      typeof v.fiscal_policies_approved==='boolean'&&v.limit===150&&
      Array.isArray(v.items)&&v.items.length<=150&&v.items.every((i:unknown)=>{
        if(!i||typeof i!=='object'||Array.isArray(i))return false;
        const x=i as Record<string,unknown>;
        return validId(x.product_id)&&validId(x.variant_id)&&
          typeof x.product_name==='string'&&x.product_name.length<=100&&
          typeof x.variant_name==='string'&&x.variant_name.length<=80&&
          Number.isSafeInteger(x.price_cents)&&Number(x.price_cents)>0&&
          Number(x.price_cents)<=10000000&&
          Number.isSafeInteger(x.available)&&Number(x.available)>=0&&
          Number(x.available)<=1000000;
      });
  };

  $effect(()=>{
    const current=scope;
    if(current!==lastScope){
      stock=null;selected={};prepared=null;complete=null;
      cashAmount='';cashConfirmed=false;tenderFrozen=false;
      prepId='';prepFingerprint='';tenderId='';cancelId='';
      error='';notice='';lastScope=current;
    }
  });
  $effect(()=>{
    const current=scope;void refresh;
    stock=null;loading=false;
    if(!browser||!actorId||!shiftId)return;
    const controller=new AbortController();
    loading=true;
    void(async()=>{
      try{
        const query=new URLSearchParams({organization_id:owner,shift_id:shiftId});
        const response=await fetch('/api/admin/pos/cashier/registers/'+
          encodeURIComponent(registerId)+'/sellable-stock?'+query,{
            credentials:'same-origin',cache:'no-store',signal:controller.signal
          });
        if(controller.signal.aborted||scope!==current)return;
        if(response.status===401){onExpired();return;}
        if(!response.ok)throw Error(response.status===403||response.status===503
          ?'Το ταμείο αγορών δεν έχει εγκριθεί ή ανατεθεί ακόμη.'
          :'Δεν είναι διαθέσιμο το απόθεμα αυτού του ταμείου.');
        const data:unknown=await response.json();
        if(!stockResponse(data))throw Error('Μη έγκυρα στοιχεία αποθέματος.');
        if(scope===current&&!controller.signal.aborted)stock=data;
      }catch(e){
        if(!controller.signal.aborted&&scope===current)
          error=e instanceof Error?e.message:'Σφάλμα αποθέματος.';
      }finally{if(!controller.signal.aborted&&scope===current)loading=false;}
    })();
    return()=>controller.abort();
  });

  function setQuantity(item:Item,value:number){
    if(prepared||complete||preparing)return;
    const quantity=Number.isInteger(value)&&value>=0&&
      value<=Math.min(99,item.available)?value:0;
    selected={...selected,[key(item)]:quantity};
    prepId='';prepFingerprint='';
  }
  async function prepare(){
    if(!canPrepare||!stock||!browser)return;
    const current=scope,ownerAtStart=owner,registerAtStart=registerId,
      shiftAtStart=shiftId;
    const basket=picked.map(({item,quantity})=>({
      product_id:item.product_id,variant_id:item.variant_id,quantity
    }));
    const fingerprint=JSON.stringify([current,basket]);
    if(prepFingerprint!==fingerprint||!uuid(prepId)){
      prepId=crypto.randomUUID();prepFingerprint=fingerprint;
    }
    preparing=true;error='';notice='';
    try{
      const response=await fetch('/api/admin/pos/cashier/registers/'+
        encodeURIComponent(registerAtStart)+'/sales/prepare?'+
        new URLSearchParams({organization_id:ownerAtStart}),{
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({request_id:prepId,shift_id:shiftAtStart,items:basket})
      });
      if(scope!==current)return;
      if(response.status===401){onExpired();return;}
      if(!response.ok)throw Error(response.status===403||response.status===503
        ?'Η δημιουργία πώλησης δεν είναι ενεργοποιημένη από τον διαχειριστή.'
        :response.status===409?'Το διαθέσιμο απόθεμα άλλαξε. Ανανέωση απαιτείται.'
        :'Η παραγγελία δεν δεσμεύτηκε.');
      const raw:unknown=await response.json();
      if(!raw||typeof raw!=='object'||Array.isArray(raw))
        throw Error('Μη επαληθευμένη πώληση.');
      const v=raw as Record<string,unknown>;
      if(v.organization_id!==ownerAtStart||!uuid(v.sale_id)||
        v.status!=='awaiting_payment'||
        !Number.isSafeInteger(v.total_cents)||Number(v.total_cents)<1||
        Number(v.total_cents)>100000000||typeof v.replay!=='boolean')
        throw Error('Απορρίφθηκε μη έγκυρη οικονομική απόκριση.');
      prepared={sale_id:v.sale_id as string,total_cents:v.total_cents as number};
      cashAmount=((v.total_cents as number)/100).toFixed(2);
      cashConfirmed=false;tenderId='';cancelId='';tenderFrozen=false;
      notice='Η παραγγελία δεσμεύτηκε. Δεν έχει ληφθεί πληρωμή ή εκδοθεί απόδειξη.';
    }catch(e){
      if(scope===current)error=e instanceof Error?e.message:'Σφάλμα πώλησης.';
    }finally{preparing=false;}
  }
  async function settle(){
    const sale=prepared,amount=cents(cashAmount);
    if(!sale||!browser||preparing||!cashConfirmed||amount===null||
      amount<sale.total_cents||!stock?.fiscal_policies_approved)return;
    const current=scope,ownerAtStart=owner;
    if(!uuid(tenderId))tenderId=crypto.randomUUID();
    tenderFrozen=true;preparing=true;error='';notice='';
    try{
      const response=await fetch('/api/admin/pos/cashier/sales/'+
        encodeURIComponent(sale.sale_id)+'/tender-cash?'+
        new URLSearchParams({organization_id:ownerAtStart}),{
          method:'POST',credentials:'same-origin',cache:'no-store',
          headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
          body:JSON.stringify({request_id:tenderId,tendered_cash_cents:amount})
        });
      if(scope!==current)return;
      if(response.status===401){onExpired();return;}
      if(!response.ok)throw Error(response.status===403||response.status===503
        ?'Η είσπραξη δεν εγκρίθηκε. Μην παραδώσετε παραστατικό χωρίς επαλήθευση.'
        :response.status===409?'Η πώληση άλλαξε. Ελέγξτε τη συμφωνία βάρδιας.'
        :'Αβέβαιη κατάσταση πληρωμής. Επαναλάβετε μόνο με το ίδιο ποσό.');
      const raw:unknown=await response.json();
      if(!raw||typeof raw!=='object'||Array.isArray(raw))
        throw Error('Μη επαληθευμένη είσπραξη.');
      const v=raw as Record<string,unknown>;
      if(v.organization_id!==ownerAtStart||v.sale_id!==sale.sale_id||
        !uuid(v.receipt_id)||!Number.isSafeInteger(v.receipt_number)||
        Number(v.receipt_number)<=0||v.total_cents!==sale.total_cents||
        v.tendered_cents!==amount||
        v.change_cents!==amount-sale.total_cents||typeof v.replay!=='boolean')
        throw Error('Μη επαληθευμένο παραστατικό ή ποσό επιστροφής.');
      complete={receipt_id:v.receipt_id as string,
        receipt_number:v.receipt_number as number,
        total_cents:sale.total_cents,change_cents:v.change_cents as number};
      prepared=null;notice='Η επαληθευμένη καταχώριση μετρητών ολοκληρώθηκε.';
      onFinished();refresh++;
    }catch(e){
      if(scope===current)error=e instanceof Error?e.message:'Αβέβαιη είσπραξη.';
    }finally{preparing=false;}
  }
  async function cancel(){
    const sale=prepared;
    if(!sale||!browser||preparing||tenderFrozen)return;
    const current=scope;
    if(!uuid(cancelId))cancelId=crypto.randomUUID();
    preparing=true;error='';
    try{
      const response=await fetch('/api/admin/pos/cashier/sales/'+
        encodeURIComponent(sale.sale_id)+'/cancel?'+
        new URLSearchParams({organization_id:owner}),{
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({request_id:cancelId})
      });
      if(scope!==current)return;
      if(response.status===401){onExpired();return;}
      if(!response.ok)throw Error('Η ακύρωση απέτυχε. Το απόθεμα παραμένει δεσμευμένο.');
      const raw:unknown=await response.json();
      if(!raw||typeof raw!=='object'||Array.isArray(raw)||
        (raw as Record<string,unknown>).organization_id!==owner||
        (raw as Record<string,unknown>).sale_id!==sale.sale_id)
        throw Error('Μη επαληθευμένη ακύρωση.');
      prepared=null;selected={};prepId='';prepFingerprint='';
      notice='Η ανεξόφλητη παραγγελία ακυρώθηκε. Δεν υπήρξε είσπραξη.';
      refresh++;
    }catch(e){if(scope===current)error=e instanceof Error?e.message:'Αποτυχία ακύρωσης.';}
    finally{preparing=false;}
  }
</script>

<section class="admin-panel cashier-checkout" data-testid="pos-cashier-safe-checkout">
  <h3>Πώληση μετρητών · συγκεκριμένο ταμείο</h3>
  <p class="admin-muted">Μόνο προϊόντα και διαθέσιμο απόθεμα της δικής σας
    τοποθεσίας. Η τιμή υπολογίζεται ξανά στον εξυπηρετητή. Πραγματικές
    εισπράξεις επιτρέπονται μόνο μετά από νομικό/φορολογικό έλεγχο και άδεια.</p>
  {#if loading}<p role="status">Φόρτωση αποθέματος τοποθεσίας…</p>{/if}
  {#if error}<p role="alert">{error}</p>{/if}
  {#if notice}<p role="status">{notice}</p>{/if}
  {#if complete}
    <div class="cashier-success" role="status">
      <p><strong>Απόδειξη #{complete.receipt_number}</strong></p>
      <p>Εισπραχθέν σύνολο: {euro(complete.total_cents)} · Ρέστα: {euro(complete.change_cents)}</p>
      <a class="admin-secondary" href={'/admin/pos/receipts/'+
        encodeURIComponent(complete.receipt_id)+'?'+
        new URLSearchParams({organization_id:owner})}>Προβολή & εκτύπωση αποθηκευμένης απόδειξης</a>
      <button type="button" class="admin-secondary" onclick={()=>{
        complete=null;selected={};cashAmount='';tenderFrozen=false;
      }}>Νέα πώληση</button>
    </div>
  {:else if prepared}
    <div class="cashier-prepared">
      <p><strong>Δεσμευμένη παραγγελία: {euro(prepared.total_cents)}</strong></p>
      <p class="admin-muted">Δεν έχει εκδοθεί απόδειξη. Μην παραδώσετε προϊόντα πριν από την
        επαληθευμένη ολοκλήρωση.</p>
      <label>Φυσικά μετρημένα χρήματα (€)
        <input type="text" inputmode="decimal" bind:value={cashAmount}
          maxlength="10" required disabled={preparing||tenderFrozen}/>
      </label>
      <label class="cashier-confirm">
        <input type="checkbox" bind:checked={cashConfirmed} disabled={preparing||tenderFrozen}/>
        Επιβεβαιώνω ότι έλαβα και καταμέτρησα το ακριβές ποσό σε μετρητά.
      </label>
      <div class="cashier-actions">
        <button type="button" class="admin-secondary" disabled={preparing||
          (!tenderFrozen&&(!cashConfirmed||cents(cashAmount)===null||
          (cents(cashAmount)??0)<prepared.total_cents))}
          onclick={()=>void settle()}>
          {preparing?'Έλεγχος…':tenderFrozen?'Επανάληψη ίδιου αιτήματος':'Επιβεβαίωση μετρητών & απόδειξη'}
        </button>
        <button type="button" class="admin-secondary"
          disabled={preparing||tenderFrozen} onclick={()=>void cancel()}>
          Ακύρωση χωρίς πληρωμή
        </button>
      </div>
      {#if tenderFrozen}
        <p role="alert">Η είσπραξη ενδέχεται να έχει καταγραφεί.
          Για αποφυγή διπλής χρέωσης, επαναλάβετε μόνο με το ίδιο αναγνωριστικό
          και ποσό ή ζητήστε έλεγχο από υπεύθυνο.</p>
      {/if}
    </div>
  {:else if stock}
    <p class="admin-muted">Τοποθεσία: {stock.location_name}</p>
    {#if !stock.fiscal_policies_approved}
      <p role="status">Η είσπραξη είναι απενεργοποιημένη: δεν υπάρχουν και οι δύο
        ανεξάρτητα εγκεκριμένες φορολογικές πολιτικές. Δεν επιτρέπεται checkout.</p>
    {/if}
    {#if stock.items.length===0}
      <p>Δεν υπάρχει διαθέσιμος κατάλογος για το συγκεκριμένο ταμείο.</p>
    {:else}
      <div class="cashier-items">
        {#each stock.items as item (key(item))}
          <label class="cashier-item">
            <span>{item.product_name} · {item.variant_name}</span>
            <span>{euro(item.price_cents)} · διαθέσιμα {item.available}</span>
            <input type="number" min="0" max={Math.min(99,item.available)} step="1"
              aria-label={'Ποσότητα για '+item.product_name+' '+item.variant_name}
              value={selected[key(item)]??0} disabled={preparing||!stock.fiscal_policies_approved||
                item.available===0} oninput={event=>setQuantity(item,Number(event.currentTarget.value))}/>
          </label>
        {/each}
      </div>
      <p>Ενδεικτικό σύνολο: <strong>{euro(estimate)}</strong> ·
        {picked.length} προϊόντα. Ο εξυπηρετητής ορίζει το τελικό ποσό.</p>
      <div class="cashier-actions">
        <button type="button" class="admin-secondary" disabled={!canPrepare}
          onclick={()=>void prepare()}>{preparing?'Δέσμευση…':'Δέσμευση παραγγελίας'}</button>
        <button type="button" class="admin-secondary" disabled={loading||preparing}
          onclick={()=>refresh++}>Ανανέωση αποθέματος</button>
      </div>
    {/if}
  {/if}
</section>

<style>
  .cashier-checkout{display:grid;gap:12px;margin:16px 0}
  .cashier-checkout h3{margin:0}
  .cashier-items{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:9px}
  .cashier-item{display:grid;gap:6px;border:1px solid #5d5037;border-radius:9px;padding:12px}
  .cashier-item span:nth-child(2){font-size:.85rem;opacity:.85}
  .cashier-item input{max-width:120px}
  .cashier-prepared,.cashier-success{display:grid;gap:10px}
  .cashier-prepared>label{display:grid;gap:7px;max-width:360px}
  .cashier-confirm{display:flex!important;flex-direction:row;gap:8px}
  .cashier-actions{display:flex;gap:8px;flex-wrap:wrap}
</style>
