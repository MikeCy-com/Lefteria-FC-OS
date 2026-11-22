<script lang="ts">
  import {browser} from '$app/env';

  type Owner='lefteria_fc'|'lefteria_fc_academy';
  type Status='matched'|'legacy_missing'|'cashier_missing'|'not_assigned'|'quantity_mismatch';
  type StockLine={
    product_id:string;variant_id:string;product_name:string;variant_name:string;
    legacy_on_hand:number|null;cashier_on_hand:number|null;
    cashier_reserved:number|null;cashier_available:number|null;
    quantity_difference:number|null;location_product_enabled:boolean;
    status:Status;
  };
  type Snapshot={
    organization_id:Owner;pos_location_id:string;location_name:string;limit:150;
    total_skus:number;matched_skus:number;discrepant_skus:number;
    legacy_missing_skus:number;cashier_missing_skus:number;
    disabled_assignments:number;reserved_units:number;open_shifts:number;
    ledger_balances_match:boolean;physical_count_approved:false;
    stock_ready_for_trading:false;lines:StockLine[];
  };
  let {owner,actorId,locationId,onExpired,onRevoked}:{
    owner:Owner;actorId:string;locationId:string;
    onExpired:()=>void;onRevoked:()=>void;
  }=$props();

  let snapshot=$state<Snapshot|null>(null);
  let busy=$state(false);
  let error=$state('');
  let lastScope='';
  const scope=$derived(owner+':'+actorId+':'+locationId);
  const id=(value:unknown)=>typeof value==='string'&&
    /^[A-Za-z0-9_-]{1,80}$/.test(value);
  const quantity=(n:unknown)=>Number.isSafeInteger(n)&&
    (n as number)>=0&&(n as number)<=100000000;
  const bounded=(n:unknown)=>n===null||quantity(n);
  const label=(status:Status)=>({
    matched:'Συμφωνούν',legacy_missing:'Δεν υπάρχει στο κύριο POS',
    cashier_missing:'Δεν υπάρχει στο ταμείο',
    not_assigned:'Δεν έχει ανατεθεί στην τοποθεσία',
    quantity_mismatch:'Διαφορετικό απόθεμα'
  })[status];
  const validated=(raw:unknown):raw is Snapshot=>{
    if(!raw||typeof raw!=='object'||Array.isArray(raw))return false;
    const r=raw as Record<string,unknown>;
    if(r.organization_id!==owner||r.pos_location_id!==locationId||
      typeof r.location_name!=='string'||r.location_name.length>140||
      r.limit!==150||r.physical_count_approved!==false||
      r.stock_ready_for_trading!==false||typeof r.ledger_balances_match!=='boolean'||
      !Array.isArray(r.lines)||r.lines.length>150||
      !['total_skus','matched_skus','discrepant_skus','legacy_missing_skus',
        'cashier_missing_skus','disabled_assignments','reserved_units',
        'open_shifts'].every(k=>quantity(r[k])))return false;
    const lines=r.lines as unknown[];
    if(r.total_skus!==lines.length||
      r.matched_skus!==lines.filter(x=>(x as StockLine).status==='matched').length||
      r.discrepant_skus!==lines.filter(x=>(x as StockLine).status!=='matched').length||
      r.ledger_balances_match!==(lines.length>0&&r.discrepant_skus===0))return false;
    return lines.every(line=>{
      if(!line||typeof line!=='object'||Array.isArray(line))return false;
      const x=line as Record<string,unknown>;
      if(!id(x.product_id)||!id(x.variant_id)||
        typeof x.product_name!=='string'||x.product_name.length>100||
        typeof x.variant_name!=='string'||x.variant_name.length>80||
        typeof x.location_product_enabled!=='boolean'||
        !bounded(x.legacy_on_hand)||!bounded(x.cashier_on_hand)||
        !bounded(x.cashier_reserved)||!bounded(x.cashier_available)||
        !(x.quantity_difference===null||
          Number.isSafeInteger(x.quantity_difference)&&
          Math.abs(x.quantity_difference as number)<=100000000)||
        !['matched','legacy_missing','cashier_missing','not_assigned',
          'quantity_mismatch'].includes(String(x.status)))return false;
      if(x.cashier_on_hand===null)return x.cashier_reserved===null&&
        x.cashier_available===null&&x.quantity_difference===null;
      return quantity(x.cashier_reserved)&&quantity(x.cashier_available)&&
        Number(x.cashier_on_hand)-Number(x.cashier_reserved)===x.cashier_available&&
        (x.legacy_on_hand===null?x.quantity_difference===null:
          x.quantity_difference===Number(x.legacy_on_hand)-Number(x.cashier_on_hand));
    });
  };
  $effect(()=>{
    const current=scope;
    if(current!==lastScope){snapshot=null;busy=false;error='';lastScope=current;}
  });
  async function inspect(){
    if(!browser||busy||!id(locationId)||!actorId)return;
    const expectedOwner=owner,expectedActor=actorId,expectedSite=locationId;
    busy=true;snapshot=null;error='';
    try{
      const response=await fetch('/api/admin/pos/sites/'+
        encodeURIComponent(expectedSite)+'/stock-reconciliation?'+
        new URLSearchParams({organization_id:expectedOwner}),{
          credentials:'same-origin',cache:'no-store',
          headers:{Accept:'application/json'}
        });
      if(owner!==expectedOwner||actorId!==expectedActor||locationId!==expectedSite)return;
      if(response.status===401){onExpired();return;}
      if(response.status===403){onRevoked();return;}
      if(!response.ok)throw Error(response.status===503
        ?'Ο έλεγχος αποθεμάτων δεν έχει εγκριθεί για αυτό το περιβάλλον.'
        :'Δεν μπορεί να διαβαστεί η κατάσταση αυτού του σημείου.');
      const payload:unknown=await response.json();
      if(!validated(payload))throw Error('Απόρριψη μη επαληθευμένων δεδομένων αποθήκης.');
      snapshot=payload;
    }catch(e){
      if(owner===expectedOwner&&actorId===expectedActor&&locationId===expectedSite)
        error=e instanceof Error?e.message:'Σφάλμα απογραφής.';
    }finally{busy=false;}
  }
</script>

<section class="admin-panel pos-stock-reconciliation" data-testid="pos-site-stock-reconciliation">
  <header>
    <div>
      <h3>Έλεγχος αποθέματος ταμείου</h3>
      <p class="admin-muted">Διαβάζει δύο ανεξάρτητα καθολικά αποθήκης μόνο
        για την επιλεγμένη τοποθεσία. Δεν εισάγει, διορθώνει ή
        εγκρίνει απογραφή.</p>
    </div>
    <button type="button" class="admin-secondary" disabled={busy||!id(locationId)}
      onclick={()=>void inspect()}>{busy?'Έλεγχος…':'Σύγκριση αποθεμάτων'}</button>
  </header>
  {#if error}<p role="alert">{error}</p>{/if}
  {#if snapshot}
    <p role="status"><strong>{snapshot.location_name}</strong> ·
      {snapshot.matched_skus}/{snapshot.total_skus} SKU συμφωνούν ·
      <strong>{snapshot.discrepant_skus} αποκλίσεις</strong></p>
    <p>Λείπουν από το κύριο POS: {snapshot.legacy_missing_skus} ·
      λείπουν από το ταμείο: {snapshot.cashier_missing_skus} ·
      προϊόντα χωρίς ενεργή ανάθεση: {snapshot.disabled_assignments} ·
      δεσμευμένα τεμάχια: {snapshot.reserved_units} ·
      ανοικτές βάρδιες: {snapshot.open_shifts}</p>
    <p role="alert">Απογραφή παραγωγής: <strong>ΜΗ ΕΓΚΕΚΡΙΜΕΝΗ</strong>.
      {snapshot.ledger_balances_match
        ?'Οι ηλεκτρονικές ποσότητες συμφωνούν, όχι όμως απαραίτητα με τη φυσική απογραφή.'
        :'Υπάρχουν αποκλίσεις μεταξύ των δύο αποθηκών.'}
      Απαιτούνται φυσική καταμέτρηση, συμφωνία παραλαβών και πωλήσεων,
      ανεξάρτητη έγκριση και ασφαλής ενοποίηση πριν ενεργοποιηθούν πραγματικές συναλλαγές.</p>
    {#if snapshot.total_skus===0}
      <p>Δεν έχει καταχωριστεί κανένα SKU για αυτό το σημείο. Δεν αποτελεί ένδειξη μηδενικής φυσικής αποθήκης.</p>
    {:else}
      <div class="pos-stock-table-wrap">
        <table>
          <thead><tr>
            <th scope="col">Προϊόν</th><th scope="col">Κύριο POS</th>
            <th scope="col">Ταμείο</th><th scope="col">Δεσμ.</th>
            <th scope="col">Διαφορά</th><th scope="col">Έλεγχος</th>
          </tr></thead>
          <tbody>
            {#each snapshot.lines as item (item.product_id+':'+item.variant_id)}
              <tr>
                <th scope="row">{item.product_name} · {item.variant_name}</th>
                <td>{item.legacy_on_hand??'—'}</td>
                <td>{item.cashier_on_hand??'—'}</td>
                <td>{item.cashier_reserved??'—'}</td>
                <td>{item.quantity_difference??'—'}</td>
                <td>{label(item.status)}</td>
              </tr>
            {/each}
          </tbody>
        </table>
      </div>
    {/if}
  {/if}
</section>

<style>
  .pos-stock-reconciliation{display:grid;gap:12px;margin:16px 0}
  .pos-stock-reconciliation header{display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap}
  .pos-stock-reconciliation h3{margin:0}
  .pos-stock-table-wrap{overflow-x:auto;max-width:100%}
  .pos-stock-reconciliation table{width:100%;border-collapse:collapse;font-size:.88rem}
  .pos-stock-reconciliation td,.pos-stock-reconciliation th{
    padding:9px 10px;text-align:left;border-bottom:1px solid #6e6355;
  }
</style>
