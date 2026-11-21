<script lang="ts">
  import {browser} from '$app/env';

  type Site={
    city_id:string;location_id:string;bill_count:number;
    gross_amount_cents:number;approved_credit_cents:number;
    pending_credit_cents:number;net_payables_cents:number;
    approved_manual_payment_cents:number;pending_payment_evidence_cents:number;
    remaining_cents:number;
  };
  type Totals={
    organization_id:'lefteria_fc_academy';month:string;currency:'EUR';
    reporting_basis:'supplier_bill_date';bank_verified:false;
    is_cash_payment:false;posted_to_general_ledger:false;locations:Site[];
  };
  let {actorId,cityId,locationId}:{
    actorId:string;cityId:string;locationId:string
  }=$props();
  const parts=new Intl.DateTimeFormat('en-GB',{timeZone:'Asia/Nicosia',
    year:'numeric',month:'2-digit'}).formatToParts(new Date());
  let month=$state((parts.find(p=>p.type==='year')?.value??'2026')+'-'+
    (parts.find(p=>p.type==='month')?.value??'11'));
  let data=$state<Totals|null>(null);
  let loading=$state(false);
  let error=$state('');
  let reload=$state(0);
  const euro=(cents:number)=>new Intl.NumberFormat('el-CY',
    {style:'currency',currency:'EUR'}).format(cents/100);
  const site=$derived(data?.locations.find(s=>
    s.city_id===cityId&&s.location_id===locationId));
  // City summaries include only locations already allowed by the server.
  // A location manager must never see totals for another city/site.
  const city=$derived((data?.locations??[]).filter(s=>s.city_id===cityId));
  const safe=(x:unknown)=>typeof x==='number'&&Number.isSafeInteger(x)&&x>=0;
  const uuid=(x:unknown)=>typeof x==='string'&&
    /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(x);
  const keys=new Set([
    'city_id','location_id','bill_count','gross_amount_cents',
    'approved_credit_cents','pending_credit_cents',
    'net_payables_cents','approved_manual_payment_cents',
    'pending_payment_evidence_cents','remaining_cents'
  ]);
  const fields=['bill_count','gross_amount_cents','approved_credit_cents',
    'pending_credit_cents','net_payables_cents',
    'approved_manual_payment_cents','pending_payment_evidence_cents',
    'remaining_cents'] as const;
  const sum=(key:typeof fields[number])=>
    city.reduce((total,entry)=>total+entry[key],0);

  $effect(()=>{
    const actor=actorId,period=month,rev=reload;
    void rev;data=null;error='';loading=false;
    if(!browser||!actor||!/^(20[2-9][0-9]|2100)-(0[1-9]|1[0-2])$/.test(period))return;
    const ctrl=new AbortController();
    loading=true;
    void(async()=>{
      try{
        const response=await fetch(
          '/api/admin/football/territories/finance/supplier-payables?'+
          'organization_id=lefteria_fc_academy&month='+encodeURIComponent(period),{
            credentials:'same-origin',cache:'no-store',signal:ctrl.signal});
        if(!response.ok)throw new Error(response.status===403
          ?'Η πρόσβαση στα οικονομικά αυτής της τοποθεσίας ανακλήθηκε.'
          :'Δεν είναι διαθέσιμη η αναφορά υποχρεώσεων τοποθεσίας.');
        const raw:unknown=await response.json();
        if(!raw||typeof raw!=='object'||Array.isArray(raw))
          throw new Error('Μη επαληθευμένη αναφορά.');
        const x=raw as Record<string,unknown>;
        if(x.organization_id!=='lefteria_fc_academy'||
          x.month!==period+'-01'||x.currency!=='EUR'||
          x.reporting_basis!=='supplier_bill_date'||x.approval_reversal_adjusted!==true||
          x.bank_verified!==false||x.is_cash_payment!==false||
          x.posted_to_general_ledger!==false||
          !Array.isArray(x.locations)||x.locations.length>3000||
          !x.locations.every((v:unknown)=>{
            if(!v||typeof v!=='object'||Array.isArray(v))return false;
            const row=v as Record<string,unknown>;
            return Object.keys(row).every(k=>keys.has(k))&&
              Object.keys(row).length===keys.size&&
              uuid(row.city_id)&&uuid(row.location_id)&&
              fields.every(field=>safe(row[field]))&&
              row.gross_amount_cents===
                (row.approved_credit_cents as number)+(row.net_payables_cents as number)&&
              row.net_payables_cents===
                (row.approved_manual_payment_cents as number)+(row.remaining_cents as number);
          }))throw new Error('Η αναφορά τοποθεσίας περιέχει μη έγκυρα ποσά ή μη επιτρεπτές λεπτομέρειες.');
        if(!ctrl.signal.aborted&&actorId===actor&&month===period)data=raw as Totals;
      }catch(e){if(!ctrl.signal.aborted)error=e instanceof Error?e.message:'Σφάλμα ανάγνωσης.';}
      finally{if(!ctrl.signal.aborted)loading=false;}
    })();
    return()=>ctrl.abort();
  });
</script>

<section class="academy-supplier-totals" data-testid="academy-site-supplier-totals">
  <h3>Προμηθευτές · υποχρεώσεις ανατεθειμένης τοποθεσίας</h3>
  <p class="admin-muted">
    Ασφαλή συγκεντρωτικά ποσά ανά τοποθεσία, χωρίς ονόματα προμηθευτών,
    αριθμούς τιμολογίων ή στοιχεία αποδεικτικών.
    Οι εγκεκριμένες αναστροφές εξαιρούνται από τα υπόλοιπα. Οι χειροκίνητες εγκρίσεις δεν είναι τραπεζική επιβεβαίωση,
    ταμειακή κίνηση ή εγγραφή γενικού καθολικού.
  </p>
  <label for="academy-site-payable-month">Μήνας έκδοσης τιμολογίων
    <input id="academy-site-payable-month" type="month"
      min="2020-01" max="2100-12" bind:value={month}/>
  </label>
  {#if loading}
    <p role="status">Ανάκτηση υποχρεώσεων…</p>
  {:else if error}
    <p role="alert">{error}</p>
    <button type="button" onclick={()=>reload++}>Επανάληψη</button>
  {:else if site}
    <div class="site-payable-cards">
      <div><span>Αρχικές μικτές υποχρεώσεις</span>
        <strong>{euro(site.gross_amount_cents)}</strong></div>
      <div><span>Εγκεκριμένα πιστωτικά</span>
        <strong>{euro(site.approved_credit_cents)}</strong></div>
      <div><span>Καθαρή οφειλή μετά πιστωτικά</span>
        <strong>{euro(site.net_payables_cents)}</strong></div>
      <div><span>Εγκεκριμένα χειροκίνητα τεκμήρια</span>
        <strong>{euro(site.approved_manual_payment_cents)}</strong></div>
      <div class="site-payable-remaining"><span>Υπόλοιπο μητρώου</span>
        <strong>{euro(site.remaining_cents)}</strong></div>
      <div><span>Σε αναμονή (πληρωμές / πιστωτικά)</span>
        <strong>{euro(site.pending_payment_evidence_cents)} /
          {euro(site.pending_credit_cents)}</strong></div>
    </div>
    <p class="admin-muted">{site.bill_count} τιμολόγια της επιλεγμένης περιόδου.
      Εκκρεμή τεκμήρια και πιστωτικά δεν αφαιρούνται από το υπόλοιπο.</p>
    {#if city.length>1}
      <div class="site-payable-city">
        <h4>Σύνολο μόνο των {city.length} ανατεθειμένων τοποθεσιών αυτής της πόλης</h4>
        <p><strong>{euro(sum('gross_amount_cents'))}</strong> αρχικές υποχρεώσεις,
          <strong>{euro(sum('approved_credit_cents'))}</strong> πιστωτικά,
          <strong>{euro(sum('approved_manual_payment_cents'))}</strong> χειροκίνητες εγκρίσεις,
          <strong>{euro(sum('remaining_cents'))}</strong> υπόλοιπο.</p>
      </div>
    {/if}
  {:else if data}
    <p>Δεν υπάρχουν εξουσιοδοτημένα δεδομένα για αυτή την τοποθεσία.</p>
  {/if}
</section>

<style>
  .academy-supplier-totals{
    border:1px solid var(--border-color,#9ca3af);
    padding:1.1rem;border-radius:.8rem;display:grid;gap:.8rem
  }
  .academy-supplier-totals h3{margin:0}
  .academy-supplier-totals label{display:grid;gap:.4rem}
  .academy-supplier-totals input{max-width:15rem;padding:.65rem;
    border:1px solid var(--border-color,#9ca3af);border-radius:.5rem;
    color:inherit;background:var(--surface,#fff)}
  .site-payable-cards{display:grid;grid-template-columns:
    repeat(auto-fit,minmax(min(100%,12.5rem),1fr));gap:.6rem}
  .site-payable-cards>div{display:grid;gap:.4rem;padding:.85rem;
    border-radius:.55rem;border:1px solid var(--border-color,#9ca3af)}
  .site-payable-cards span{font-size:.88rem;opacity:.8}
  .site-payable-cards strong{font-variant-numeric:tabular-nums;
    font-size:1.16rem;overflow-wrap:anywhere}
  .site-payable-remaining{border-width:2px!important}
  .site-payable-city{border-top:1px solid var(--border-color,#9ca3af);
    padding-top:.7rem}
  .site-payable-city h4{margin:0 0 .4rem}
  .site-payable-city p{line-height:1.6}
</style>
