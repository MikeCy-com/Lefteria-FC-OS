<script lang="ts">
  import { untrack } from 'svelte';
  import { verifiedFinanceOverview, type FinanceOverview, type FinanceOwner } from './finance-overview.ts';

  let { owner, role, sessionId }:{
    owner:FinanceOwner|null;role:string;sessionId:string
  }=$props();
  let summary=$state<FinanceOverview|null>(null);
  let state=$state<'idle'|'loading'|'ready'|'error'>('idle');
  let refresh=$state(0);
  const allowed=$derived(role==='accountant'||role==='super_admin');
  const money=(cents:number)=>new Intl.NumberFormat('el-CY',{
    style:'currency',currency:'EUR',maximumFractionDigits:2
  }).format(cents/100);

  $effect(()=>{
    const selected=owner,actor=sessionId,permission=allowed,version=refresh;
    void actor;void version;
    state='idle';summary=null;
    if(!permission||!selected)return;
    const controller=new AbortController();
    state='loading';
    untrack(()=>void(async()=>{
      try {
        const response=await fetch('/api/admin/finance/overview?organization_id='+
          encodeURIComponent(selected),{
          credentials:'same-origin',cache:'no-store',
          headers:{Accept:'application/json'},signal:controller.signal
        });
        if(!response.ok)throw new Error('Finance overview unavailable');
        const data:unknown=await response.json();
        if(!verifiedFinanceOverview(data,selected))throw new Error('Unverified ledger totals');
        if(!controller.signal.aborted && owner===selected && sessionId===actor){
          summary=data;state='ready';
        }
      }catch{
        if(!controller.signal.aborted)state='error';
      }
    })());
    return()=>controller.abort();
  });
</script>

<section class="finance-cockpit" data-testid="finance-ledger-overview" aria-label="Επισκόπηση οικονομικών">
  <div class="finance-cockpit-heading">
    <div>
      <p class="admin-eyebrow">FINANCE CONTROL · ΕΛΕΓΧΟΜΕΝΗ ΠΡΟΣΒΑΣΗ</p>
      <h2>Οικονομική επισκόπηση</h2>
      <p>Αυτόνομες εγγραφές ανά οργανισμό, χωρίς ανάμειξη Ακαδημίας και πρώτης ομάδας.</p>
    </div>
    {#if allowed}
      <button class="admin-secondary" type="button" disabled={state==='loading'}
        onclick={()=>refresh+=1}>Ανανέωση στοιχείων</button>
    {/if}
  </div>
  {#if !allowed}
    <p class="finance-note" role="status">Η συνολική οικονομική αναφορά απαιτεί ρόλο Accountant ή Super Admin.
      Οι υπεύθυνοι τοποθεσιών πρέπει να χρησιμοποιούν αναφορές περιορισμένες στις αναθέσεις τους.</p>
  {:else if state==='loading'}
    <p class="finance-note" role="status">Φόρτωση επαληθευμένων οικονομικών εγγραφών…</p>
  {:else if state==='error'}
    <p class="finance-note" role="alert">Δεν κατέστη δυνατή η επαλήθευση της αναφοράς. Δεν εμφανίζονται υποθετικά ποσά.</p>
  {:else if summary}
    <div class="finance-snapshot-label">
      <strong>{summary.organization_id==='lefteria_fc'?'Πρώτη ομάδα':'Ακαδημία'}</strong>
      <span>Σύνολο διαθέσιμου ιστορικού · {new Intl.DateTimeFormat('el-CY',{
        dateStyle:'medium',timeStyle:'short',timeZone:'Asia/Nicosia'
      }).format(new Date(summary.generated_at))}</span>
    </div>
    <div class="finance-metrics">
      {#if owner==='lefteria_fc_academy'}
        <article class="finance-metric" data-testid="finance-academy-fee-totals">
          <p class="finance-metric-label">Μηνιαία δίδακτρα αθλητών</p>
          <strong>{money(summary.training_fees.issued_cents)}</strong>
          <span>Εκδοθείσες χρεώσεις · {summary.training_fees.count}</span>
          <dl>
            <div><dt>Καταχωρισμένα ως πληρωμένα</dt><dd>{money(summary.training_fees.paid_cents)}</dd></div>
            <div><dt>Εκκρεμείς οφειλές</dt><dd>{money(summary.training_fees.outstanding_cents)}</dd></div>
            <div><dt>Υπό έλεγχο</dt><dd>{money(summary.training_fees.review_cents)}</dd></div>
          </dl>
        </article>
      {/if}
      <article class="finance-metric" data-testid="finance-general-charge-totals">
        <p class="finance-metric-label">Λοιπές χρεώσεις · ιδιωτικό καθολικό</p>
        <strong>{money(summary.general_charges.issued_cents)}</strong>
        <span>Ενεργές χρεώσεις · {summary.general_charges.count}</span>
        <dl>
          <div><dt>Καταχωρισμένες εισπράξεις</dt><dd>{money(summary.general_charges.collected_cents)}</dd></div>
          <div><dt>Υπόλοιπο οφειλών</dt><dd>{money(summary.general_charges.outstanding_cents)}</dd></div>
          <div><dt>Ληξιπρόθεσμα</dt><dd>{money(summary.general_charges.overdue_cents)}</dd></div>
          <div><dt>Ακυρωμένες εγγραφές</dt><dd>{summary.general_charges.cancelled_count}</dd></div>
        </dl>
      </article>
      <article class="finance-metric finance-metric-sandbox" data-testid="finance-payabl-unattached">
        <p class="finance-metric-label">Payabl · ανεξάρτητα δοκιμαστικά αιτήματα</p>
        <strong>{money(summary.unattached_payabl_requests.settled_cents)}</strong>
        <span>Μόνο sandbox · {summary.unattached_payabl_requests.count} αιτήματα χωρίς σύνδεση σε δίδακτρα</span>
        <dl>
          <div><dt>Αναμονή</dt><dd>{money(summary.unattached_payabl_requests.pending_cents)}</dd></div>
          <div><dt>Προς συμφωνία</dt><dd>{money(summary.unattached_payabl_requests.review_cents)}</dd></div>
        </dl>
      </article>
    </div>
    <p class="finance-note" data-testid="finance-aggregate-caveat">
      Οι τρεις πηγές εμφανίζονται χωριστά για να αποφευχθεί διπλομέτρηση.
      Τα δεδομένα δεν αποτελούν ισολογισμό, επίσημη κατάσταση αποτελεσμάτων ή επιβεβαιωμένο ταμειακό υπόλοιπο.
      Η καταχώριση εξόδων, οι τραπεζικές συμφωνίες και η παραγωγική διασύνδεση πληρωμών δεν είναι ακόμη διαθέσιμες.
    </p>
  {:else}
    <p class="finance-note">Επιλέξτε οργανισμό για επαληθευμένα οικονομικά στοιχεία.</p>
  {/if}
</section>

<style>
  .finance-cockpit{border:1px solid #d7dee8;border-radius:23px;padding:clamp(16px,2.5vw,28px);background:linear-gradient(145deg,#fbfcff 0%,#f1f5fa 100%);color:#17243b;box-shadow:0 16px 42px #1326440c;margin:0 0 1.3rem}
  .finance-cockpit-heading{display:flex;align-items:start;justify-content:space-between;gap:20px;flex-wrap:wrap}
  .finance-cockpit-heading h2{font-size:clamp(21px,2.6vw,29px);margin:4px 0 7px;color:#12233e}
  .finance-cockpit-heading p:last-child{margin:0;line-height:1.55;color:#536179}
  .finance-cockpit-heading button{flex-shrink:0}
  .finance-snapshot-label{display:flex;gap:12px;align-items:center;flex-wrap:wrap;margin:22px 0 12px;color:#3a4b63;font-size:.85rem}
  .finance-snapshot-label strong{background:#102846;color:#fff;border-radius:20px;padding:7px 14px}
  .finance-metrics{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,245px),1fr));gap:12px}
  .finance-metric{background:#fff;border:1px solid #dce4ed;border-radius:17px;padding:18px;min-width:0}
  .finance-metric-label{text-transform:uppercase;letter-spacing:.06em;font-size:.76rem;font-weight:750;color:#51627d;margin:0 0 12px}
  .finance-metric>strong{display:block;font-size:clamp(23px,2.7vw,31px);line-height:1.2;font-variant-numeric:tabular-nums;color:#132845;overflow-wrap:anywhere}
  .finance-metric>span{display:block;font-size:.8rem;color:#66758c;margin:8px 0 17px;line-height:1.5}
  .finance-metric dl{display:grid;gap:12px;margin:0;padding:14px 0 0;border-top:1px solid #e7ebf2}
  .finance-metric dl div{display:flex;justify-content:space-between;gap:12px;flex-wrap:wrap}
  .finance-metric dt{font-size:.79rem;color:#607089}
  .finance-metric dd{margin:0;font-weight:740;font-variant-numeric:tabular-nums}
  .finance-metric-sandbox{background:#fdfaf2;border-color:#e9dfc5}
  .finance-note{margin:17px 0 0;background:#e8edf5;border-radius:11px;padding:12px 14px;color:#34465e;font-size:.82rem;line-height:1.65}
  @media(max-width:560px){.finance-cockpit{border-radius:16px}.finance-metric{padding:15px}}
</style>
