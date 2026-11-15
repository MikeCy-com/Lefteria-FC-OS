<script lang="ts">
  import {getContext} from 'svelte';
  import {browser} from '$app/env';
  import {goto} from '$app/navigation';
  import {ADMIN_AUTH_CONTEXT,type AdminAuthController} from '../../../../lib/admin/controller.ts';
  import {hasAdminPermission} from '../../../../lib/admin/permissions.ts';
  import {scopedAdminOrganization} from '../../../../lib/admin/admin-organization.ts';
  import {validFanHubOverview,type FanHubOverview,type FanHubOwner,type FanTrack}
    from '../../../../lib/admin/hub-fan-overview.ts';
  import AdminFrame from '../../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../../lib/admin/AdminSessionStatus.svelte';

  const auth=getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  // Aggregate organization-wide finance is ONLY visible to a Super Admin.
  // The API and PostgreSQL revalidate live authority independently.
  const permitted=$derived($auth.status==='authenticated'&&$auth.session&&
    $auth.session.user.role==='super_admin'&&
    hasAdminPermission($auth.session.user.permissions,'finance:read')
      ? $auth.session.user.organizations.filter(org=>
          org.id==='lefteria_fc'||org.id==='lefteria_fc_academy')
          .map(org=>org.id as FanHubOwner) : []);
  let owner=$state<FanHubOwner|null>(null);
  let overview=$state<FanHubOverview|null>(null);
  let loading=$state(false),error=$state<string|null>(null),reload=$state(0);
  const names:Record<FanTrack,string>={
    parent:'Θεματολογία γονέων',player:'Θεματολογία αθλητών',both:'Γονέων & αθλητών'
  };
  const euro=(cents:number)=>new Intl.NumberFormat('el-CY',{
    style:'currency',currency:'EUR'
  }).format(cents/100);

  $effect(()=>{if(browser&&$auth.status==='anonymous')void goto('/admin/login',{replace:true});});
  $effect(()=>{
    const selected=scopedAdminOrganization($auth.session?.user??null,permitted);
    if(owner!==selected)owner=selected;
  });
  $effect(()=>{
    const selected=owner,actor=$auth.session?.user.id,generation=reload;
    void actor;void generation;
    overview=null;error=null;
    if(!browser||!selected||!permitted.includes(selected))return;
    const controller=new AbortController();
    loading=true;
    void(async()=>{
      try{
        const response=await fetch('/api/admin/hub/fan-subscriptions/overview?organization_id='+
          encodeURIComponent(selected),{
          credentials:'same-origin',cache:'no-store',signal:controller.signal
        });
        if(controller.signal.aborted)return;
        if(response.status===401){auth.invalidateSession();return;}
        if(response.status===403){void auth.bootstrap();return;}
        if(!response.ok)throw new Error();
        const data:unknown=await response.json();
        if(!validFanHubOverview(data,selected))throw new Error();
        if(!controller.signal.aborted&&owner===selected&&
           $auth.session?.user.id===actor)overview=data;
      }catch{
        if(!controller.signal.aborted)error='Δεν επιβεβαιώθηκαν τα στοιχεία συνδρομών.';
      }finally{
        if(!controller.signal.aborted)loading=false;
      }
    })();
    return()=>controller.abort();
  });
</script>

<svelte:head><title>Συνδρομές HUB · Lefteria FC</title></svelte:head>
{#if $auth.status==='authenticated'&&$auth.session}
  <AdminFrame active="hub" user={$auth.session.user} busy={$auth.busy||loading}
    onLogout={()=>void auth.logout()}>
    <main class="fan-admin" data-testid="hub-fan-overview-workspace">
      <header>
        <p class="eyebrow">ΔΙΑΧΕΙΡΙΣΗ · HUB</p>
        <h1>Συνδρομές φιλάθλων</h1>
        <p class="intro">Επαληθευμένη, συγκεντρωτική εικόνα ανά οργανισμό.
          Δεν εμφανίζονται προσωπικά στοιχεία συνδρομητών, στοιχεία καρτών ή αριθμοί πληρωμών.</p>
        <a href="/admin/hub">Επιστροφή στον έλεγχο περιεχομένου</a>
      </header>
      {#if permitted.length}
        <div class="toolbar">
          <label>Οργανισμός
            <select value={owner??''} disabled={loading} onchange={event=>{
              const selected=event.currentTarget.value;
              if(permitted.some(o=>o===selected))owner=selected as FanHubOwner;
            }}>
              {#each permitted as item}
                <option value={item}>{item==='lefteria_fc'?'Πρώτη ομάδα':'Ακαδημία'}</option>
              {/each}
            </select>
          </label>
          <button type="button" disabled={loading} onclick={()=>reload++}>Ανανέωση</button>
        </div>
        {#if loading}<p role="status">Φόρτωση επαληθευμένων στοιχείων…</p>{/if}
        {#if error}<p role="alert" class="error">{error}</p>{/if}
        {#if overview}
          <p class="timestamp">Εικόνα κατά την {new Intl.DateTimeFormat('el-CY',{
            dateStyle:'medium',timeStyle:'short',timeZone:'Asia/Nicosia'
          }).format(new Date(overview.as_of))} (ώρα Κύπρου)</p>
          <section aria-labelledby="status-title">
            <h2 id="status-title">Καταστάσεις συνδρομών</h2>
            <div class="metrics">
              <article class="metric primary">
                <p>Ενεργές με επιβεβαιωμένη πληρωμή</p>
                <strong>{overview.counts.verified_active}</strong>
              </article>
              <article class="metric">
                <p>Καταχωρισμένες ως ενεργές</p>
                <strong>{overview.counts.recorded_active}</strong>
              </article>
              <article class="metric">
                <p>Σε αναμονή πληρωμής</p>
                <strong>{overview.counts.pending}</strong>
              </article>
              <article class="metric">
                <p>Με ληξιπρόθεσμη κατάσταση</p>
                <strong>{overview.counts.past_due}</strong>
              </article>
              <article class="metric">
                <p>Ακυρωμένες</p>
                <strong>{overview.counts.cancelled}</strong>
              </article>
              <article class="metric">
                <p>Ληγμένες</p>
                <strong>{overview.counts.expired}</strong>
              </article>
            </div>
            <p class="note">Συνολικές εγγραφές: <strong>{overview.counts.total_records}</strong>.
              Η κατάσταση «ενεργή» στη βάση δεν αποτελεί από μόνη της απόδειξη πληρωμής.
              Η επιβεβαίωση ελέγχει την πραγματική συναλλαγή Payabl, τη λήξη, τον λογαριασμό φιλάθλου
              και τυχόν επιστροφή ή αναστροφή.</p>
            {#if overview.counts.recorded_active>overview.counts.verified_active}
              <p class="warning" role="status">
                Υπάρχουν {overview.counts.recorded_active-overview.counts.verified_active}
                καταχωρίσεις «ενεργές» χωρίς τρέχουσα πλήρη επιβεβαίωση. Δεν παρουσιάζονται ως έγκυρη πρόσβαση.
              </p>
            {/if}
          </section>
          <section class="plans" aria-labelledby="plans-title">
            <h2 id="plans-title">Προγράμματα δοκιμαστικού Payabl</h2>
            {#if overview.plans.length}
              <div class="table-scroll">
                <table>
                  <thead><tr><th>Πρόγραμμα</th><th>Τιμή EUR</th><th>Περίοδος</th><th>Διαθεσιμότητα</th></tr></thead>
                  <tbody>
                    {#each overview.plans as plan (plan.track)}
                      <tr>
                        <th scope="row">{names[plan.track]}</th>
                        <td>{euro(plan.monthly_price_cents)}</td>
                        <td>{plan.duration_days} ημέρες</td>
                        <td>{plan.is_enabled?'Ενεργό μόνο στο sandbox':'Ανενεργό'}</td>
                      </tr>
                    {/each}
                  </tbody>
                </table>
              </div>
            {:else}
              <p>Δεν έχουν καταχωριστεί τιμές συνδρομής για αυτόν τον οργανισμό.</p>
            {/if}
            <p class="note">Η εμφάνιση ενός τιμοκαταλόγου δεν ενεργοποιεί πραγματικές πληρωμές.
              Οι τιμές ορίζονται μόνο από εξουσιοδοτημένο διαχειριστή υποδομής σε απομονωμένο sandbox.
              Οι ανανεώσεις παραμένουν χειροκίνητες· δεν υπάρχει αποθήκευση κάρτας.</p>
          </section>
          <section class="privacy">
            <h2>Διαχωρισμός περιεχομένου και ρόλων</h2>
            <p>Ο φίλαθλος μπορεί να επιλέξει θεματολογία γονέα, αθλητή ή και τα δύο.
              Κάθε νέα συνδρομή ξεκινά ανεξάρτητη εκπαιδευτική διαδρομή από το πρώτο μάθημα.
              Η επιλογή θεματολογίας δεν δημιουργεί δικαίωμα κηδεμόνα ούτε πρόσβαση σε δεδομένα παιδιών.</p>
            <p class="note">Η σελίδα είναι μόνο για ανάγνωση. Καμία αλλαγή χρέωσης, ακύρωση,
              επανενεργοποίηση ή αποστολή μηνύματος δεν μπορεί να γίνει εδώ.</p>
          </section>
        {:else if !loading&&!error}
          <p>Δεν υπάρχουν ακόμη επαληθευμένα συγκεντρωτικά στοιχεία.</p>
        {/if}
      {:else}
        <p>Η συγκεντρωτική διαχείριση συνδρομών είναι διαθέσιμη μόνο σε εξουσιοδοτημένο Super Admin.</p>
      {/if}
    </main>
  </AdminFrame>
{:else}
  <AdminSessionStatus unavailable={$auth.status==='unavailable'} busy={$auth.busy}
    error={$auth.error} onRetry={()=>void auth.bootstrap()} onLogout={()=>void auth.logout()}/>
{/if}
<style>
  .fan-admin{max-width:1260px;margin:0 auto;padding:24px;color:var(--admin-text,#f4f4f4)}
  .fan-admin h1{font-size:clamp(28px,4vw,42px)}
  .fan-admin h2{font-size:23px;margin:0 0 15px}
  .intro{max-width:790px;line-height:1.65;color:#d2d5d5}
  .fan-admin a{color:#f4cc75}
  .eyebrow{color:#dbaa4f;letter-spacing:.13em;font-size:11px;font-weight:800}
  .toolbar{display:flex;flex-wrap:wrap;align-items:end;gap:15px;margin:24px 0}
  label{display:grid;gap:7px;font-size:13px;font-weight:700}
  select,button{min-height:44px;padding:9px 14px;background:#252826;color:#ffdb89;
    border:1px solid #6d5a34;border-radius:9px}
  button:disabled,select:disabled{opacity:.5;cursor:default}
  select:focus-visible,button:focus-visible,a:focus-visible{outline:3px solid #f4c45c;outline-offset:3px}
  section{padding:21px;background:#1b1f20;border:1px solid #484a45;border-radius:13px;margin:20px 0}
  .metrics{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px}
  .metric{min-width:0;padding:17px;background:#242827;border:1px solid #414744;border-radius:10px}
  .metric p{font-size:13px;color:#ddd;line-height:1.45;margin:0 0 13px}
  .metric strong{display:block;font-size:34px;color:#f7d68a;font-variant-numeric:tabular-nums}
  .metric.primary{border-color:#af8a44}
  .note,.timestamp{color:#c2c7c6;font-size:13px;line-height:1.65}
  .table-scroll{overflow-x:auto}
  table{border-collapse:collapse;width:100%;min-width:570px;text-align:left}
  th,td{padding:13px;border-bottom:1px solid #434846}
  th{font-size:13px}td{font-size:14px}
  .warning,.error{border:1px solid #b88f49;border-radius:9px;padding:13px;color:#ffe1a5}
  @media(max-width:760px){.metrics{grid-template-columns:repeat(2,minmax(0,1fr))}.fan-admin{padding:14px}}
  @media(max-width:440px){.metrics{grid-template-columns:1fr}}
</style>
