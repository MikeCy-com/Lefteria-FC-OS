<script lang="ts">
  import { untrack } from 'svelte';

  type Bucket={issued_count:number;issued_cents:number;paid_cents:number;
    open_cents:number;review_cents:number;cancelled_count:number;cancelled_cents:number};
  type General={issued_count:number;issued_cents:number;collected_cents:number;
    outstanding_cents:number;overdue_count:number};
  type Payabl={request_count:number;confirmed_count:number;review_count:number};
  type Overview={organization_id:string;month:string;currency:'EUR';
    scope:'organization';is_statutory_statement:false;
    academy_training_fees:Bucket;general_charges:General;payabl_sandbox:Payabl};
  let {owner,sessionId}:{owner:string|null;sessionId:string}=$props();
  const now=new Intl.DateTimeFormat('en-GB',{timeZone:'Asia/Nicosia',
    year:'numeric',month:'2-digit'}).formatToParts(new Date());
  let month=$state((now.find(p=>p.type==='year')?.value??'2026')+'-'+
    (now.find(p=>p.type==='month')?.value??'11'));
  let reload=$state(0);
  let loading=$state(false);
  let error=$state('');
  let data=$state<Overview|null>(null);
  const euro=(cents:number)=>new Intl.NumberFormat('el-CY',
    {style:'currency',currency:'EUR'}).format(cents/100);
  const keys=(value:unknown,required:readonly string[]):value is Record<string,number>=>
    value!==null && typeof value==='object' && !Array.isArray(value) &&
    required.every(key=>{
      const number=(value as Record<string,unknown>)[key];
      return typeof number==='number'&&Number.isSafeInteger(number)&&number>=0;
    });
  function verified(value:unknown,organization:string,period:string):value is Overview{
    if(!value||typeof value!=='object'||Array.isArray(value))return false;
    const d=value as Record<string,unknown>;
    return d.organization_id===organization&&d.month===period+'-01'&&
      d.currency==='EUR'&&d.scope==='organization'&&d.is_statutory_statement===false&&
      keys(d.academy_training_fees,['issued_count','issued_cents','paid_cents','open_cents','review_cents','cancelled_count','cancelled_cents'])&&
      keys(d.general_charges,['issued_count','issued_cents','collected_cents',
        'outstanding_cents','overdue_count'])&&
      keys(d.payabl_sandbox,['request_count','confirmed_count','review_count']);
  }
  $effect(()=>{
    const selected=owner,period=month,actor=sessionId,refresh=reload;
    void actor;void refresh;
    data=null;error='';loading=false;
    if(!selected||!/^20\d{2}-(0[1-9]|1[0-2])$/.test(period))return;
    const ctrl=new AbortController();
    loading=true;
    untrack(()=>{void(async()=>{
      try{
        const response=await fetch('/api/admin/finance/overview?organization_id='+
          encodeURIComponent(selected)+'&month='+encodeURIComponent(period),{
            credentials:'same-origin',cache:'no-store',signal:ctrl.signal});
        if(!response.ok)throw new Error(response.status===403
          ?'Δεν επιτρέπεται η συνολική οικονομική προβολή για αυτή την οντότητα.'
          :'Η οικονομική αναφορά δεν είναι διαθέσιμη.');
        const json:unknown=await response.json();
        if(!verified(json,selected,period))throw new Error('Η απάντηση δεν επαληθεύτηκε.');
        if(!ctrl.signal.aborted&&owner===selected&&month===period)data=json;
      }catch(e){
        if(!ctrl.signal.aborted)error=e instanceof Error?e.message:'Αποτυχία αναφοράς.';
      }finally{if(!ctrl.signal.aborted)loading=false;}
    })();});
    return()=>ctrl.abort();
  });
</script>

<section class="admin-panel" data-testid="accounting-overview">
  <h2>Οικονομική εικόνα · ανά οργανισμό</h2>
  <p class="admin-muted">
    Επαληθευμένα σύνολα από ξεχωριστά μητρώα. Δεν αποτελούν ενοποιημένο ισοζύγιο,
    κατάσταση αποτελεσμάτων ή φορολογική αναφορά και δεν προστίθενται μεταξύ τους.
  </p>
  <label for="accounting-month">Περίοδος αναφοράς</label>
  <input id="accounting-month" type="month" min="2020-01" max="2100-12" bind:value={month}/>
  {#if loading}
    <p role="status">Ανάκτηση οικονομικών στοιχείων…</p>
  {:else if error}
    <p role="alert">{error}</p>
    <button type="button" class="admin-secondary" onclick={()=>reload++}>Επανάληψη</button>
  {:else if data}
    {#if owner==='lefteria_fc_academy'}
      <h3>Ακαδημία · μηνιαία δίδακτρα</h3>
      <div class="accounting-cards">
        <div><strong>{euro(data.academy_training_fees.issued_cents)}</strong><span>Εκδοθέντα · {data.academy_training_fees.issued_count}</span></div>
        <div><strong>{euro(data.academy_training_fees.paid_cents)}</strong><span>Επαληθευμένα εξοφλημένα</span></div>
        <div><strong>{euro(data.academy_training_fees.open_cents)}</strong><span>Ανοικτές χρεώσεις</span></div>
        <div><strong>{euro(data.academy_training_fees.review_cents)}</strong><span>Προς συμφωνία</span></div>
        <div data-testid="academy-fees-cancelled-stat"><strong>{euro(data.academy_training_fees.cancelled_cents)}</strong><span>Ακυρωμένες χρεώσεις · {data.academy_training_fees.cancelled_count} (δεν υπολογίζονται ως έσοδα ή οφειλές)</span></div>
      </div>
    {/if}
    <h3>Λοιπές χρεώσεις · εκδοθείσες στην περίοδο</h3>
    <div class="accounting-cards">
      <div><strong>{euro(data.general_charges.issued_cents)}</strong><span>Εκδοθείσες · {data.general_charges.issued_count}</span></div>
      <div><strong>{euro(data.general_charges.collected_cents)}</strong><span>Καταγεγραμμένες εισπράξεις</span></div>
      <div><strong>{euro(data.general_charges.outstanding_cents)}</strong><span>Υπόλοιπο</span></div>
      <div><strong>{data.general_charges.overdue_count}</strong><span>Ληξιπρόθεσμες χρεώσεις</span></div>
    </div>
    <h3>Payabl · δοκιμαστικές συναλλαγές</h3>
    <p>Αιτήματα: <strong>{data.payabl_sandbox.request_count}</strong> ·
      Επιβεβαιωμένα στον πάροχο: <strong>{data.payabl_sandbox.confirmed_count}</strong> ·
      Προς έλεγχο: <strong>{data.payabl_sandbox.review_count}</strong>.</p>
    <p class="admin-muted">Οι αιτήσεις Payabl ενδέχεται να αφορούν τις παραπάνω χρεώσεις.
      Δεν μετρούνται ξανά ως ξεχωριστό έσοδο. Τα έξοδα, η μισθοδοσία και η
      συμφωνία τραπεζών δεν έχουν ακόμη συνδεθεί σε αυτό το αναγνωστικό σύνολο.</p>
    <button type="button" class="admin-secondary" onclick={()=>reload++}>Ανανέωση δεδομένων</button>
  {/if}
</section>

<style>
  .accounting-cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(11rem,1fr));gap:.75rem;margin:.8rem 0 1.6rem}
  .accounting-cards>div{border:1px solid var(--border-color,#9ca3af);border-radius:.7rem;
    padding:1rem;display:flex;flex-direction:column;gap:.5rem}
  .accounting-cards strong{font-size:1.3rem;font-variant-numeric:tabular-nums}
  .accounting-cards span{font-size:.85rem;opacity:.8}
  input[type="month"]{display:block;max-width:16rem;width:100%;padding:.7rem;margin:.5rem 0 1rem;
    border:1px solid var(--border-color,#9ca3af);border-radius:.5rem;color:inherit;
    background:var(--surface,#fff)}
</style>
