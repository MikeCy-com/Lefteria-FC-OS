<script lang="ts">
  import { untrack } from 'svelte';
  import type { MobileAuthController } from '../controller.ts';
  import { validParentTrainingFees, payablHostedLink,
    type ParentTrainingFees } from '../finance.ts';
  import './mobile-theme.css';

  let { controller }: {controller:MobileAuthController}=$props();
  let status=$state<'loading'|'ready'|'error'>('loading');
  let snapshot=$state<ParentTrainingFees|null>(null);
  let message=$state('');
  let saving=$state(false);
  let downloading=$state<string|null>(null);
  let refresh=$state(0);
  let day=$state(5);
  let saveCard=$state(false);
  let automatic=$state(false);
  let initialized=$state(false);

  const money=(cents:number)=>new Intl.NumberFormat('el-CY',
    {style:'currency',currency:'EUR'}).format(cents/100);

  $effect(()=>{
    void refresh;
    let valid=true;
    status='loading';
    untrack(()=>{
      void controller.getTrainingFees().then(result=>{
        if(!valid)return;
        if(!validParentTrainingFees(result))throw new Error('Μη επαληθευμένα στοιχεία χρεώσεων.');
        snapshot=result;
        if(!initialized){
          day=result.preferred_day;saveCard=result.save_card_requested;
          automatic=result.automatic_collection_requested;initialized=true;
        }
        status='ready';
      }).catch(error=>{
        if(!valid)return;
        status='error';snapshot=null;
        message=error instanceof Error?error.message:'Η οικονομική καρτέλα δεν είναι διαθέσιμη.';
      });
    });
    return()=>{valid=false;};
  });

  async function downloadPdf(ids:string[]) {
    if(downloading||ids.length<1||ids.length>20)return;
    downloading=ids.join(',');message='';
    try{
      const file=await controller.downloadTrainingInvoice(ids);
      const url=URL.createObjectURL(file);
      const anchor=document.createElement('a');
      anchor.href=url;
      anchor.download=ids.length===1?'invoice-'+ids[0].slice(0,8)+'.pdf':
        'family-invoice-'+ids.length+'.pdf';
      document.body.appendChild(anchor);anchor.click();anchor.remove();
      window.setTimeout(()=>URL.revokeObjectURL(url),30000);
    }catch(error){message=error instanceof Error?error.message:'Το PDF δεν είναι διαθέσιμο.';}
    finally{downloading=null;}
  }
  async function savePreference(event:SubmitEvent){
    event.preventDefault();
    if(saving||!Number.isInteger(day)||day<1||day>31||
       (automatic&&!saveCard))return;
    saving=true;message='';
    try{
      const response=await controller.saveTrainingFeePreferences(day,saveCard,automatic);
      if(!response||typeof response!=='object'||Array.isArray(response)||
         !('preferred_day' in response)||response.preferred_day!==day||
         !('automatic_collection_active' in response)||
         response.automatic_collection_active!==false)
        throw new Error('Δεν επιβεβαιώθηκαν οι προτιμήσεις.');
      message='Οι προτιμήσεις καταχωρίστηκαν. Αυτόματες χρεώσεις δεν είναι ενεργές.';
      refresh+=1;
    }catch(error){
      message=error instanceof Error?error.message:'Η αποθήκευση δεν ολοκληρώθηκε.';
    }finally{saving=false;}
  }
</script>

<section class="parent-fees" aria-label="Οικονομικά Ακαδημίας" data-testid="parent-training-fees">
  <header class="fee-heading">
    <p>ΛΕΥΤΕΡΙΑ FC ACADEMY</p><h1>Οικονομικά & πληρωμές</h1>
    <span>Χρεώσεις μόνο για μήνες που έχει εκδώσει η Ακαδημία.</span>
  </header>
  {#if status==='loading'}
    <p class="fee-notice" role="status">Έλεγχος οφειλών και επαληθευμένης κηδεμονίας…</p>
  {:else if status==='error'}
    <p class="fee-notice" role="alert">{message}</p>
    <button class="fee-secondary" type="button" onclick={()=>refresh+=1}>Δοκιμή ξανά</button>
  {:else if snapshot}
    <div class="fee-summary">
      <div><small>Εκκρεμές ποσό</small>
        <strong>{money(snapshot.fees.filter(f=>f.status==='issued')
          .reduce((sum,f)=>sum+f.amount_cents,0))}</strong></div>
      <div><small>Χρεωμένοι μήνες</small><strong>{snapshot.fees.length}</strong></div>
    </div>
    <section class="fee-card" data-testid="issued-training-months">
      <div class="fee-card-title"><h2>Χρεώσεις ανά παιδί</h2>
        <button class="fee-secondary" type="button" onclick={()=>refresh+=1}>Ανανέωση</button></div>
      {#if snapshot.fees.length>1}
        <button class="fee-secondary" type="button" disabled={downloading!==null}
          onclick={()=>void downloadPdf(snapshot.fees.slice(0,20).map(fee=>fee.id))}
          data-testid="parent-download-family-invoice">
          {downloading?'Προετοιμασία PDF…':'Λήψη οικογενειακού PDF'+(snapshot.fees.length>20?' · 20 πρώτα':'')}
        </button>
      {/if}
      {#if snapshot.fees.length===0}
        <p class="fee-notice">Δεν έχουν εκδοθεί μηνιαίες χρεώσεις για αυτόν τον λογαριασμό.</p>
      {:else}
        <ul class="fee-list">
          {#each snapshot.fees as fee (fee.id)}
            <li>
              <div class="fee-row-title">
                <div><strong>{fee.child_name}</strong><small>Προπόνηση {fee.fee_month.slice(0,7)}
                  · Προτεινόμενη ημερομηνία {fee.due_date}</small></div>
                <strong>{money(fee.amount_cents)}</strong>
              </div>
              {#if fee.status==='paid'}
                <p class="fee-status paid">Εξοφλήθηκε · επιβεβαιωμένη συναλλαγή</p>
              {:else if fee.status==='review_required'||fee.payment_status==='review_required'}
                <p class="fee-status review">Αναμονή ελέγχου από την Ακαδημία</p>
              {:else if fee.payment_status==='awaiting_settlement'}
                <p class="fee-status review">Σε αναμονή εκκαθάρισης · δεν έχει εξοφληθεί</p>
              {:else}
                <p class="fee-status due">Οφειλή σε εκκρεμότητα</p>
              {/if}
              <button type="button" class="fee-secondary" disabled={downloading!==null}
                onclick={()=>void downloadPdf([fee.id])}
                data-testid="parent-download-invoice">
                {downloading===fee.id?'Λήψη…':'Λήψη PDF τιμολογίου / απόδειξης'}
              </button>
              {#if fee.status==='issued'&&fee.payment_status==='ready'&&payablHostedLink(fee.checkout_url)}
                <a class="fee-pay" href={fee.checkout_url} target="_blank" rel="noopener noreferrer"
                  data-testid="parent-fee-click-to-pay">Πληρωμή με κάρτα · payabl.</a>
              {:else if fee.status==='issued'&&fee.payment_status==='not_created'}
                <p class="fee-notice">Ο ασφαλής σύνδεσμος πληρωμής δεν έχει εκδοθεί ακόμη.</p>
              {/if}
            </li>
          {/each}
        </ul>
      {/if}
      <p class="fee-caption">Το άνοιγμα της σελίδας πληρωμής δεν θεωρείται εξόφληση.
        Το ποσό ενημερώνεται μόνον όταν επιβεβαιωθεί από την payabl.</p>
    </section>

    <section class="fee-card">
      <h2>Προτιμήσεις μηνιαίων πληρωμών</h2>
      <form onsubmit={savePreference} class="fee-form">
        <label for="fee-preferred-day">Επιθυμητή ημέρα κάθε μήνα</label>
        <input id="fee-preferred-day" type="number" min="1" max="31" step="1"
          bind:value={day} disabled={saving} required/>
        <p class="fee-caption">Για μήνες με λιγότερες ημέρες επιλέγεται η τελευταία ημέρα του μήνα.
          Η επιλογή δεν δημιουργεί νέες χρεώσεις.</p>
        <label class="fee-check"><input type="checkbox" bind:checked={saveCard} disabled={saving}/>
          Θέλω να αποθηκεύσω κάρτα με ασφάλεια στην payabl. όταν ενεργοποιηθεί η υπηρεσία.</label>
        <label class="fee-check"><input type="checkbox" bind:checked={automatic} disabled={saving||!saveCard}/>
          Αιτούμαι αυτόματη εξόφληση μόνο για μήνες που εκδίδει η Ακαδημία,
          την επιλεγμένη ημερομηνία, μετά από τη ρητή ενεργοποίηση της υπηρεσίας.</label>
        <p class="fee-notice">Οι κάρτες φυλάσσονται μόνο από την payabl., όχι από το σωματείο.
          Οι αυτόματες χρεώσεις <strong>δεν είναι ενεργές</strong>. Όταν υποστηρίζεται
          η αποθήκευση κάρτας από τον πάροχο, η επιλογή θα εμφανίζεται στην ασφαλή
          σελίδα πληρωμής.</p>
        <button class="fee-pay" type="submit" disabled={saving||!Number.isInteger(day)||day<1||day>31||automatic&&!saveCard}>
          {saving?'Αποθήκευση…':'Αποθήκευση προτιμήσεων'}</button>
        {#if message}<p class="fee-caption" role="status">{message}</p>{/if}
      </form>
    </section>
  {/if}
</section>

<style>
  .parent-fees{max-width:820px;margin:0 auto;color:var(--lf-text,#f8f8f8)}
  .fee-heading p{color:var(--lf-gold,#d6aa52);font-size:12px;letter-spacing:.13em;margin:0 0 7px}
  .fee-heading h1{font-family:var(--lf-heading);font-size:clamp(26px,6vw,36px);margin:0 0 5px}
  .fee-heading span{display:block;color:var(--lf-muted,#a4a4a4);font-size:14px}
  .fee-summary{display:grid;grid-template-columns:1fr 1fr;gap:12px;margin:22px 0}
  .fee-summary>div{padding:20px;border-radius:14px;background:linear-gradient(120deg,#151918,#101213);border:1px solid #51432b}
  .fee-summary small{display:block;color:#aeb6b5;margin-bottom:9px}
  .fee-summary strong{font-size:25px;font-family:var(--lf-heading)}
  .fee-card{padding:20px 18px;margin:18px 0;border:1px solid #4e4437;background:#121718;border-radius:16px}
  .fee-card h2{margin:0 0 14px;font-family:var(--lf-heading);font-size:23px}
  .fee-card-title{display:flex;justify-content:space-between;gap:15px;align-items:center}
  .fee-list{list-style:none;display:grid;gap:12px;padding:0;margin:10px 0}
  .fee-list li{padding:16px;background:#0b0f10;border:1px solid #313b38;border-radius:11px}
  .fee-row-title{display:flex;justify-content:space-between;gap:12px;align-items:flex-start}
  .fee-row-title strong{font-size:16px}
  .fee-row-title small{display:block;color:#9ca9a4;font-size:12px;margin-top:5px}
  .fee-status{font-size:13px;margin:14px 0}.paid{color:#a8dfbc}.review{color:#ffc980}.due{color:#d4d4d4}
  .fee-pay{display:inline-block;text-decoration:none;padding:13px 18px;background:var(--lf-gold,#d5ac56);
    color:#101310;border:0;border-radius:9px;font-weight:700;cursor:pointer}
  .fee-pay:disabled{opacity:.45;cursor:not-allowed}
  .fee-secondary{padding:8px 13px;background:transparent;color:#f4e4be;border:1px solid #806a40;border-radius:7px;cursor:pointer}
  .fee-notice{color:#d0d3d3;font-size:14px}.fee-caption{color:#aeb6b4;font-size:12px;line-height:1.5}
  .fee-form{display:grid;gap:11px}.fee-form>label:not(.fee-check){font-weight:700;font-size:14px}
  .fee-form>input{width:100px;background:#0d1112;color:#fff;padding:11px;border:1px solid #7a725e;border-radius:8px}
  .fee-check{display:flex;gap:12px;align-items:flex-start;line-height:1.5;font-size:14px}
  .fee-check input{margin-top:5px;flex-shrink:0;width:17px;height:17px}
  @media(max-width:400px){.fee-summary{grid-template-columns:1fr}.fee-row-title{flex-direction:column}}
</style>
