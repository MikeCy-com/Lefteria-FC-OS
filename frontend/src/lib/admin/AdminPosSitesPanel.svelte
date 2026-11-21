<script lang="ts">
  import {browser} from '$app/env';
  import AdminPosRecoveryReviewPanel from './AdminPosRecoveryReviewPanel.svelte';
  import AdminPosStockReconciliationPanel from './AdminPosStockReconciliationPanel.svelte';
  type Owner='lefteria_fc'|'lefteria_fc_academy';
  type Site={pos_location_id:string;name:string;city_name:string;
    academy_city_id:string|null;academy_location_id:string|null};
  type Sites={organization_id:Owner;sites:Site[]};
  type SiteFinances={
    organization_id:Owner;pos_location_id:string;location_name:string;
    registers_total:number;sales_total:number;sales_paid:number;
    sales_pending:number;receipts_issued:number;receipted_gross_cents:number;
    verified_cash_cents:number;verified_card_cents:number;
    verified_refunds_cents:number;receipted_net_after_refunds_cents:number;
    paid_without_receipt:number;finance_posting_active:false;
  };
  type Sale={sale_id:string;register_id:string;register_label:string;
    status:'awaiting_payment'|'partially_paid'|'paid'|'voided';
    total_cents:number;created_at:string;paid_at:string|null;
    receipt_number:number|null;receipt_id:string|null};
  type History={organization_id:Owner;pos_location_id:string;limit:50;sales:Sale[]};
  type OpenShift={id:string;cashier_id:string;cashier_name:string;
    cashier_active:boolean;opened_at:string;opening_cash_cents:number};
  type Terminal={id:string;label:string;location_id:string;location_name:string;
    active:boolean;open_shift:OpenShift|null;
    operators:{admin_account_id:string;name:string;active:boolean}[]};
  type Account={id:string;name:string;role:string};
  type AcademyLocation={academy_city_id:string;academy_location_id:string;
    city_name:string;location_name:string;pos_location_id:string|null};
  type Setup={organization_id:Owner;terminals:Terminal[];
    operator_accounts:Account[];academy_locations:AcademyLocation[]};
  let {owner,actorId,canProvision,onExpired,onRevoked}:{
    owner:Owner;actorId:string;canProvision:boolean;
    onExpired:()=>void;onRevoked:()=>void;
  }=$props();

  let sites=$state<Sites|null>(null);
  let setup=$state<Setup|null>(null);
  let siteId=$state('');
  let academyLocationId=$state('');
  let terminalName=$state('');
  let terminalId=$state('');
  let accountId=$state('');
  let recoveryCount=$state('');
  let recoveryReason=$state<'account_suspended'|'security_incident'|'operator_unavailable'>('account_suspended');
  let recoveryConfirmed=$state(false);
  let recoveryRequestId=$state('');
  let recoveryFingerprint=$state('');
  let finances=$state<SiteFinances|null>(null);
  let history=$state<History|null>(null);
  let loading=$state(false);
  let setupLoading=$state(false);
  let reportLoading=$state(false);
  let historyLoading=$state(false);
  let historyError=$state('');
  let writing=$state(false);
  let error=$state('');
  let setupError=$state('');
  let reportError=$state('');
  let notice=$state('');
  let refresh=$state(0);
  const selectedSite=$derived(sites?.sites.find(s=>s.pos_location_id===siteId));
  const formatDate=(value:string)=>new Intl.DateTimeFormat('el-CY',{
    dateStyle:'short',timeStyle:'short'}).format(new Date(value));
  const statusLabel=(value:Sale['status'])=>({
    awaiting_payment:'Αναμονή πληρωμής',partially_paid:'Μερική πληρωμή',
    paid:'Εξοφλημένη',voided:'Ακυρωμένη'
  }[value]);
  const selectedTerminal=$derived(setup?.terminals.find(t=>t.id===terminalId));
  const selectedAccount=$derived(selectedTerminal?.operators.find(o=>o.admin_account_id===accountId));
  const countedCents=(value:string):number|null=>{
    if(!/^\d{1,6}(?:[.,]\d{1,2})?$/.test(value))return null;
    const [whole,dec='']=value.replace(',','.').split('.');
    const cents=Number(whole)*100+Number(dec.padEnd(2,'0'));
    return Number.isSafeInteger(cents)&&cents<=100000000?cents:null;
  };
  const euro=(cents:number)=>new Intl.NumberFormat('el-CY',{
    style:'currency',currency:'EUR'}).format(cents/100);
  const validId=(x:unknown)=>typeof x==='string'&&/^[A-Za-z0-9_-]{1,80}$/.test(x);
  const uuid=(x:unknown)=>typeof x==='string'&&
    /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(x);
  const validSites=(raw:unknown,expected:Owner):raw is Sites=>{
    if(!raw||typeof raw!=='object'||Array.isArray(raw))return false;
    const v=raw as Record<string,unknown>;
    return v.organization_id===expected&&Array.isArray(v.sites)&&v.sites.length<=80&&
      v.sites.every((item:unknown)=>{
        if(!item||typeof item!=='object'||Array.isArray(item))return false;
        const s=item as Record<string,unknown>;
        return validId(s.pos_location_id)&&typeof s.name==='string'&&
          typeof s.city_name==='string'&&
          (s.academy_location_id===null||uuid(s.academy_location_id))&&
          (s.academy_city_id===null||uuid(s.academy_city_id));
      });
  };
  const validFinance=(raw:unknown,expected:Owner,location:string):raw is SiteFinances=>{
    if(!raw||typeof raw!=='object'||Array.isArray(raw))return false;
    const v=raw as Record<string,unknown>;
    if(v.organization_id!==expected||v.pos_location_id!==location||
       typeof v.location_name!=='string'||v.finance_posting_active!==false)return false;
    const nonnegative=['registers_total','sales_total','sales_paid','sales_pending',
      'receipts_issued','receipted_gross_cents','verified_cash_cents',
      'verified_card_cents','verified_refunds_cents','paid_without_receipt'];
    return nonnegative.every(k=>Number.isSafeInteger(v[k])&&(v[k] as number)>=0)&&
      Number.isSafeInteger(v.receipted_net_after_refunds_cents)&&
      v.receipted_net_after_refunds_cents===
        (v.receipted_gross_cents as number)-(v.verified_refunds_cents as number);
  };
  const validHistory=(raw:unknown,expected:Owner,site:string):raw is History=>{
    if(!raw||typeof raw!=='object'||Array.isArray(raw))return false;
    const v=raw as Record<string,unknown>;
    return v.organization_id===expected&&v.pos_location_id===site&&v.limit===50&&
      Array.isArray(v.sales)&&v.sales.length<=50&&v.sales.every((row:unknown)=>{
        if(!row||typeof row!=='object'||Array.isArray(row))return false;
        const x=row as Record<string,unknown>;
        return uuid(x.sale_id)&&validId(x.register_id)&&
          typeof x.register_label==='string'&&
          ['awaiting_payment','partially_paid','paid','voided'].includes(String(x.status))&&
          Number.isSafeInteger(x.total_cents)&&(x.total_cents as number)>0&&
          typeof x.created_at==='string'&&!Number.isNaN(Date.parse(x.created_at))&&
          (x.paid_at===null||(typeof x.paid_at==='string'&&!Number.isNaN(Date.parse(x.paid_at))))&&
          (x.receipt_number===null||(Number.isSafeInteger(x.receipt_number)&&
            (x.receipt_number as number)>0))&&
          (x.receipt_id===null||uuid(x.receipt_id));
      });
  };
  const validSetup=(raw:unknown,expected:Owner):raw is Setup=>{
    if(!raw||typeof raw!=='object'||Array.isArray(raw))return false;
    const v=raw as Record<string,unknown>;
    return v.organization_id===expected&&
      Array.isArray(v.terminals)&&v.terminals.length<=200&&
      Array.isArray(v.operator_accounts)&&v.operator_accounts.length<=500&&
      Array.isArray(v.academy_locations)&&v.academy_locations.length<=100&&
      v.terminals.every((t:unknown)=>{
        if(!t||typeof t!=='object'||Array.isArray(t))return false;
        const x=t as Record<string,unknown>;
        return validId(x.id)&&validId(x.location_id)&&typeof x.label==='string'&&
          typeof x.location_name==='string'&&typeof x.active==='boolean'&&
          (x.open_shift===null||!!x.open_shift&&
            typeof x.open_shift==='object'&&!Array.isArray(x.open_shift)&&
            uuid((x.open_shift as Record<string,unknown>).id)&&
            uuid((x.open_shift as Record<string,unknown>).cashier_id)&&
            typeof (x.open_shift as Record<string,unknown>).cashier_name==='string'&&
            typeof (x.open_shift as Record<string,unknown>).cashier_active==='boolean'&&
            typeof (x.open_shift as Record<string,unknown>).opened_at==='string'&&
            !Number.isNaN(Date.parse(String((x.open_shift as Record<string,unknown>).opened_at)))&&
            Number.isSafeInteger((x.open_shift as Record<string,unknown>).opening_cash_cents)&&
            Number((x.open_shift as Record<string,unknown>).opening_cash_cents)>=0)&&
          Array.isArray(x.operators)&&x.operators.length<=100&&
          x.operators.every((p:unknown)=>!!p&&typeof p==='object'&&!Array.isArray(p)&&
            uuid((p as Record<string,unknown>).admin_account_id)&&
            typeof (p as Record<string,unknown>).name==='string'&&
            typeof (p as Record<string,unknown>).active==='boolean');
      })&&
      v.operator_accounts.every((a:unknown)=>!!a&&typeof a==='object'&&!Array.isArray(a)&&
        uuid((a as Record<string,unknown>).id)&&
        typeof (a as Record<string,unknown>).name==='string')&&
      v.academy_locations.every((l:unknown)=>!!l&&typeof l==='object'&&!Array.isArray(l)&&
        uuid((l as Record<string,unknown>).academy_location_id)&&
        uuid((l as Record<string,unknown>).academy_city_id)&&
        typeof (l as Record<string,unknown>).city_name==='string'&&
        typeof (l as Record<string,unknown>).location_name==='string'&&
        ((l as Record<string,unknown>).pos_location_id===null||
          validId((l as Record<string,unknown>).pos_location_id)));
  };
  const query=(selected:Owner)=>new URLSearchParams({organization_id:selected});
  const responseScope=(status:number)=>{
    if(status===401){onExpired();return true;}
    if(status===403){onRevoked();return true;}
    return false;
  };
  $effect(()=>{
    const selectedOwner=owner,selectedActor=actorId;
    void refresh;
    sites=null;finances=null;history=null;error='';notice='';reportError='';historyError='';loading=false;
    if(!browser||!selectedActor)return;
    const abort=new AbortController();
    loading=true;
    void (async()=>{
      try{
        const response=await fetch('/api/admin/pos/sites?'+query(selectedOwner),{
          credentials:'same-origin',cache:'no-store',signal:abort.signal
        });
        if(abort.signal.aborted)return;
        if(responseScope(response.status))return;
        if(!response.ok)throw Error('Οι τοποθεσίες POS δεν είναι διαθέσιμες.');
        const data:unknown=await response.json();
        if(!validSites(data,selectedOwner))throw Error('Μη έγκυρο μητρώο τοποθεσιών.');
        if(!abort.signal.aborted&&owner===selectedOwner&&actorId===selectedActor){
          sites=data;
          if(!data.sites.some(s=>s.pos_location_id===siteId))
            siteId=data.sites[0]?.pos_location_id??'';
        }
      }catch(e){if(!abort.signal.aborted)error=e instanceof Error?e.message:'Σφάλμα τοποθεσιών.';}
      finally{if(!abort.signal.aborted)loading=false;}
    })();
    return()=>abort.abort();
  });
  $effect(()=>{
    const selectedOwner=owner,selectedActor=actorId,allowed=canProvision;
    void refresh;
    setup=null;setupError='';setupLoading=false;
    if(!browser||!selectedActor||!allowed)return;
    const abort=new AbortController();
    setupLoading=true;
    void (async()=>{
      try{
        const response=await fetch('/api/admin/pos/sites/terminal-setup?'+query(selectedOwner),{
          credentials:'same-origin',cache:'no-store',signal:abort.signal
        });
        if(abort.signal.aborted)return;
        if(responseScope(response.status))return;
        if(!response.ok)throw Error('Η διαμόρφωση ταμείων δεν είναι διαθέσιμη.');
        const data:unknown=await response.json();
        if(!validSetup(data,selectedOwner))throw Error('Μη επαληθευμένα στοιχεία τερματικών.');
        if(!abort.signal.aborted&&owner===selectedOwner&&actorId===selectedActor){
          setup=data;
          if(!data.terminals.some(t=>t.id===terminalId))terminalId=data.terminals[0]?.id??'';
          if(!data.operator_accounts.some(a=>a.id===accountId))
            accountId=data.operator_accounts[0]?.id??'';
          if(!data.academy_locations.some(l=>l.academy_location_id===academyLocationId))
            academyLocationId=data.academy_locations.find(l=>!l.pos_location_id)?.academy_location_id??'';
        }
      }catch(e){if(!abort.signal.aborted)setupError=e instanceof Error?e.message:'Σφάλμα διαμόρφωσης.';}
      finally{if(!abort.signal.aborted)setupLoading=false;}
    })();
    return()=>abort.abort();
  });

  async function viewFinance(){
    if(!browser||!selectedSite||reportLoading)return;
    const expectedOwner=owner,expectedActor=actorId,expectedSite=selectedSite.pos_location_id;
    reportLoading=true;finances=null;reportError='';
    try{
      const response=await fetch('/api/admin/pos/sites/'+encodeURIComponent(expectedSite)+
        '/finance?'+query(expectedOwner),{credentials:'same-origin',cache:'no-store'});
      if(owner!==expectedOwner||actorId!==expectedActor||siteId!==expectedSite)return;
      if(responseScope(response.status))return;
      if(!response.ok)throw Error('Η οικονομική κατάσταση αυτού του σημείου δεν είναι διαθέσιμη.');
      const raw:unknown=await response.json();
      if(!validFinance(raw,expectedOwner,expectedSite))
        throw Error('Μη επαληθευμένη οικονομική αναφορά.');
      finances=raw;
    }catch(e){
      if(owner===expectedOwner&&actorId===expectedActor)
        reportError=e instanceof Error?e.message:'Σφάλμα οικονομικής αναφοράς.';
    }finally{reportLoading=false;}
  }

  async function viewSales(){
    if(!browser||!selectedSite||historyLoading)return;
    const expectedOwner=owner,expectedActor=actorId,expectedSite=selectedSite.pos_location_id;
    historyLoading=true;history=null;historyError='';
    try{
      const response=await fetch('/api/admin/pos/sites/'+encodeURIComponent(expectedSite)+
        '/sales?'+query(expectedOwner),{credentials:'same-origin',cache:'no-store'});
      if(owner!==expectedOwner||actorId!==expectedActor||siteId!==expectedSite)return;
      if(responseScope(response.status))return;
      if(!response.ok)throw Error('Το ιστορικό συναλλαγών δεν είναι διαθέσιμο.');
      const raw:unknown=await response.json();
      if(!validHistory(raw,expectedOwner,expectedSite))
        throw Error('Μη επαληθευμένο ιστορικό πωλήσεων.');
      history=raw;
    }catch(e){
      if(owner===expectedOwner&&actorId===expectedActor)
        historyError=e instanceof Error?e.message:'Σφάλμα ιστορικού.';
    }finally{historyLoading=false;}
  }

  async function recoverShift(){
    const register=selectedTerminal,shift=register?.open_shift;
    if(!browser||!canProvision||writing||!register||!shift||
      shift.cashier_active||!recoveryConfirmed)return;
    const cents=countedCents(recoveryCount);
    if(cents===null){
      setupError='Εισαγάγετε τα πραγματικά μετρημένα μετρητά σε ευρώ.';
      return;
    }
    const expectedOwner=owner,expectedActor=actorId,expectedShift=shift.id;
    const fingerprint=JSON.stringify([expectedOwner,expectedActor,register.id,
      expectedShift,cents,recoveryReason]);
    if(recoveryFingerprint!==fingerprint||!uuid(recoveryRequestId)){
      recoveryRequestId=crypto.randomUUID();
      recoveryFingerprint=fingerprint;
    }
    const requestId=recoveryRequestId,reasonCode=recoveryReason;
    writing=true;setupError='';notice='';
    try{
      const response=await fetch('/api/admin/pos/sites/shifts/'+encodeURIComponent(expectedShift)+
        '/recover-close?'+query(expectedOwner),{
          method:'POST',credentials:'same-origin',cache:'no-store',
          headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
          body:JSON.stringify({requestId,countedCashCents:cents,reasonCode})
        });
      if(owner!==expectedOwner||actorId!==expectedActor||
        selectedTerminal?.open_shift?.id!==expectedShift)return;
      if(responseScope(response.status))return;
      if(!response.ok)throw Error(response.status===409
        ?'Η βάρδια έχει εκκρεμείς συναλλαγές ή έχει ήδη κλείσει. Ελέγξτε τη συμφωνία.'
        :'Δεν επιτρέπεται ασφαλές κλείσιμο. Βεβαιωθείτε ότι ο ταμίας είναι απενεργοποιημένος.');
      const raw:unknown=await response.json();
      if(!raw||typeof raw!=='object'||Array.isArray(raw))
        throw Error('Μη επαληθευμένη απάντηση ανάκτησης βάρδιας.');
      const v=raw as Record<string,unknown>;
      if(v.organization_id!==expectedOwner||v.register_id!==register.id||
        v.shift_id!==expectedShift||v.cashier_id!==shift.cashier_id||
        v.closed_by!==expectedActor||v.reason!==reasonCode||
        v.counted_cash_cents!==cents||
        !Number.isSafeInteger(v.expected_cash_cents)||Number(v.expected_cash_cents)<0||
        !Number.isSafeInteger(v.variance_cents)||
        v.variance_cents!==cents-Number(v.expected_cash_cents)||
        v.requires_review!==(v.variance_cents!==0)||
        typeof v.replay!=='boolean')
        throw Error('Η επιβεβαίωση κλεισίματος απέτυχε.');
      notice='Καταγράφηκε ασφαλές κλείσιμο βάρδιας από Super Admin. '+
        (v.requires_review
          ?'Διαφορά '+euro(Number(v.variance_cents))+' · απαιτείται ανθρώπινος οικονομικός έλεγχος.'
          :'Τα μετρημένα μετρητά συμφωνούν με την αναμενόμενη κατάσταση.');
      recoveryCount='';recoveryConfirmed=false;recoveryRequestId='';recoveryFingerprint='';
      refresh++;
    }catch(e){
      if(owner===expectedOwner&&actorId===expectedActor)
        setupError=e instanceof Error?e.message:'Η ανάκτηση βάρδιας απέτυχε.';
    }finally{writing=false;}
  }

  async function write(kind:'site'|'terminal'|'operator',enabled=true){
    if(!browser||!canProvision||!setup||writing)return;
    const expectedOwner=owner,expectedActor=actorId;
    let path='/api/admin/pos/sites/';
    let method:'POST'|'PUT'='POST';
    let body:Record<string,unknown>;
    if(kind==='site'){
      if(expectedOwner!=='lefteria_fc_academy'||!uuid(academyLocationId)||
         !setup.academy_locations.some(l=>l.academy_location_id===academyLocationId&&!l.pos_location_id))return;
      path+='academy-locations';
      body={academyLocationId};
    }else if(kind==='terminal'){
      if(!sites?.sites.some(s=>s.pos_location_id===siteId)||!terminalName.trim()||
         terminalName!==terminalName.trim()||terminalName.length>140)return;
      path+='terminals';body={locationId:siteId,label:terminalName};
    }else{
      if(!selectedTerminal||!uuid(accountId))return;
      path+='terminals/'+encodeURIComponent(terminalId)+'/operators';
      method='PUT';body={adminAccountId:accountId,enabled};
    }
    writing=true;setupError='';notice='';
    try{
      const response=await fetch(path+'?'+query(expectedOwner),{
        method,credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify(body)
      });
      if(owner!==expectedOwner||actorId!==expectedActor)return;
      if(responseScope(response.status))return;
      if(!response.ok)throw Error(response.status===409?'Η καταχώριση άλλαξε. Ανανέωση απαιτείται.':
        'Η ενέργεια απορρίφθηκε. Ελέγξτε τη σωστή πόλη, τοποθεσία και ανάθεση δικαιωμάτων.');
      const raw:unknown=await response.json();
      if(!raw||typeof raw!=='object'||Array.isArray(raw)||
         (raw as Record<string,unknown>).organization_id!==expectedOwner)
        throw Error('Η επιβεβαίωση αλλαγής δεν είναι έγκυρη.');
      terminalName='';
      notice='Η αλλαγή επιβεβαιώθηκε από το API. Επαναφόρτωση πραγματικών δεδομένων…';
      refresh++;
    }catch(e){
      if(owner===expectedOwner&&actorId===expectedActor)
        setupError=e instanceof Error?e.message:'Η καταχώριση απέτυχε.';
    }finally{writing=false;}
  }
</script>

<section class="admin-panel pos-site-panel" data-testid="admin-pos-site-finances">
  <header class="pos-site-head">
    <div>
      <p class="admin-eyebrow">POS · ΠΟΛΗ → ΤΟΠΟΘΕΣΙΑ → ΤΑΜΕΙΟ</p>
      <h2>Οικονομικά ανά τοποθεσία</h2>
      <p class="admin-muted">Μόνο οι τοποθεσίες που επιτρέπονται στον λογαριασμό σας.
        Τα ποσά προέρχονται από επιβεβαιωμένα παραστατικά, όχι από προσομοίωση.</p>
    </div>
    <button class="admin-secondary" type="button" onclick={()=>refresh++} disabled={loading||writing}>
      Ανανέωση</button>
  </header>
  {#if loading}<p role="status">Επαλήθευση προσβάσιμων σημείων…</p>{/if}
  {#if error}<p role="alert">{error}</p>{/if}
  {#if sites}
    {#if sites.sites.length}
      <div class="pos-site-actions">
        <label>Πόλη και τοποθεσία
          <select bind:value={siteId} onchange={()=>{finances=null;reportError='';history=null;historyError='';}}>
            {#each sites.sites as site (site.pos_location_id)}
              <option value={site.pos_location_id}>{site.city_name} · {site.name}</option>
            {/each}
          </select>
        </label>
        <button class="admin-secondary" type="button" disabled={reportLoading||!selectedSite}
          onclick={()=>void viewFinance()}>{reportLoading?'Ανάκτηση…':'Προβολή οικονομικών'}</button>
        <button class="admin-secondary" type="button" disabled={historyLoading||!selectedSite}
          onclick={()=>void viewSales()}>{historyLoading?'Ανάκτηση…':'Πωλήσεις & παραγγελίες'}</button>
      </div>
    {:else}
      <p class="admin-muted">Δεν έχετε ανατεθειμένη τοποθεσία POS. Η διαχείριση πρόσβασης γίνεται από τον Super Admin.</p>
    {/if}
  {/if}
  {#if selectedSite}
    <AdminPosStockReconciliationPanel {owner} {actorId}
      locationId={selectedSite.pos_location_id} {onExpired} {onRevoked} />
  {/if}
  {#if reportError}<p role="alert">{reportError}</p>{/if}
  {#if finances}
    <div class="pos-finance-grid" aria-live="polite">
      <div><small>Ταμεία</small><strong>{finances.registers_total}</strong></div>
      <div><small>Πωλήσεις με απόδειξη</small><strong>{euro(finances.receipted_gross_cents)}</strong></div>
      <div><small>Επαληθευμένες επιστροφές</small><strong>{euro(finances.verified_refunds_cents)}</strong></div>
      <div><small>Καθαρό ποσό</small><strong>{euro(finances.receipted_net_after_refunds_cents)}</strong></div>
    </div>
    <p>Συναλλαγές: {finances.sales_total} · Εξοφλημένες: {finances.sales_paid} ·
      Εκκρεμείς: {finances.sales_pending} · Αποδείξεις: {finances.receipts_issued}</p>
    <p>Επιβεβαιωμένα μετρητά: {euro(finances.verified_cash_cents)} ·
      επιβεβαιωμένες κάρτες: {euro(finances.verified_card_cents)}</p>
    {#if finances.paid_without_receipt>0}
      <p role="alert">Απαιτείται έλεγχος: {finances.paid_without_receipt} εξοφλημένες συναλλαγές χωρίς απόδειξη.</p>
    {/if}
    <p class="admin-muted">Οι αναφορές POS δεν επιβεβαιώνουν καταχώριση στο γενικό λογιστικό.</p>
  {/if}
  {#if historyError}<p role="alert">{historyError}</p>{/if}
  {#if history}
    <div class="pos-sales-history" data-testid="admin-pos-site-sales-history">
      <h3>Πωλήσεις & παραγγελίες · έως 50 πρόσφατες</h3>
      {#if history.sales.length}
        <div class="pos-sales-scroll">
          <table>
            <thead><tr><th>Ημερομηνία</th><th>Ταμείο</th><th>Κατάσταση</th><th>Απόδειξη</th><th>Ποσό</th></tr></thead>
            <tbody>
              {#each history.sales as sale (sale.sale_id)}
                <tr>
                  <td>{formatDate(sale.created_at)}</td>
                  <td>{sale.register_label}</td>
                  <td>{statusLabel(sale.status)}</td>
                  <td>{sale.receipt_number??'—'}</td>
                  <td>{euro(sale.total_cents)}</td>
                </tr>
              {/each}
            </tbody>
          </table>
        </div>
      {:else}
        <p class="admin-muted">Δεν υπάρχουν πραγματικές συναλλαγές για αυτό το σημείο.</p>
      {/if}
      <p class="admin-muted">Η εμφάνιση εκκρεμούς συναλλαγής δεν σημαίνει επιβεβαιωμένη πληρωμή.</p>
    </div>
  {/if}
</section>

{#if canProvision}
  <section class="admin-panel pos-site-panel" data-testid="admin-pos-terminal-provisioning">
    <p class="admin-eyebrow">ΜΟΝΟ SUPER ADMIN · ΡΥΘΜΙΣΗ ΤΕΡΜΑΤΙΚΩΝ</p>
    <h2>POS ανά πόλη, τοποθεσία και χειριστή</h2>
    <p class="admin-muted">Κάθε ταμείο έχει ξεχωριστές αναθέσεις προσωπικού.
      Η καταχώριση δεν ενεργοποιεί πληρωμές ή φορολογικά παραστατικά.</p>
    {#if setupLoading}<p role="status">Φόρτωση καταχωρισμένων τερματικών…</p>{/if}
    {#if setupError}<p role="alert">{setupError}</p>{/if}
    {#if notice}<p role="status">{notice}</p>{/if}
    {#if setup}
      {#if owner==='lefteria_fc_academy'}
        <div class="pos-config-block">
          <h3>1. Σύνδεση με υπάρχουσα τοποθεσία Ακαδημίας</h3>
          <p class="admin-muted">Οι πόλεις και τα σημεία δημιουργούνται στην Ακαδημία. Εδώ συνδέεται το POS, χωρίς δεύτερο ανεξάρτητο μητρώο.</p>
          {#if setup.academy_locations.some(l=>!l.pos_location_id)}
            <label>Τοποθεσία
              <select bind:value={academyLocationId}>
                {#each setup.academy_locations.filter(l=>!l.pos_location_id) as location (location.academy_location_id)}
                  <option value={location.academy_location_id}>
                    {location.city_name} · {location.location_name}
                  </option>
                {/each}
              </select>
            </label>
            <button type="button" class="admin-secondary" disabled={writing||!uuid(academyLocationId)}
              onclick={()=>void write('site')}>Σύνδεση τοποθεσίας POS</button>
          {:else}
            <p class="admin-muted">Όλες οι διαθέσιμες τοποθεσίες έχουν συνδεθεί ή δεν υπάρχουν ενεργές τοποθεσίες Ακαδημίας.</p>
          {/if}
        </div>
      {/if}
      <div class="pos-config-block">
        <h3>2. Δημιουργία ταμείου</h3>
        {#if sites?.sites.length}
          <label>Τοποθεσία
            <select bind:value={siteId}>
              {#each sites.sites as site (site.pos_location_id)}
                <option value={site.pos_location_id}>{site.city_name} · {site.name}</option>
              {/each}
            </select>
          </label>
          <label>Όνομα τερματικού
            <input bind:value={terminalName} maxlength="140" placeholder="π.χ. Κεντρικό ταμείο" />
          </label>
          <button type="button" class="admin-secondary" disabled={writing||!terminalName.trim()}
            onclick={()=>void write('terminal')}>Δημιουργία ταμείου</button>
        {:else}
          <p class="admin-muted">Συνδέστε πρώτα μια πραγματική τοποθεσία POS.</p>
        {/if}
      </div>
      <div class="pos-config-block">
        <h3>3. Ανάθεση εξουσιοδοτημένου χειριστή</h3>
        {#if setup.terminals.length&&setup.operator_accounts.length}
          <label>Ταμείο
            <select bind:value={terminalId}>
              {#each setup.terminals as terminal (terminal.id)}
                <option value={terminal.id}>{terminal.location_name} · {terminal.label}</option>
              {/each}
            </select>
          </label>
          <label>Προσωπικό
            <select bind:value={accountId}>
              {#each setup.operator_accounts as account (account.id)}
                <option value={account.id}>{account.name} · {account.role}</option>
              {/each}
            </select>
          </label>
          <p class="admin-muted">Απαιτείται επιπλέον ενεργό δικαίωμα στην ίδια πόλη/τοποθεσία από τη διαχείριση προσωπικού.</p>
          <div class="pos-config-buttons">
            <button type="button" class="admin-secondary" disabled={writing||selectedAccount?.active}
              onclick={()=>void write('operator',true)}>Εξουσιοδότηση</button>
            <button type="button" class="admin-secondary" disabled={writing||!selectedAccount?.active}
              onclick={()=>void write('operator',false)}>Ανάκληση</button>
          </div>
          {#if selectedTerminal}
            <p class="admin-muted">Ενεργοί χειριστές: {selectedTerminal.operators.filter(o=>o.active).map(o=>o.name).join(', ')||'Κανένας'}</p>
          {/if}
        {:else}
          <p class="admin-muted">Χρειάζεται ένα ταμείο και λογαριασμός διαχειριστή με συγκεκριμένη ανάθεση τοποθεσίας.</p>
        {/if}
      </div>
      <div class="pos-config-block" data-testid="pos-shift-supervisor-recovery">
        <h3>4. Ασφαλής ανάκτηση βάρδιας</h3>
        <p class="admin-muted">Αποκλειστικά για Super Admin όταν ο συγκεκριμένος
          ταμίας έχει απενεργοποιηθεί. Απαιτείται φυσική καταμέτρηση,
          αιτιολόγηση και μηδενικές εκκρεμείς συναλλαγές. Δεν δημιουργείται
          πληρωμή, απόδειξη ή πλασματική συμφωνία.</p>
        {#if selectedTerminal?.open_shift}
          <p class="admin-muted">
            Βάρδια: {selectedTerminal.open_shift.cashier_name} ·
            {formatDate(selectedTerminal.open_shift.opened_at)} ·
            αρχικό ποσό {euro(selectedTerminal.open_shift.opening_cash_cents)}
          </p>
          {#if selectedTerminal.open_shift.cashier_active}
            <p role="status">Ο ταμίας παραμένει ενεργός. Πρώτα απαιτείται
              εξουσιοδοτημένη απενεργοποίηση του λογαριασμού μέσω Διαχείρισης Χρηστών.</p>
          {:else}
            <form class="pos-recovery-form" onsubmit={event=>{event.preventDefault();void recoverShift();}}>
              <label>Φυσικά μετρημένα μετρητά (€)
                <input type="text" inputmode="decimal" maxlength="10"
                  bind:value={recoveryCount} placeholder="π.χ. 12,00" required disabled={writing}/>
              </label>
              <label>Λόγος έκτακτου κλεισίματος
                <select bind:value={recoveryReason} disabled={writing}>
                  <option value="account_suspended">Απενεργοποιημένος λογαριασμός</option>
                  <option value="security_incident">Περιστατικό ασφαλείας</option>
                  <option value="operator_unavailable">Μη διαθέσιμος χειριστής</option>
                </select>
              </label>
              <label class="pos-recovery-confirm">
                <input type="checkbox" bind:checked={recoveryConfirmed} disabled={writing}/>
                Επιβεβαιώνω ότι καταμέτρησα φυσικά τα μετρητά και έλεγξα τις εκκρεμότητες.
              </label>
              <button type="submit" class="admin-secondary"
                disabled={writing||!recoveryConfirmed||countedCents(recoveryCount)===null}>
                {writing?'Καταχώριση…':'Καταγραφή έκτακτου κλεισίματος'}
              </button>
            </form>
          {/if}
        {:else}
          <p class="admin-muted">Δεν υπάρχει ανοιχτή βάρδια στο επιλεγμένο τερματικό.</p>
        {/if}
      </div>
    {/if}
  </section>
  {#if canProvision}
    <AdminPosRecoveryReviewPanel {owner} {actorId} {refresh} {onExpired} {onRevoked}/>
  {/if}
{/if}

<style>
  .pos-site-panel{margin-top:18px;display:grid;gap:13px}
  .pos-site-head{display:flex;gap:14px;align-items:start;justify-content:space-between;flex-wrap:wrap}
  .pos-site-panel h2,.pos-site-panel h3{margin:0}
  .pos-site-actions{display:flex;align-items:end;flex-wrap:wrap;gap:12px}
  .pos-site-panel label{display:grid;gap:7px;min-width:min(100%,230px);font-size:.88rem}
  .pos-site-panel select,.pos-site-panel input{padding:11px 12px;border:1px solid #57514b;border-radius:9px;background:#161616;color:#f6f1e8;width:100%}
  .pos-finance-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(160px,1fr));gap:9px}
  .pos-finance-grid div{display:grid;gap:6px;border:1px solid #4b4130;border-radius:12px;padding:14px;background:#211e19}
  .pos-finance-grid small{color:#d7d0c3}
  .pos-finance-grid strong{font-size:1.4rem;font-variant-numeric:tabular-nums}
  .pos-config-block{display:grid;justify-items:start;gap:12px;padding:16px;border:1px solid #444;border-radius:12px}
  .pos-config-buttons{display:flex;gap:8px;flex-wrap:wrap}
  .pos-recovery-form{display:grid;gap:12px;width:min(100%,460px)}
  .pos-recovery-confirm{display:flex!important;flex-direction:row;align-items:start;gap:10px}
  .pos-recovery-confirm input{width:auto!important;min-width:auto;margin-top:4px}
  .pos-sales-history{display:grid;gap:10px}
  .pos-sales-scroll{overflow-x:auto;max-width:100%}
  .pos-sales-scroll table{width:100%;border-collapse:collapse;font-size:.87rem}
  .pos-sales-scroll th,.pos-sales-scroll td{text-align:left;padding:10px;border-bottom:1px solid #46413a;white-space:nowrap}
  .pos-sales-scroll th{color:#dfcc9d}
  @media(max-width:600px){.pos-site-actions,.pos-site-actions label{width:100%}.pos-site-panel select,.pos-site-panel input{max-width:100%}}
</style>
