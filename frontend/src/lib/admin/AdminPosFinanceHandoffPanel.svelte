<script lang="ts">
  import {browser} from '$app/environment';
  type Owner='lefteria_fc'|'lefteria_fc_academy';
  type Handoff={
    organization_id:Owner;
    posting_status:'awaiting_verified_finance_consumer';
    total_events:number;sale_receipt_events:number;
    verified_refund_events:number;sale_gross_cents:number;
    refund_gross_cents:number;net_event_cents:number;
    oldest_event_at:string|null;journal_entries_confirmed:0;
    finance_posting_active:false;
  };
  let {owner,actorId,onExpired,onRevoked}:{
    owner:Owner;actorId:string;onExpired:()=>void;onRevoked:()=>void
  }=$props();
  let report=$state<Handoff|null>(null);
  let loading=$state(false);
  let error=$state('');
  const euros=(amount:number)=>new Intl.NumberFormat('el-CY',
    {style:'currency',currency:'EUR'}).format(amount/100);
  function valid(payload:unknown,expectedOwner:Owner):payload is Handoff{
    if(!payload||typeof payload!=='object'||Array.isArray(payload))return false;
    const item=payload as Record<string,unknown>;
    return item.organization_id===expectedOwner&&
      item.posting_status==='awaiting_verified_finance_consumer'&&
      item.finance_posting_active===false&&
      item.journal_entries_confirmed===0&&
      ['total_events','sale_receipt_events','verified_refund_events',
        'sale_gross_cents','refund_gross_cents'].every(k=>
          Number.isSafeInteger(item[k])&&(item[k] as number)>=0)&&
      Number.isSafeInteger(item.net_event_cents)&&
      (item.total_events as number)===(item.sale_receipt_events as number)+
        (item.verified_refund_events as number)&&
      (item.net_event_cents as number)===(item.sale_gross_cents as number)-
        (item.refund_gross_cents as number)&&
      (item.oldest_event_at===null||typeof item.oldest_event_at==='string'&&
        !Number.isNaN(Date.parse(item.oldest_event_at)));
  }
  $effect(()=>{void owner;void actorId;report=null;error='';});
  async function load(){
    if(!browser||loading||!actorId)return;
    const expectedOwner=owner,expectedActor=actorId;
    loading=true;report=null;error='';
    try{
      const result=await fetch('/api/admin/pos/cashier/finance-handoff?'+
        new URLSearchParams({organization_id:expectedOwner}),{
          credentials:'same-origin',cache:'no-store',
          headers:{Accept:'application/json'}
        });
      if(owner!==expectedOwner||actorId!==expectedActor)return;
      if(result.status===401){onExpired();return;}
      if(result.status===403){onRevoked();return;}
      if(!result.ok)throw Error('Η κατάσταση συμφωνίας δεν είναι διαθέσιμη.');
      const data:unknown=await result.json();
      if(!valid(data,expectedOwner))throw Error('Μη έγκυρη αναφορά συμφωνίας.');
      report=data;
    }catch(e){
      if(owner===expectedOwner&&actorId===expectedActor)
        error=e instanceof Error?e.message:'Σφάλμα συμφωνίας.';
    }finally{loading=false;}
  }
</script>
<section class="admin-panel" data-testid="pos-finance-handoff">
  <p class="admin-eyebrow">POS · ΟΙΚΟΝΟΜΙΚΗ ΣΥΜΦΩΝΙΑ</p>
  <h2>Παραστατικά προς λογιστική συμφωνία</h2>
  <p class="admin-muted">Οι εγγραφές έχουν καταχωριστεί στο POS,
    αλλά δεν έχουν επιβεβαιωθεί ως εγγραφές του γενικού λογιστικού.</p>
  <button type="button" class="admin-secondary" disabled={loading}
    onclick={()=>void load()}>{loading?'Ανάκτηση…':'Έλεγχος εκκρεμών εγγραφών'}</button>
  {#if error}<p role="alert">{error}</p>{/if}
  {#if report}
    <div aria-live="polite">
      <p>Εκκρεμή γεγονότα: <strong>{report.total_events}</strong>
        (πωλήσεις {report.sale_receipt_events}, επαληθευμένες επιστροφές
        {report.verified_refund_events})</p>
      <p>Σύνολο αποδείξεων: {euros(report.sale_gross_cents)}</p>
      <p>Σύνολο επαληθευμένων επιστροφών: {euros(report.refund_gross_cents)}</p>
      <p>Καθαρό σύνολο εκκρεμών γεγονότων: <strong>{euros(report.net_event_cents)}</strong></p>
      <p class="admin-muted">Λογιστική καταχώριση: Εκκρεμεί. Καμία χρέωση ή
        γονεϊκό υπόλοιπο δεν έχει ενημερωθεί από αυτή την αναφορά.</p>
    </div>
  {/if}
</section>