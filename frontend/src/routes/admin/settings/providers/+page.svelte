<script lang="ts">
  import {getContext} from 'svelte';
  import {browser} from '$app/env';
  import {goto} from '$app/navigation';
  import AdminFrame from '../../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../../lib/admin/AdminSessionStatus.svelte';
  import {ADMIN_AUTH_CONTEXT,type AdminAuthController} from '../../../../lib/admin/controller.ts';

  type TwilioStatus={
    provider:'twilio';credential_source:'operator_configuration';
    account_sid_configured:boolean;auth_token_configured:boolean;
    from_number_configured:boolean;role_senders_configured:number;
    ready:boolean;editing_available:false;test_send_available:false
  };
  type PayablStatus={
    configured:boolean;environment:'sandbox';payment_links:boolean;
    subscriptions:boolean;employee_card_payouts:boolean;refunds:boolean;
    live_transactions:boolean
  };
  const auth=getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  let twilio=$state<TwilioStatus|null>(null);
  let payabl=$state<PayablStatus|null>(null);
  let loading=$state(false),revision=$state(0);
  let error=$state(''),payablError=$state('');
  const isSuperAdmin=$derived($auth.session?.user.role==='super_admin');
  const paymentOwner=$derived($auth.session?.user.organizations.find(
    org=>org.id==='lefteria_fc_academy'||org.id==='lefteria_fc')?.id??null);

  function verifiedTwilio(raw:unknown):raw is TwilioStatus{
    if(!raw||typeof raw!=='object'||Array.isArray(raw))return false;
    const x=raw as Record<string,unknown>;
    return x.provider==='twilio'&&x.credential_source==='operator_configuration'&&
      ['account_sid_configured','auth_token_configured','from_number_configured',
        'ready','editing_available','test_send_available'].every(k=>typeof x[k]==='boolean')&&
      x.editing_available===false&&x.test_send_available===false&&
      Number.isSafeInteger(x.role_senders_configured)&&
      (x.role_senders_configured as number)>=0&&
      (x.role_senders_configured as number)<=100;
  }
  function verifiedPayabl(raw:unknown):raw is PayablStatus{
    if(!raw||typeof raw!=='object'||Array.isArray(raw))return false;
    const x=raw as Record<string,unknown>;
    return x.environment==='sandbox'&&
      ['configured','payment_links','subscriptions','employee_card_payouts',
        'refunds','live_transactions'].every(k=>typeof x[k]==='boolean')&&
      x.live_transactions===false;
  }
  $effect(()=>{
    if(browser&&$auth.status==='anonymous')void goto('/admin/login',{replace:true});
  });
  $effect(()=>{
    const actor=$auth.session?.user.id,owner=paymentOwner,granted=isSuperAdmin;
    void revision;
    twilio=null;payabl=null;error='';payablError='';loading=false;
    if(!browser||!granted||!actor)return;
    const controller=new AbortController();
    loading=true;
    void (async()=>{
      try{
        const response=await fetch('/api/admin/settings/providers/twilio',{
          credentials:'same-origin',cache:'no-store',signal:controller.signal,
          headers:{Accept:'application/json'}
        });
        if(controller.signal.aborted)return;
        if(response.status===401){auth.invalidateSession();return;}
        if(response.status===403){void auth.bootstrap();return;}
        if(!response.ok)throw Error('Δεν ήταν δυνατή η επιβεβαίωση των ρυθμίσεων Twilio.');
        const raw:unknown=await response.json();
        if(!verifiedTwilio(raw))throw Error('Η απάντηση Twilio δεν επαληθεύτηκε.');
        if(!controller.signal.aborted&&$auth.session?.user.id===actor)twilio=raw;
      }catch(e){
        if(!controller.signal.aborted)error=e instanceof Error?e.message:'Η κατάσταση Twilio δεν είναι διαθέσιμη.';
      }finally{
        if(!controller.signal.aborted)loading=false;
      }
    })();
    if(owner)void (async()=>{
      try{
        const response=await fetch('/api/admin/finance/payabl/status?organization_id='+
          encodeURIComponent(owner),{
            credentials:'same-origin',cache:'no-store',signal:controller.signal,
            headers:{Accept:'application/json'}
          });
        if(controller.signal.aborted)return;
        if(response.status===401){auth.invalidateSession();return;}
        if(response.status===403)return;
        if(!response.ok)throw Error('Η κατάσταση payabl. δεν είναι διαθέσιμη.');
        const raw:unknown=await response.json();
        if(!verifiedPayabl(raw))throw Error('Η απάντηση payabl. δεν επαληθεύτηκε.');
        if(!controller.signal.aborted&&$auth.session?.user.id===actor&&paymentOwner===owner)
          payabl=raw;
      }catch(e){
        if(!controller.signal.aborted)payablError=e instanceof Error?e.message:'Η κατάσταση payabl. δεν είναι διαθέσιμη.';
      }
    })();
    return()=>controller.abort();
  });
</script>

<svelte:head><title>SMS / OTP & payabl. · Ρυθμίσεις | Λευτεριά FC OS</title></svelte:head>

{#if $auth.status==='authenticated' && $auth.session}
  <AdminFrame user={$auth.session.user} active="settings" busy={$auth.busy}
    onLogout={()=>void auth.logout()}>
    <div class="provider-settings" data-testid="admin-provider-settings">
      <header class="provider-title">
        <div>
          <h1>SMS / OTP & Πληρωμές</h1>
          <p class="admin-muted">Κεντρικές διασυνδέσεις, επαληθευμένη κατάσταση και ασφαλής λειτουργία.</p>
        </div>
        {#if isSuperAdmin}
          <button type="button" class="admin-secondary" disabled={loading}
            data-testid="admin-provider-refresh" onclick={()=>revision++}>Ανανέωση κατάστασης</button>
        {/if}
      </header>
      {#if !isSuperAdmin}
        <section class="admin-panel" role="alert">Οι ρυθμίσεις παρόχων είναι διαθέσιμες μόνο στον Super Admin.</section>
      {:else}
        <section class="provider-card" aria-labelledby="twilio-heading" data-testid="admin-twilio-settings">
          <div class="provider-row-heading">
            <div><h2 id="twilio-heading">SMS / OTP (TWILIO)</h2>
              <p>Διαμόρφωση αποστολής κωδικών OTP στην ιδιωτική εφαρμογή.</p></div>
            <span class:ready={twilio?.ready} class="provider-state">
              {twilio?.ready?'Ρυθμισμένο':loading?'Έλεγχος…':'Χρειάζεται έλεγχος'}
            </span>
          </div>
          {#if twilio}
            <div class="provider-health" role="status">
              <strong>{twilio.ready?'Οι απαιτούμενες παράμετροι έχουν οριστεί':'Απαιτείται ολοκλήρωση διαμόρφωσης'}</strong>
              <p>Ο έλεγχος αφορά τις παραμέτρους του διακομιστή, όχι επιτυχημένη παράδοση SMS.</p>
              <div class="provider-checks">
                <span>{twilio.account_sid_configured?'✓':'○'} Account SID</span>
                <span>{twilio.auth_token_configured?'✓':'○'} Auth Token</span>
                <span>{twilio.from_number_configured?'✓':'○'} Default From</span>
                <span>Προαιρετικοί αποστολείς ρόλων: {twilio.role_senders_configured}</span>
              </div>
            </div>
          {:else if error}
            <p role="alert" class="provider-error">{error}</p>
          {/if}
          <div class="provider-section">
            <h3>ΔΙΑΠΙΣΤΕΥΤΗΡΙΑ TWILIO</h3>
            <div class="provider-fields">
              <div><span>ACCOUNT SID</span>
                <strong>{twilio?.account_sid_configured?'Ρυθμισμένο · απόκρυψη τιμής':'Μη επιβεβαιωμένο'}</strong></div>
              <div><span>AUTH TOKEN</span>
                <strong>{twilio?.auth_token_configured?'•••••••••••• (κρυφό)':'Μη επιβεβαιωμένο'}</strong></div>
              <div><span>DEFAULT FROM NUMBER</span>
                <strong>{twilio?.from_number_configured?'Ρυθμισμένο · απόκρυψη αριθμού':'Μη επιβεβαιωμένο'}</strong></div>
            </div>
            <p class="provider-note">Οι τιμές διατηρούνται στη διαμόρφωση του διακομιστή.
              Η αλλαγή ή περιστροφή μυστικών δεν επιτρέπεται από αυτή τη σελίδα πριν ενεργοποιηθεί ελεγχόμενη, καταγεγραμμένη αποθήκευση.</p>
          </div>
          <div class="provider-section">
            <h3>ΔΟΚΙΜΗ ΑΠΟΣΤΟΛΗΣ</h3>
            <p>Δεν εκτελείται πραγματική αποστολή SMS από αυτόν τον πίνακα.
              Η δοκιμή απαιτεί περιορισμό συχνότητας, ρητή εξουσιοδότηση και καταγραφή.</p>
            <span class="provider-locked">Μη ενεργή · δεν αποστέλλεται SMS</span>
          </div>
        </section>

        <section class="provider-card" aria-labelledby="payabl-heading" data-testid="admin-payabl-settings">
          <div class="provider-row-heading">
            <div><h2 id="payabl-heading">PAYABL. · ΠΛΗΡΩΜΕΣ</h2>
              <p>Επιβεβαιωμένη κατάσταση δοκιμαστικού περιβάλλοντος πληρωμών.</p></div>
            <span class:ready={payabl?.configured} class="provider-state">
              {payabl?.configured?'Sandbox ρυθμισμένο':'Χωρίς επαλήθευση'}
            </span>
          </div>
          {#if payabl}
            <div class="provider-health" role="status">
              <strong>Περιβάλλον: SANDBOX</strong>
              <div class="provider-checks">
                <span>{payabl.payment_links?'✓':'○'} Δοκιμαστικοί σύνδεσμοι</span>
                <span>Live συναλλαγές: ανενεργές</span>
                <span>Συνδρομές: {payabl.subscriptions?'διαθέσιμες':'μη ενεργές'}</span>
                <span>Επιστροφές: {payabl.refunds?'διαθέσιμες':'μη ενεργές'}</span>
              </div>
            </div>
          {:else if payablError}
            <p role="alert" class="provider-error">{payablError}</p>
          {/if}
          <div class="provider-section">
            <h3>ΔΙΑΠΙΣΤΕΥΤΗΡΙΑ ΚΑΙ ΕΓΚΡΙΣΗ</h3>
            <p>Τα μυστικά API, οι υπογραφές callback και τα στοιχεία merchant δεν επιστρέφονται στον browser.
              Η πραγματική είσπραξη, οι επιστροφές και οι παραγωγικές συναλλαγές παραμένουν ξεχωριστά κλειδωμένες.</p>
            <a class="provider-link" href={"/admin/finance?organization_id="+encodeURIComponent(paymentOwner??"lefteria_fc_academy")}
              data-testid="admin-provider-payabl-finance">Άνοιγμα οικονομικών / payabl. sandbox ↗</a>
          </div>
        </section>
        <p class="admin-muted provider-footnote">Αυτή η σελίδα δείχνει μόνο επαληθευμένη κατάσταση, χωρίς διαρροή διαπιστευτηρίων ή ενεργοποίηση παρόχου. Η αποθήκευση ρυθμίσεων απαιτεί ασφαλή διαχείριση μυστικών και έλεγχο αλλαγών.</p>
      {/if}
    </div>
  </AdminFrame>
{:else}
  <AdminSessionStatus unavailable={$auth.status==='unavailable'} busy={$auth.busy}
    error={$auth.error} onRetry={()=>void auth.bootstrap()} onLogout={()=>void auth.logout()}/>
{/if}

<style>
  .provider-settings{width:100%;min-width:0;display:grid;gap:15px}
  .provider-title,.provider-row-heading{display:flex;justify-content:space-between;
    gap:16px;align-items:center;flex-wrap:wrap}
  .provider-title h1{margin:0 0 5px;font-size:clamp(22px,2vw,29px)}
  .provider-title p,.provider-row-heading p{margin:0;font-size:12px;color:#9fa5a8}
  .provider-card{border:1px solid #343638;background:#141516;border-radius:8px;padding:17px;
    display:grid;gap:15px;min-width:0}
  .provider-row-heading h2{font:850 19px/1.2 'Fira Sans Extra Condensed',sans-serif;
    margin:0 0 6px;color:#f7f8f6}
  .provider-state{border:1px solid #776444;background:#2b2417;color:#ffd386;border-radius:5px;
    padding:6px 10px;font-size:12px;font-weight:700}
  .provider-state.ready{border-color:#116446;background:#0b271e;color:#60dfaf}
  .provider-health{background:#0a1e17;border:1px solid #185943;border-radius:6px;
    padding:13px;color:#76e2bb}
  .provider-health strong{display:block;font-size:13px;margin-bottom:5px}
  .provider-health p{font-size:11px;margin:0 0 12px;color:#b2c4bc}
  .provider-checks{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));
    gap:10px;font-size:11.5px}
  .provider-section{border-top:1px solid #333637;padding-top:14px;display:grid;gap:11px}
  .provider-section h3{margin:0;font-size:13px;color:#ffc45c}
  .provider-section p{font-size:12px;color:#adb3b6;line-height:1.6;margin:0;max-width:100ch}
  .provider-fields{display:grid;gap:10px}
  .provider-fields>div{display:grid;gap:5px}
  .provider-fields span{font-size:10px;font-weight:800;color:#b7bec1}
  .provider-fields strong{padding:10px 12px;min-height:40px;border:1px solid #333738;
    border-radius:5px;background:#0c0e0e;font-size:12px;font-weight:600;color:#e1e6e7}
  .provider-note{border-left:3px solid #b27e1b;padding:9px 11px;background:#241c0e}
  .provider-locked{font-size:12px;color:#e4c68a}
  .provider-link{display:inline-flex;align-items:center;justify-self:start;min-height:44px;
    border:1px solid #a27b32;color:#ffcb67;padding:10px 13px;border-radius:5px;text-decoration:none}
  .provider-link:focus-visible,.provider-settings button:focus-visible{outline:2px solid #ffcf65;outline-offset:3px}
  .provider-error{color:#ffc3b9;font-size:13px;border-left:3px solid #f48e77;padding:8px}
  .provider-footnote{font-size:12px}
  @media(max-width:650px){.provider-card{padding:13px}.provider-checks{grid-template-columns:1fr}}
</style>
