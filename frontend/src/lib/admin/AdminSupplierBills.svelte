<script lang="ts">
  import { untrack } from 'svelte';

  type Site={city_id:string;city_name:string;location_id:string;location_name:string};
  type Bill={id:string;supplier_name:string;invoice_reference:string;
    description:string;category:string;bill_date:string;gross_amount_cents:number;
    city_id:string|null;location_id:string|null;recorded_at:string};
  type Register={organization_id:string;month:string;currency:'EUR';
    is_cash_payment:false;is_general_ledger:false;
    summary:{bill_count:number;gross_amount_cents:number};bills:Bill[];sites:Site[]};
  let {owner,sessionId,canWrite}:{
    owner:string|null;sessionId:string;canWrite:boolean
  }=$props();
  const current=new Intl.DateTimeFormat('en-CA',{timeZone:'Asia/Nicosia',
    year:'numeric',month:'2-digit',day:'2-digit'}).formatToParts(new Date());
  const part=(name:string)=>current.find(p=>p.type===name)?.value??'';
  let month=$state(part('year')+'-'+part('month'));
  let billDate=$state(part('year')+'-'+part('month')+'-'+part('day'));
  let reload=$state(0);
  let data=$state<Register|null>(null);
  let loading=$state(false);
  let error=$state('');
  let saved=$state('');
  let saving=$state(false);
  let supplier=$state('');
  let reference=$state('');
  let category=$state('facilities');
  let description=$state('');
  let amount=$state('');
  let locationId=$state('');
  let requestId=$state<string|null>(null);

  const categories=[
    {value:'facilities',text:'Εγκαταστάσεις'},
    {value:'equipment',text:'Εξοπλισμός'},
    {value:'travel',text:'Μετακινήσεις'},
    {value:'matchday',text:'Αγώνες'},
    {value:'staff_services',text:'Υπηρεσίες προσωπικού'},
    {value:'administration',text:'Διοίκηση'},
    {value:'other',text:'Άλλο'}
  ];
  const money=(cents:number)=>new Intl.NumberFormat('el-CY',
    {style:'currency',currency:'EUR'}).format(cents/100);
  const safe=(n:unknown)=>typeof n==='number'&&Number.isSafeInteger(n)&&n>=0;
  const id=(s:unknown)=>typeof s==='string'&&
    /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(s);
  function valid(value:unknown,selected:string,period:string):value is Register{
    if(!value||typeof value!=='object'||Array.isArray(value))return false;
    const r=value as Record<string,unknown>;
    if(r.organization_id!==selected||r.month!==period+'-01'||
      r.currency!=='EUR'||r.is_cash_payment!==false||r.is_general_ledger!==false||
      !r.summary||typeof r.summary!=='object'||Array.isArray(r.summary)||
      !safe((r.summary as Record<string,unknown>).bill_count)||
      !safe((r.summary as Record<string,unknown>).gross_amount_cents)||
      !Array.isArray(r.bills)||r.bills.length>100||
      !Array.isArray(r.sites)||r.sites.length>2000)return false;
    return r.bills.every((b:unknown)=>{
      if(!b||typeof b!=='object'||Array.isArray(b))return false;
      const x=b as Record<string,unknown>;
      return id(x.id)&&typeof x.supplier_name==='string'&&
        typeof x.invoice_reference==='string'&&typeof x.description==='string'&&
        categories.some(c=>c.value===x.category)&&
        typeof x.bill_date==='string'&&/^\d{4}-\d{2}-\d{2}$/.test(x.bill_date)&&
        safe(x.gross_amount_cents)&&typeof x.recorded_at==='string'&&
        (x.city_id===null||id(x.city_id))&&(x.location_id===null||id(x.location_id));
    })&&r.sites.every((s:unknown)=>{
      if(!s||typeof s!=='object'||Array.isArray(s))return false;
      const x=s as Record<string,unknown>;
      return id(x.city_id)&&id(x.location_id)&&
        typeof x.city_name==='string'&&typeof x.location_name==='string';
    });
  }
  $effect(()=>{
    const selected=owner,actor=sessionId,period=month,refresh=reload;
    void actor;void refresh;
    data=null;loading=false;error='';
    if(!selected||!/^(20[2-9][0-9]|2100)-(0[1-9]|1[0-2])$/.test(period))return;
    const ctrl=new AbortController();
    loading=true;
    untrack(()=>{void(async()=>{
      try {
        const response=await fetch('/api/admin/finance/supplier-bills?organization_id='+
          encodeURIComponent(selected)+'&month='+encodeURIComponent(period),{
            credentials:'same-origin',cache:'no-store',signal:ctrl.signal
          });
        if(!response.ok)throw new Error(response.status===403
          ?'Δεν υπάρχει πλέον πρόσβαση στις υποχρεώσεις προμηθευτών.'
          :'Το μητρώο προμηθευτών δεν είναι διαθέσιμο.');
        const json:unknown=await response.json();
        if(!valid(json,selected,period))throw new Error('Μη επαληθευμένο μητρώο.');
        if(!ctrl.signal.aborted&&owner===selected&&month===period)data=json;
      }catch(e){if(!ctrl.signal.aborted)error=e instanceof Error?e.message:'Σφάλμα μητρώου.';}
      finally{if(!ctrl.signal.aborted)loading=false;}
    })();});
    return()=>ctrl.abort();
  });
  $effect(()=>{
    void owner;
    locationId='';requestId=null;saved='';error='';
  });
  function cents(value:string):number|null{
    if(!/^\d{1,7}([.,]\d{1,2})?$/.test(value))return null;
    const [euros,fraction='']=value.replace(',','.').split('.');
    const result=Number(euros)*100+Number(fraction.padEnd(2,'0'));
    return Number.isSafeInteger(result)&&result>=1&&result<=1000000000?result:null;
  }
  const clean=(s:string,min:number,max:number)=>s.length>=min&&
    s.length<=max&&s.trim()===s&&![...s].some(x=>/\p{Cc}/u.test(x));

  async function submit(e:SubmitEvent){
    e.preventDefault();
    if(!owner||!canWrite||saving)return;
    const selected=owner,actor=sessionId;
    const sum=cents(amount);
    if(sum===null||!clean(supplier,2,140)||!clean(reference,1,80)||
      !clean(description,4,300)||!categories.some(c=>c.value===category)||
      !/^\d{4}-\d{2}-\d{2}$/.test(billDate)||billDate<'2020-01-01'||
      billDate>'2100-12-31'||(selected==='lefteria_fc_academy'&&
      !data?.sites.some(x=>x.location_id===locationId))){
      error='Συμπληρώστε μοναδικό παραστατικό, έγκυρο ποσό και ακριβή τοποθεσία.';
      return;
    }
    requestId??=crypto.randomUUID();
    const key=requestId;
    saving=true;error='';saved='';
    try {
      const response=await fetch('/api/admin/finance/supplier-bills?organization_id='+
        encodeURIComponent(selected),{
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({
          client_request_id:key,supplier_name:supplier,invoice_reference:reference,
          category,description,bill_date:billDate,gross_amount_cents:sum,
          location_id:selected==='lefteria_fc_academy'?locationId:null
        })
      });
      if(owner!==selected||sessionId!==actor)return;
      if(!response.ok){
        if(response.status===409)requestId=null;
        throw new Error(response.status===403?'Η άδεια καταχώρισης ανακλήθηκε.':
          response.status===409?'Το παραστατικό υπάρχει ήδη ή η εγγραφή διαφέρει.':
          'Δεν επιβεβαιώθηκε η αποθήκευση. Ελέγξτε το μητρώο πριν επαναλάβετε.');
      }
      const result:unknown=await response.json();
      if(!result||typeof result!=='object'||Array.isArray(result)||
        (result as Record<string,unknown>).organization_id!==selected||
        !id((result as Record<string,unknown>).id)||
        (result as Record<string,unknown>).recorded!==true||
        (result as Record<string,unknown>).cash_paid!==false||
        (result as Record<string,unknown>).posted_to_general_ledger!==false)
        throw new Error('Η απάντηση εγγραφής δεν επαληθεύτηκε.');
      requestId=null;supplier='';reference='';description='';amount='';
      saved='Το παραστατικό καταχωρίστηκε ως υποχρέωση προμηθευτή. Δεν εξοφλήθηκε.';
      month=billDate.slice(0,7);reload++;
    }catch(e){if(owner===selected)error=e instanceof Error?e.message:'Αποτυχία αποθήκευσης.';}
    finally{saving=false;}
  }
</script>

<section class="admin-panel" data-testid="supplier-bill-register">
  <h2>Παραστατικά προμηθευτών · χωριστό μητρώο</h2>
  <p class="admin-muted">Χειροκίνητες καταχωρίσεις τιμολογίων και υποχρεώσεων προς προμηθευτές.
    Δεν αποτελούν καταγεγραμμένες πληρωμές, φορολογικό ΦΠΑ, μισθοδοσία ή διπλογραφικές εγγραφές.</p>
  <label for="supplier-report-month">Μήνας παραστατικών</label>
  <input id="supplier-report-month" type="month" min="2020-01" max="2100-12" bind:value={month}/>
  {#if loading}
    <p role="status">Ανάκτηση παραστατικών…</p>
  {:else if error && !data}
    <p role="alert">{error}</p>
    <button type="button" class="admin-secondary" onclick={()=>reload++}>Επανάληψη</button>
  {:else if data}
    <p><strong>{data.summary.bill_count}</strong> τιμολόγια ·
      <strong>{money(data.summary.gross_amount_cents)}</strong> καταγεγραμμένες μικτές υποχρεώσεις.</p>
    {#if data.summary.bill_count>data.bills.length}
      <p class="admin-muted">Προβάλλονται τα πρώτα {data.bills.length} παραστατικά. Το σύνολο αφορά όλα τα παραστατικά του μήνα.</p>
    {/if}
    {#if data.bills.length}
      <ul class="supplier-list">
        {#each data.bills as bill (bill.id)}
          <li><strong>{bill.supplier_name} · {bill.invoice_reference}</strong>
            <span>{money(bill.gross_amount_cents)} · {bill.bill_date} ·
              {categories.find(c=>c.value===bill.category)?.text??bill.category}</span>
            <small>{bill.description}
              {#if bill.location_id}
                · {data.sites.find(s=>s.location_id===bill.location_id)?.location_name??'Ιστορική τοποθεσία'}
              {/if}
            </small>
          </li>
        {/each}
      </ul>
    {:else}
      <p>Δεν υπάρχουν παραστατικά για τον επιλεγμένο μήνα.</p>
    {/if}
    {#if canWrite}
      <h3>Καταχώριση τιμολογίου προμηθευτή</h3>
      <form class="supplier-form" onsubmit={submit}>
        <label for="supplier-name">Προμηθευτής</label>
        <input id="supplier-name" required minlength="2" maxlength="140" bind:value={supplier}/>
        <label for="supplier-invoice">Αριθμός / αναφορά τιμολογίου</label>
        <input id="supplier-invoice" required maxlength="80" bind:value={reference}/>
        <label for="supplier-type">Κατηγορία</label>
        <select id="supplier-type" bind:value={category}>
          {#each categories as item (item.value)}
            <option value={item.value}>{item.text}</option>
          {/each}
        </select>
        <label for="supplier-note">Περιγραφή</label>
        <input id="supplier-note" required minlength="4" maxlength="300" bind:value={description}/>
        <label for="supplier-date">Ημερομηνία τιμολογίου</label>
        <input id="supplier-date" type="date" min="2020-01-01" max="2100-12-31" required bind:value={billDate}/>
        <label for="supplier-cents">Μικτό ποσό (EUR)</label>
        <input id="supplier-cents" inputmode="decimal" required placeholder="125.00" bind:value={amount}/>
        {#if owner==='lefteria_fc_academy'}
          <label for="supplier-location">Τοποθεσία Ακαδημίας</label>
          <select id="supplier-location" bind:value={locationId} required>
            <option value="">Επιλέξτε ακριβή τοποθεσία</option>
            {#each data.sites as site (site.location_id)}
              <option value={site.location_id}>{site.city_name} · {site.location_name}</option>
            {/each}
          </select>
          {#if !data.sites.length}
            <p role="alert">Χρειάζεται ενεργή, καταχωρισμένη τοποθεσία για έξοδο Ακαδημίας.</p>
          {/if}
        {/if}
        <button type="submit" disabled={saving||(owner==='lefteria_fc_academy'&&!data.sites.length)}>
          {saving?'Αποθήκευση…':'Καταχώριση υποχρέωσης'}
        </button>
      </form>
    {/if}
  {/if}
  {#if saved}<p role="status">{saved}</p>{/if}
  {#if error && data}<p role="alert">{error}</p>{/if}
</section>

<style>
  .supplier-form{display:grid;gap:.55rem;max-width:34rem}
  .supplier-form input,.supplier-form select,input[type="month"]{
    padding:.7rem;border-radius:.5rem;border:1px solid var(--border-color,#9ca3af);
    color:inherit;background:var(--surface,#fff);width:100%;max-width:34rem}
  .supplier-form button{margin-top:.65rem;padding:.8rem}
  .supplier-list{list-style:none;padding:0;display:grid;gap:.5rem}
  .supplier-list li{border:1px solid var(--border-color,#9ca3af);
    border-radius:.5rem;padding:.8rem;display:grid;gap:.25rem}
  .supplier-list span,.supplier-list small{opacity:.8}
</style>
