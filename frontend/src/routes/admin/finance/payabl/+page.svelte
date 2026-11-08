<script lang="ts">
  import { getContext } from 'svelte';
  import { browser } from '$app/env';
  import { goto } from '$app/navigation';
  import { ADMIN_AUTH_CONTEXT,type AdminAuthController } from '../../../../lib/admin/controller.ts';
  import { hasAdminPermission } from '../../../../lib/admin/permissions.ts';
  import AdminFrame from '../../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../../lib/admin/AdminSessionStatus.svelte';

  type Org='lefteria_fc'|'lefteria_fc_academy';
  type Payment={
    id:string;public_token:string;purpose:string;title:string;amount_cents:number;
    currency:string;status:string;version:number;created_at?:string;
    paid_at?:string|null;gateway_transaction_id?:string|null
  };
  const auth=getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  const permitted=$derived($auth.status==='authenticated'&&$auth.session&&
    hasAdminPermission($auth.session.user.permissions,'finance:read')
      ?$auth.session.user.organizations.filter((o):o is typeof o & {id:Org}=>
        o.id==='lefteria_fc'||o.id==='lefteria_fc_academy') : []);
  const canWrite=$derived($auth.status==='authenticated'&&$auth.session&&
    hasAdminPermission($auth.session.user.permissions,'finance:write'));
  let owner=$state<Org|null>(null);
  let payments=$state<Payment[]>([]);
  let loading=$state(false),busy=$state(false),reload=$state(0);
  let error=$state<string|null>(null),success=$state<string|null>(null);
  let purpose=$state('academy_fee'),title=$state(''),amount=$state(''),
    firstName=$state(''),lastName=$state(''),email=$state('');
  const choices=[
    {id:'academy_fee',label:'Δίδακτρα Ακαδημίας'},
    {id:'subscription_invoice',label:'Δόση συνδρομής (μεμονωμένη)'},
    {id:'shop_order',label:'Παραγγελία καταστήματος'},
    {id:'event_ticket',label:'Εισιτήριο εκδήλωσης'},
    {id:'donation',label:'Δωρεά'},
    {id:'other',label:'Άλλη χρέωση'}
  ];
  const validRow=(x:unknown):x is Payment=>{
    if(!x||typeof x!=='object'||Array.isArray(x))return false;
    const p=x as Record<string,unknown>;
    return typeof p.id==='string'&&/^pay_[0-9a-f]{32}$/.test(p.id)&&
      typeof p.public_token==='string'&&/^[0-9a-f]{32}$/.test(p.public_token)&&
      typeof p.title==='string'&&p.title.length>1&&p.title.length<=140&&
      typeof p.purpose==='string'&&choices.some(c=>c.id===p.purpose)&&
      Number.isInteger(p.amount_cents)&&Number(p.amount_cents)>0&&
      Number(p.amount_cents)<=10000000&&p.currency==='EUR'&&
      typeof p.status==='string'&&
      ['draft','initiating','checkout_ready','paid','declined','cancelled'].includes(p.status)&&
      Number.isSafeInteger(p.version)&&Number(p.version)>=1;
  };
  const parseRows=(x:unknown,o:Org):Payment[]=>{
    if(!x||typeof x!=='object'||Array.isArray(x))throw Error('Invalid payment response.');
    const p=x as Record<string,unknown>;
    if(p.organization_id!==o||!Array.isArray(p.payments)||
       p.payments.length>100||!p.payments.every(validRow))throw Error('Unverified payment list.');
    return p.payments;
  };
  $effect(()=>{if(browser&&$auth.status==='anonymous')void goto('/admin/login',{replace:true});});
  $effect(()=>{
    if(!permitted.some(o=>o.id===owner))owner=permitted[0]?.id??null;
  });
  $effect(()=>{
    const selected=owner,actor=$auth.session?.user.id,r=reload;
    void r;
    payments=[];error=null;success=null;
    if(!browser||!selected||!permitted.some(o=>o.id===selected))return;
    const controller=new AbortController();loading=true;
    void(async()=>{
      try{
        const response=await fetch('/api/admin/finance/payabl/payment-links?'+
          new URLSearchParams({organization_id:selected}),{
            credentials:'same-origin',cache:'no-store',signal:controller.signal});
        if(controller.signal.aborted)return;
        if(response.status===401){auth.invalidateSession();return;}
        if(response.status===403){void auth.bootstrap();return;}
        if(!response.ok)throw Error('Δεν φορτώθηκαν οι οικονομικές εγγραφές.');
        const raw:unknown=await response.json();
        if(!controller.signal.aborted&&owner===selected&&actor===$auth.session?.user.id)
          payments=parseRows(raw,selected);
      }catch{
        if(!controller.signal.aborted)error='Δεν ήταν δυνατή η φόρτωση των πληρωμών.';
      }finally{if(!controller.signal.aborted)loading=false;}
    })();
    return()=>controller.abort();
  });
  const cents=(raw:string):number|null=>{
    if(!/^[0-9]{1,6}([.,][0-9]{1,2})?$/.test(raw))return null;
    const [whole,decimal='']=raw.replace(',','.').split('.');
    const parsed=Number(whole)*100+Number(decimal.padEnd(2,'0'));
    return Number.isSafeInteger(parsed)&&parsed>0&&parsed<=10000000?parsed:null;
  };
  async function create(){
    const selected=owner,actor=$auth.session?.user.id,total=cents(amount);
    if(!browser||!selected||!canWrite||busy||loading||total===null||
      !choices.some(c=>c.id===purpose)||title.length<2||title.length>140||
      title!==title.trim()||firstName.length<1||firstName.length>50||
      lastName.length<1||lastName.length>50||email.length>50||!email.includes('@')){
      error='Συμπληρώστε έγκυρα στοιχεία χρέωσης, ποσό και στοιχεία πληρωτή.';
      return;
    }
    busy=true;error=null;success=null;
    try{
      const response=await fetch('/api/admin/finance/payabl/payment-links?'+
        new URLSearchParams({organization_id:selected}),{
          method:'POST',credentials:'same-origin',cache:'no-store',
          headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
          body:JSON.stringify({purpose,title,amount_cents:total,
            first_name:firstName,last_name:lastName,email})
        });
      if(selected!==owner||actor!==$auth.session?.user.id)return;
      if(response.status===401){auth.invalidateSession();return;}
      if(response.status===403){void auth.bootstrap();return;}
      if(!response.ok)throw Error('Η χρέωση δεν καταχωρήθηκε.');
      const raw:unknown=await response.json();
      if(!validRow(raw)||(raw as Payment).status!=='draft'||
        (raw as Payment).amount_cents!==total)
        throw Error('Η καταχώρηση δεν επαληθεύτηκε.');
      payments=[raw,...payments].slice(0,100);
      title='';amount='';firstName='';lastName='';email='';
      success='Το αίτημα καταχωρήθηκε ως πρόχειρο. Δεν έχει εισπραχθεί ποσό.';
    }catch(e){
      if(selected===owner&&actor===$auth.session?.user.id)
        error=e instanceof Error?e.message:'Η καταχώρηση απέτυχε.';
    }finally{busy=false;}
  }
  async function activate(row:Payment){
    const selected=owner,actor=$auth.session?.user.id;
    if(!browser||!selected||!canWrite||busy||loading||row.status!=='draft'||
      !window.confirm('Δημιουργία εξωτερικού checkout για τη χρέωση '+row.title+';'))return;
    busy=true;error=null;success=null;
    try{
      const response=await fetch('/api/admin/finance/payabl/payment-links/'+
        encodeURIComponent(row.id)+'/checkout?'+
        new URLSearchParams({organization_id:selected}),{
          method:'POST',credentials:'same-origin',cache:'no-store',
          headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
          body:'{}'
        });
      if(selected!==owner||actor!==$auth.session?.user.id)return;
      if(response.status===401){auth.invalidateSession();return;}
      if(response.status===403){void auth.bootstrap();return;}
      if(!response.ok)throw Error('Το payabl. checkout δεν είναι διαθέσιμο ή δεν ενεργοποιήθηκε ακόμη.');
      const result:unknown=await response.json();
      if(!result||typeof result!=='object'||Array.isArray(result))throw Error('Invalid response.');
      const data=result as Record<string,unknown>;
      if(data.id!==row.id||data.public_token!==row.public_token||
        data.status!=='checkout_ready'||!Number.isSafeInteger(data.version)||
        Number(data.version)<=row.version)throw Error('Το checkout δεν επαληθεύτηκε.');
      payments=payments.map(p=>p.id===row.id
        ?{...p,status:'checkout_ready',version:Number(data.version)}:p);
      success='Το hosted checkout δημιουργήθηκε. Μπορείτε να κοινοποιήσετε τον σύνδεσμο.';
    }catch(e){
      if(selected===owner&&actor===$auth.session?.user.id)
        error=e instanceof Error?e.message:'Η ενεργοποίηση απέτυχε.';
    }finally{busy=false;}
  }
  function paymentLink(token:string):string {
    if(!browser||!/^[0-9a-f]{32}$/.test(token))return '';
    return window.location.origin+'/pay/'+token;
  }
  async function copyLink(row:Payment){
    if(!browser||row.status!=='checkout_ready'||!paymentLink(row.public_token))return;
    try{
      await navigator.clipboard.writeText(paymentLink(row.public_token));
      success='Ο σύνδεσμος αντιγράφηκε.';error=null;
    }catch{error='Αντιγράψτε τον σύνδεσμο από την περιοχή του αιτήματος.';}
  }
  const formatMoney=(n:number)=>new Intl.NumberFormat('el-CY',
    {style:'currency',currency:'EUR'}).format(n/100);
</script>

<svelte:head>
  <title>payabl. · Πληρωμές | Λευτεριά FC OS</title>
  <meta name="robots" content="noindex"/>
</svelte:head>
{#if $auth.status==='authenticated'&&$auth.session}
  <AdminFrame user={$auth.session.user} active="finance" busy={$auth.busy}
    onLogout={()=>void auth.logout()}>
    <p class="admin-eyebrow">ΟΙΚΟΝΟΜΙΚΑ · PAYABL.</p>
    <h1>Σύνδεσμοι πληρωμών</h1>
    <p class="admin-muted">Ασφαλές εξωτερικό checkout payabl. για δίδακτρα,
      συνδρομές ανά δόση, κατάστημα και άλλες χρεώσεις. Η επιστροφή του πελάτη
      στη σελίδα δεν αποτελεί επιβεβαίωση είσπραξης: η κατάσταση ενημερώνεται
      μόνο από επαληθευμένη ειδοποίηση του παρόχου.</p>
    {#if !permitted.length}
      <section class="admin-panel" role="alert">Δεν διαθέτετε οικονομική πρόσβαση.</section>
    {:else}
      <section class="admin-panel">
        <label for="payabl-owner">Οργανισμός</label>
        <select id="payabl-owner" value={owner??''}
          onchange={e=>{if(permitted.some(o=>o.id===e.currentTarget.value))
            owner=e.currentTarget.value as Org;}}>
          {#each permitted as org (org.id)}
            <option value={org.id}>{org.name}</option>
          {/each}
        </select>
      </section>
      {#if canWrite}
        <section class="admin-panel">
          <h2>Νέο αίτημα πληρωμής</h2>
          <form class="payment-form" onsubmit={e=>{e.preventDefault();void create();}}
            data-testid="payabl-payment-form">
            <label>Σκοπός
              <select bind:value={purpose} disabled={busy||loading}>
                {#each choices as option (option.id)}
                  <option value={option.id}>{option.label}</option>
                {/each}
              </select>
            </label>
            <label>Περιγραφή
              <input bind:value={title} required maxlength="140"
                placeholder="Δίδακτρα τμήματος" disabled={busy||loading}/>
            </label>
            <label>Ποσό (€)
              <input bind:value={amount} inputmode="decimal" required
                placeholder="45.00" disabled={busy||loading}/>
            </label>
            <label>Όνομα πληρωτή
              <input bind:value={firstName} required maxlength="50" disabled={busy||loading}/>
            </label>
            <label>Επώνυμο πληρωτή
              <input bind:value={lastName} required maxlength="50" disabled={busy||loading}/>
            </label>
            <label>Email πληρωτή
              <input type="email" bind:value={email} required maxlength="50"
                disabled={busy||loading}/>
            </label>
            <button class="admin-primary" type="submit" disabled={busy||loading}>
              Αποθήκευση αιτήματος</button>
          </form>
        </section>
      {/if}
      <section class="admin-panel" data-testid="payabl-links">
        <div class="section-head">
          <h2>Εγγραφές πληρωμών</h2>
          <button type="button" class="admin-secondary" disabled={busy||loading}
            onclick={()=>reload++}>Ανανέωση</button>
        </div>
        {#if loading}<p role="status">Φόρτωση…</p>
        {:else if !payments.length}<p>Δεν υπάρχουν αιτήματα για τον οργανισμό.</p>
        {:else}
          <ul class="payment-list">
            {#each payments as p (p.id)}
              <li>
                <div class="section-head">
                  <strong>{p.title}</strong>
                  <strong>{formatMoney(p.amount_cents)}</strong>
                </div>
                <p>{choices.find(x=>x.id===p.purpose)?.label??p.purpose}
                  · <b>{p.status==='paid'?'Πληρώθηκε':
                     p.status==='checkout_ready'?'Αναμονή πληρωμής':
                     p.status==='draft'?'Πρόχειρο':
                     p.status==='declined'?'Απορρίφθηκε':p.status}</b></p>
                {#if p.status==='draft'&&canWrite}
                  <button type="button" class="admin-primary"
                    disabled={busy||loading} onclick={()=>void activate(p)}>
                    Δημιουργία payabl. checkout
                  </button>
                {:else if p.status==='checkout_ready'}
                  <p class="payment-url">{paymentLink(p.public_token)}</p>
                  <button type="button" class="admin-secondary"
                    disabled={busy||loading} onclick={()=>void copyLink(p)}>
                    Αντιγραφή συνδέσμου
                  </button>
                  <p class="admin-muted">Η συνεδρία checkout έχει περιορισμένο χρόνο
                    ισχύος. Σε ληγμένο σύνδεσμο ζητήστε νέο αίτημα.</p>
                {/if}
              </li>
            {/each}
          </ul>
        {/if}
      </section>
    {/if}
    {#if error}<p class="form-error" role="alert">{error}</p>{/if}
    {#if success}<p class="form-success" role="status">{success}</p>{/if}
  </AdminFrame>
{:else}
  <AdminSessionStatus status={$auth.status} onRetry={()=>void auth.bootstrap()}/>
{/if}
<style>
  .payment-form{display:grid;grid-template-columns:repeat(auto-fit,minmax(230px,1fr));
    gap:14px;align-items:end;margin-top:18px}
  .payment-form label{display:grid;gap:7px;color:var(--admin-text,#fff);font-size:13px}
  .payment-form input,.payment-form select,select{min-height:43px;width:100%;
    border:1px solid #5e665d;background:#101715;color:#fff;border-radius:8px;
    padding:10px;font:inherit}
  .payment-list{list-style:none;margin:0;padding:0;display:grid;gap:12px}
  .payment-list li{padding:15px;border:1px solid #454a40;border-radius:11px;
    background:#111815}
  .payment-list p{font-size:13px;color:#d3d3ce;line-height:1.6}
  .payment-url{word-break:break-all;color:#d6ba75!important;font-size:12px!important}
  .section-head{display:flex;justify-content:space-between;align-items:center;
    gap:12px;flex-wrap:wrap}
  .section-head strong{font-size:17px}
  .form-error{color:#fbc7bb}.form-success{color:#d1edcf}
</style>
