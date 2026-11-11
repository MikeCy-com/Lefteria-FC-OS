<script lang="ts">
  import {browser} from '$app/env';
  import {page} from '$app/state';
  import {getContext} from 'svelte';
  import {ADMIN_AUTH_CONTEXT,type AdminAuthController} from '../../../../../lib/admin/controller.ts';
  import {hasAdminPermission} from '../../../../../lib/admin/permissions.ts';
  import AdminFrame from '../../../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../../../lib/admin/AdminSessionStatus.svelte';

  type Org='lefteria_fc'|'lefteria_fc_academy';
  type Line={line_number:number;product_name:string;variant_name:string;
    quantity:number;unit_price_cents:number;line_total_cents:number};
  type ReceiptPayload={
    schema_version:number;receipt_kind:string;currency:string;
    organization_id:Org;receipt_number:number;total_cents:number;
    subtotal_cents:number;tax_cents:number;discount_cents:number;
    fiscal_policy_version:string;lines:Line[];
  };
  type Receipt={
    organization_id:Org;receipt_id:string;register_id:string;
    receipt_number:number;issued_at:string;payload_sha256:string;
    receipt:ReceiptPayload;
  };
  const auth=getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  const isUnavailable=$derived($auth.status === 'unavailable');
  const assigned=$derived($auth.session?.user.organizations.filter(o=>
    o.id==='lefteria_fc'||o.id==='lefteria_fc_academy').map(o=>o.id as Org)??[]);
  const allowed=$derived($auth.status==='authenticated'&&!!$auth.session&&
    ['manager','super_admin'].includes($auth.session.user.role)&&
    hasAdminPermission($auth.session.user.permissions,'shop:write'));
  let owner=$state<Org>('lefteria_fc');
  let receipt=$state<Receipt|null>(null);
  let loading=$state(false);
  let error=$state('');
  $effect(()=>{if(assigned.length&&!assigned.includes(owner))owner=assigned[0];});
  const euros=(cents:number)=>new Intl.NumberFormat('el-CY',{
    style:'currency',currency:'EUR'}).format(cents/100);
  function safeReceipt(raw:unknown,org:Org,id:string):raw is Receipt{
    if(!raw||typeof raw!=='object'||Array.isArray(raw))return false;
    const r=raw as Record<string,unknown>;
    if(r.organization_id!==org||r.receipt_id!==id||
      typeof r.register_id!=='string'||r.register_id.length>80||
      !Number.isSafeInteger(r.receipt_number)||
      typeof r.issued_at!=='string'||Number.isNaN(Date.parse(r.issued_at))||
      typeof r.payload_sha256!=='string'||!/^[a-f0-9]{64}$/.test(r.payload_sha256)||
      !r.receipt||typeof r.receipt!=='object'||Array.isArray(r.receipt))return false;
    const p=r.receipt as Record<string,unknown>;
    return p.schema_version===1&&p.receipt_kind==='pos_sale'&&
      p.organization_id===org&&p.currency==='EUR'&&
      p.receipt_number===r.receipt_number&&
      typeof p.fiscal_policy_version==='string'&&p.fiscal_policy_version.length<=80&&
      ['total_cents','subtotal_cents','tax_cents','discount_cents'].every(k=>
        Number.isSafeInteger(p[k])&&(p[k] as number)>=0&&
        (p[k] as number)<=100000000)&&
      Array.isArray(p.lines)&&p.lines.length>=1&&p.lines.length<=30&&
      p.lines.reduce((sum:number,v:unknown)=>
        sum+(typeof v==='object'&&v!==null&&
          Number.isSafeInteger((v as Record<string,unknown>).line_total_cents)
            ?(v as Line).line_total_cents:0),0)===p.subtotal_cents&&
      (p.subtotal_cents as number)-(p.discount_cents as number)+
        (p.tax_cents as number)===p.total_cents&&
      p.lines.every((v:unknown)=>{
        if(!v||typeof v!=='object'||Array.isArray(v))return false;
        const l=v as Record<string,unknown>;
        return Number.isSafeInteger(l.line_number)&&
          typeof l.product_name==='string'&&l.product_name.length<=100&&
          typeof l.variant_name==='string'&&l.variant_name.length<=80&&
          Number.isSafeInteger(l.quantity)&&(l.quantity as number)>=1&&
          (l.quantity as number)<=99&&
          Number.isSafeInteger(l.unit_price_cents)&&
          Number.isSafeInteger(l.line_total_cents)&&
          l.line_total_cents===(l.quantity as number)*(l.unit_price_cents as number);
      });
  }
  $effect(()=>{
    const org=owner,user=$auth.session?.user.id,permitted=allowed&&assigned.includes(org);
    const id=page.params.receiptId;
    receipt=null;error='';loading=false;
    if(!browser||!permitted||!user||typeof id!=='string'||
      !/^[0-9a-f-]{36}$/.test(id))return;
    let active=true;const controller=new AbortController();
    loading=true;
    void(async()=>{
      try{
        const response=await fetch(
          '/api/admin/pos/cashier/receipts/'+encodeURIComponent(id)+'?'+
          new URLSearchParams({organization_id:org}),{
          credentials:'same-origin',cache:'no-store',
          headers:{Accept:'application/json'},signal:controller.signal});
        if(!active)return;
        if(response.status===401||response.status===403){
          void auth.bootstrap();
          throw Error('Η συνεδρία ή η πρόσβαση στα έγγραφα άλλαξε.');
        }
        if(!response.ok)throw Error('Δεν είναι διαθέσιμη αυτή η απόδειξη.');
        const value:unknown=await response.json();
        if(!safeReceipt(value,org,id))throw Error('Μη επαληθευμένη απόδειξη.');
        if(active)receipt=value;
      }catch(e){if(active)error=e instanceof Error?e.message:'Σφάλμα ανάγνωσης.';}
      finally{if(active)loading=false;}
    })();
    return()=>{active=false;controller.abort();};
  });
</script>

<svelte:head><title>Εκδοθείσα απόδειξη | Lefteria FC OS</title></svelte:head>
{#if $auth.status==='authenticated'&&$auth.session}
<AdminFrame user={$auth.session.user} active="pos" busy={$auth.busy}
  onLogout={()=>void auth.logout()}>
  <section class="pos-receipt-page mx-auto max-w-2xl space-y-4 p-5">
    <div class="no-print">
      <h1 class="text-2xl font-bold">Προβολή εκδοθείσας απόδειξης</h1>
      <p>Η εκτύπωση εμφανίζει μόνο την αποθηκευμένη απόδειξη. Δεν δημιουργεί νέα συναλλαγή.</p>
      <AdminSessionStatus unavailable={isUnavailable} busy={$auth.busy} error={$auth.error}
    onRetry={() => void auth.bootstrap()} onLogout={() => void auth.logout()}/>
      {#if allowed}
      <label>Οργανισμός
        <select bind:value={owner}>
          {#each assigned as org}
            <option value={org}>{org==='lefteria_fc'?'Πρώτη ομάδα':'Ακαδημία'}</option>
          {/each}
        </select>
      </label>
      {/if}
      {#if loading}<p role="status">Φόρτωση απόδειξης…</p>{/if}
      {#if error}<p role="alert">{error}</p>{/if}
    </div>
    {#if receipt}
      <article class="pos-print-receipt" aria-label="Εκδοθείσα απόδειξη">
        <h2>Λευτεριά FC</h2>
        <p>Απόδειξη #{receipt.receipt_number}</p>
        <p>{new Date(receipt.issued_at).toLocaleString('el-CY')}</p>
        <p>Ταμείο: {receipt.register_id}</p>
        <table>
          <thead><tr><th>Προϊόν</th><th>Ποσ.</th><th>Τιμή</th><th>Σύνολο</th></tr></thead>
          <tbody>
            {#each receipt.receipt.lines as line (line.line_number)}
              <tr>
                <td>{line.product_name} · {line.variant_name}</td>
                <td>{line.quantity}</td>
                <td>{euros(line.unit_price_cents)}</td>
                <td>{euros(line.line_total_cents)}</td>
              </tr>
            {/each}
          </tbody>
        </table>
        <p>Υποσύνολο: {euros(receipt.receipt.subtotal_cents)}</p>
        <p>ΦΠΑ: {euros(receipt.receipt.tax_cents)}</p>
        <p>Έκπτωση: {euros(receipt.receipt.discount_cents)}</p>
        <p class="total">Πληρωτέο: {euros(receipt.receipt.total_cents)}</p>
        <p class="small">Έκδοση φορολογικού κανόνα: {receipt.receipt.fiscal_policy_version}</p>
        <p class="small">Ακεραιότητα: {receipt.payload_sha256}</p>
      </article>
      <button type="button" class="no-print admin-secondary" onclick={()=>window.print()}>
        Εκτύπωση
      </button>
    {/if}
  </section>
</AdminFrame>
{:else}
<AdminSessionStatus unavailable={isUnavailable} busy={$auth.busy} error={$auth.error}
    onRetry={() => void auth.bootstrap()} onLogout={() => void auth.logout()}/>
{/if}

<style>
.pos-print-receipt{padding:24px;border:1px solid #777;background:white;color:black;font:14px system-ui}
.pos-print-receipt table{width:100%;border-collapse:collapse}
.pos-print-receipt td,.pos-print-receipt th{padding:8px 4px;text-align:left;border-bottom:1px solid #ddd}
.pos-print-receipt .total{font-weight:700;font-size:18px}
.pos-print-receipt .small{font-size:10px;overflow-wrap:anywhere}
@media print{
  :global(body){background:white!important;color:black!important}
  :global(header),:global(nav),:global(aside),:global(footer){display:none!important}
  .no-print{display:none!important}
  .pos-receipt-page{padding:0!important;max-width:none!important}
  .pos-print-receipt{border:0;padding:0}
}
</style>