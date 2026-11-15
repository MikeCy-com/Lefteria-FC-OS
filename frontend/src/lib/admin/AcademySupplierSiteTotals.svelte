<script lang="ts">
  import {browser} from '$app/env';
  type Site={city_id:string;location_id:string;bill_count:number;gross_amount_cents:number};
  type Totals={organization_id:'lefteria_fc_academy';month:string;
    currency:'EUR';is_cash_payment:false;locations:Site[]};
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
  const city=$derived((data?.locations??[]).filter(s=>s.city_id===cityId));
  const safe=(x:unknown)=>typeof x==='number'&&Number.isSafeInteger(x)&&x>=0;
  $effect(()=>{
    const actor=actorId,period=month,rev=reload;
    void rev; data=null;error='';loading=false;
    if(!browser||!actor||!/^(20[2-9][0-9]|2100)-(0[1-9]|1[0-2])$/.test(period))return;
    const ctrl=new AbortController();
    loading=true;
    void(async()=>{
      try{
        const response=await fetch(
          '/api/admin/football/territories/finance/supplier-bills?'+
          'organization_id=lefteria_fc_academy&month='+encodeURIComponent(period),{
            credentials:'same-origin',cache:'no-store',signal:ctrl.signal});
        if(!response.ok)throw new Error('Δεν είναι διαθέσιμες οι υποχρεώσεις προμηθευτών.');
        const raw:unknown=await response.json();
        if(!raw||typeof raw!=='object'||Array.isArray(raw))
          throw new Error('Μη επαληθευμένη αναφορά.');
        const x=raw as Record<string,unknown>;
        if(x.organization_id!=='lefteria_fc_academy'||
          x.month!==period+'-01'||x.currency!=='EUR'||
          x.is_cash_payment!==false||!Array.isArray(x.locations)||
          x.locations.length>3000||!x.locations.every((v:unknown)=>{
            if(!v||typeof v!=='object'||Array.isArray(v))return false;
            const row=v as Record<string,unknown>;
            return typeof row.city_id==='string'&&typeof row.location_id==='string'&&
              safe(row.bill_count)&&safe(row.gross_amount_cents);
          }))throw new Error('Μη έγκυρα σύνολα προμηθευτών.');
        if(!ctrl.signal.aborted&&actorId===actor&&month===period)data=raw as Totals;
      }catch(e){if(!ctrl.signal.aborted)error=e instanceof Error?e.message:'Σφάλμα ανάγνωσης.';}
      finally{if(!ctrl.signal.aborted)loading=false;}
    })();
    return()=>ctrl.abort();
  });
</script>

<section class="academy-supplier-totals" data-testid="academy-site-supplier-totals">
  <h3>Υποχρεώσεις προμηθευτών · μόνο ανατεθειμένη τοποθεσία</h3>
  <p>Μικτά ποσά καταχωρισμένων παραστατικών, όχι πληρωμές ή καθαρό αποτέλεσμα.
    Δεν προβάλλονται ονόματα προμηθευτών ή αριθμοί τιμολογίων.</p>
  <label>Μήνας παραστατικών
    <input type="month" min="2020-01" max="2100-12" bind:value={month}/>
  </label>
  {#if loading}
    <p role="status">Ανάκτηση υποχρεώσεων…</p>
  {:else if error}
    <p role="alert">{error}</p>
    <button type="button" onclick={()=>reload++}>Επανάληψη</button>
  {:else if site}
    <p><strong>{euro(site.gross_amount_cents)}</strong> ·
      {site.bill_count} παραστατικά αυτής της τοποθεσίας.</p>
    {#if city.length>1}
      <p>Ανατεθειμένες τοποθεσίες της πόλης: {city.length},
        {euro(city.reduce((n,s)=>n+s.gross_amount_cents,0))}.</p>
    {/if}
  {:else if data}
    <p>Δεν υπάρχουν εξουσιοδοτημένα δεδομένα προμηθευτών για αυτή την τοποθεσία.</p>
  {/if}
</section>

<style>
  .academy-supplier-totals{border:1px solid var(--border-color,#9ca3af);
    padding:1rem;border-radius:.6rem;display:grid;gap:.6rem}
  .academy-supplier-totals h3{margin:0}
  .academy-supplier-totals label{display:grid;gap:.4rem}
  .academy-supplier-totals input{max-width:15rem;padding:.6rem;
    border:1px solid var(--border-color,#9ca3af);border-radius:.5rem;
    color:inherit;background:var(--surface,#fff)}
</style>
