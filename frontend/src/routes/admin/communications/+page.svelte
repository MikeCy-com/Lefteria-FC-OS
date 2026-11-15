<script lang="ts">
  import {getContext} from 'svelte';
  import {browser} from '$app/env';
  import {goto} from '$app/navigation';
  import {ADMIN_AUTH_CONTEXT,type AdminAuthController} from '../../../lib/admin/controller.ts';
  import {hasAdminPermission} from '../../../lib/admin/permissions.ts';
  import {scopedAdminOrganization} from '../../../lib/admin/admin-organization.ts';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';

  type Organization='lefteria_fc'|'lefteria_fc_academy';
  const auth=getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  // Web-admin cookie and exact organization grants only. This is a control
  // centre, not an administrative backdoor into private member conversations.
  const permitted=$derived($auth.status==='authenticated' && $auth.session &&
    ['manager','super_admin'].includes($auth.session.user.role)
    ? $auth.session.user.organizations.filter((org):org is typeof org & {id:Organization} =>
        (org.id==='lefteria_fc' &&
          hasAdminPermission($auth.session!.user.permissions,'teams:write')) ||
        (org.id==='lefteria_fc_academy' &&
          hasAdminPermission($auth.session!.user.permissions,'academy:write')))
    : []);
  let organization=$state<Organization|null>(null);
  let counts=$state<{drafts:number;published:number}|null>(null);
  let loading=$state(false);
  let error=$state<string|null>(null);

  $effect(()=>{
    if(browser && $auth.status==='anonymous')void goto('/admin/login',{replace:true});
  });
  $effect(()=>{
    const selected=scopedAdminOrganization($auth.session?.user??null,permitted.map(o=>o.id));
    if(organization!==selected)organization=selected;
  });
  $effect(()=>{
    const owner=organization,actor=$auth.session?.user.id;
    const grants=permitted.map(o=>o.id).join('|');
    void grants;
    counts=null;error=null;loading=false;
    if(!browser||!owner||!permitted.some(org=>org.id===owner))return;
    const controller=new AbortController();
    loading=true;
    void(async()=>{
      try{
        const query=new URLSearchParams({organization_id:owner});
        const [draftReply,publishedReply]=await Promise.all([
          fetch('/api/admin/communications/announcement-drafts?'+query,{
            credentials:'same-origin',cache:'no-store',signal:controller.signal
          }),
          fetch('/api/admin/communications/app-announcements?'+query,{
            credentials:'same-origin',cache:'no-store',signal:controller.signal
          })
        ]);
        if(controller.signal.aborted)return;
        if(draftReply.status===401||publishedReply.status===401){
          auth.invalidateSession();return;
        }
        if(draftReply.status===403||publishedReply.status===403){
          void auth.bootstrap();return;
        }
        if(!draftReply.ok||!publishedReply.ok)throw new Error('unavailable');
        const draftRaw:unknown=await draftReply.json();
        const publishedRaw:unknown=await publishedReply.json();
        if(!draftRaw||typeof draftRaw!=='object'||Array.isArray(draftRaw)||
           !publishedRaw||typeof publishedRaw!=='object'||Array.isArray(publishedRaw))
          throw new Error('invalid');
        const drafts=draftRaw as Record<string,unknown>;
        const announcements=publishedRaw as Record<string,unknown>;
        if(drafts.organization_id!==owner||announcements.organization_id!==owner||
           !Array.isArray(drafts.drafts)||drafts.drafts.length>100||
           !Array.isArray(announcements.announcements)||announcements.announcements.length>100||
           !drafts.drafts.every((x:unknown)=>x!==null&&typeof x==='object'&&
             !Array.isArray(x)&&(x as Record<string,unknown>).organization_id===owner&&
             (x as Record<string,unknown>).status==='draft')||
           !announcements.announcements.every((x:unknown)=>x!==null&&typeof x==='object'&&
             !Array.isArray(x)&&(x as Record<string,unknown>).organization_id===owner&&
             (x as Record<string,unknown>).status==='published'))
          throw new Error('invalid');
        if(!controller.signal.aborted&&organization===owner&&actor===$auth.session?.user.id)
          counts={drafts:drafts.drafts.length,published:announcements.announcements.length};
      }catch{
        if(!controller.signal.aborted&&organization===owner&&actor===$auth.session?.user.id)
          error='Δεν είναι διαθέσιμη η σύνοψη ανακοινώσεων. Η διαχείριση παραμένει προσβάσιμη.';
      }finally{if(!controller.signal.aborted)loading=false;}
    })();
    return ()=>controller.abort();
  });
</script>

<svelte:head><title>Εφαρμογή · Επικοινωνία | Λευτεριά FC</title></svelte:head>

{#if $auth.status==='authenticated' && $auth.session}
  <AdminFrame user={$auth.session.user} active="communications" busy={$auth.busy}
    onLogout={()=>void auth.logout()}>
    <div class="comms-intro" data-testid="app-communications-centre">
      <p class="admin-eyebrow">ΙΔΙΩΤΙΚΗ ΕΦΑΡΜΟΓΗ · ΕΠΙΚΟΙΝΩΝΙΑ</p>
      <h1>Επικοινωνία μελών</h1>
      <p class="admin-muted">Ανακοινώσεις, συνομιλίες, ειδοποιήσεις και προστατευμένα αρχεία.
        Αφορά αποκλειστικά την εφαρμογή μελών, όχι τη δημόσια ιστοσελίδα ή το CRM.</p>
    </div>

    {#if permitted.length===0}
      <section class="admin-panel" data-testid="communications-access-denied">
        <h2>Δεν υπάρχει άδεια διαχείρισης</h2>
        <p class="admin-muted">Απαιτείται ενεργός ρόλος Manager ή Super Admin και δικαίωμα
          επεξεργασίας του αντίστοιχου οργανισμού.</p>
      </section>
    {:else}
      <section class="comms-context" aria-label="Ενεργό πλαίσιο πρόσβασης">
        <div>
          <span class="comms-small">ΕΝΕΡΓΟΣ ΟΡΓΑΝΙΣΜΟΣ</span>
          <strong>{organization==='lefteria_fc_academy'?'Ακαδημία':organization==='lefteria_fc'?'Πρώτη ομάδα':'Δεν επιλέχθηκε οργανισμός'}</strong>
        </div>
        <p>Τα μέλη και οι ομάδες επαληθεύονται ξανά στο API. Τα δεδομένα της
          Ακαδημίας και της Πρώτης ομάδας δεν αναμειγνύονται.</p>
      </section>

      <div class="comms-grid" data-testid="communications-four-areas">
        <section class="comms-card" aria-labelledby="comms-announcements">
          <p class="comms-index">01 / ΔΗΜΟΣΙΕΥΣΗ ΣΤΗΝ ΕΦΑΡΜΟΓΗ</p>
          <h2 id="comms-announcements">Ανακοινώσεις</h2>
          <p>Δημιουργία πρόχειρων, στόχευση πραγματικής ομάδας, δημοσίευση,
            ανάγνωση από μέλη και απόσυρση από το web admin.</p>
          {#if loading}
            <p class="comms-count" role="status">Φόρτωση πραγματικών στοιχείων…</p>
          {:else if counts}
            <p class="comms-count" data-testid="communications-announcement-counts">
              <strong>{counts.drafts}</strong> πρόχειρα <span aria-hidden="true">·</span>
              <strong>{counts.published}</strong> δημοσιευμένες
            </p>
          {:else if error}
            <p class="comms-count" role="status">{error}</p>
          {/if}
          <a href="/admin/announcements" class="comms-link">Διαχείριση ανακοινώσεων <span aria-hidden="true">↗</span></a>
        </section>

        <section class="comms-card" aria-labelledby="comms-messages">
          <p class="comms-index">02 / ΣΥΝΟΜΙΛΙΕΣ ΜΕΛΩΝ</p>
          <h2 id="comms-messages">Μηνύματα</h2>
          <p>Η εφαρμογή διαθέτει ομαδικές συνομιλίες και ιδιωτικά αιτήματα με αποδοχή.
            Ομαδικές αντιδράσεις και ανάγνωση ελέγχονται ανά ενεργό μέλος.</p>
          <p class="comms-note">Η διαχείριση/εποπτεία συνομιλιών από το web admin
            δεν είναι διαθέσιμη. Καμία προβολή ιδιωτικού ιστορικού τρίτων.</p>
          <span class="comms-limited">Διαχειριστικό API σε ανάπτυξη</span>
        </section>

        <section class="comms-card" aria-labelledby="comms-notifications">
          <p class="comms-index">03 / ΠΑΡΑΔΟΣΗ & ΕΝΗΜΕΡΩΣΕΙΣ</p>
          <h2 id="comms-notifications">Ειδοποιήσεις</h2>
          <p>Υπάρχει ιδιωτικό inbox και υποδομή Web Push με ρητή συγκατάθεση
            συσκευής. Η αποστολή εξαρτάται από τη διαμόρφωση του παρόχου.</p>
          <p class="comms-note">Η χειροκίνητη αποστολή, η διαχείριση απόπειρων
            και οι επαναλήψεις από το web admin δεν έχουν ενεργοποιηθεί.</p>
          <span class="comms-limited">Διαχειριστικό API σε ανάπτυξη</span>
        </section>

        <section class="comms-card" aria-labelledby="comms-attachments">
          <p class="comms-index">04 / ΠΡΟΣΤΑΤΕΥΜΕΝΑ ΑΡΧΕΙΑ</p>
          <h2 id="comms-attachments">Συνημμένα</h2>
          <p>Ένα banner και έως τρία PDF ανά πρόχειρη ανακοίνωση, με
            ελεγχόμενη λήψη μόνο για τα μέλη που δικαιούνται την ανακοίνωση.</p>
          <p class="comms-note">Δεν υπάρχει γενική βιβλιοθήκη συνημμένων
            συνομιλιών. Τα αρχεία δεν δημοσιεύονται στον δημόσιο ιστότοπο.</p>
          <a href="/admin/announcements" class="comms-link">Αρχεία ανακοινώσεων <span aria-hidden="true">↗</span></a>
        </section>
      </div>

      <section class="comms-security" aria-labelledby="comms-security-heading">
        <h2 id="comms-security-heading">Προστασία επικοινωνίας</h2>
        <p>Ορατότητα μόνο σε ενεργά εξουσιοδοτημένα μέλη της συγκεκριμένης ομάδας.
          Η πόλη, η τοποθεσία, οι αναθέσεις προπονητών και η σύνδεση γονέα–παιδιού
          πρέπει να ελέγχονται από τη βάση δεδομένων σε κάθε ανάγνωση και ενέργεια.
          Η καταχώριση εδώ δεν παρακάμπτει τα δικαιώματα της εφαρμογής.</p>
      </section>
    {/if}
  </AdminFrame>
{:else}
  <AdminSessionStatus unavailable={$auth.status==='unavailable'} busy={$auth.busy}
    error={$auth.error} onRetry={()=>void auth.bootstrap()} onLogout={()=>void auth.logout()}/>
{/if}

<style>
  .comms-intro{max-width:860px;margin-bottom:22px}
  .comms-intro h1{font:800 clamp(32px,4vw,50px)/1 'Fira Sans Extra Condensed',sans-serif;
    text-transform:uppercase;margin:7px 0 10px}
  .comms-context{display:flex;align-items:center;justify-content:space-between;gap:20px;
    padding:19px 22px;margin:0 0 18px;border:1px solid #665431;background:#191c1c;
    border-left:4px solid #edba4d}
  .comms-context div{display:grid;gap:6px}
  .comms-small,.comms-index{color:#ecbb58;letter-spacing:.12em;font-size:11px;font-weight:800}
  .comms-context strong{font-size:19px}
  .comms-context p{max-width:450px;margin:0;color:#cfd1cf;font-size:13px;line-height:1.6}
  .comms-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:15px}
  .comms-card{display:flex;flex-direction:column;align-items:flex-start;gap:12px;min-height:292px;
    padding:25px;background:linear-gradient(145deg,#24251e,#151819 70%);border:1px solid #62583e}
  .comms-index,.comms-card h2,.comms-card p{margin:0}
  .comms-card h2{color:#fff;font:800 clamp(25px,3vw,33px)/1 'Fira Sans Extra Condensed',sans-serif;
    text-transform:uppercase}
  .comms-card>p:not(.comms-index):not(.comms-count):not(.comms-note){
    color:#e2e2df;font-size:14px;line-height:1.6}
  .comms-note{color:#bec2c3;font-size:13px;line-height:1.55}
  .comms-count{font-size:14px;color:#d9d3c4;line-height:1.6}
  .comms-count strong{font-size:22px;color:#f3c465}
  .comms-link{display:inline-flex;gap:16px;align-items:center;margin-top:auto;padding:12px 16px;
    border:1px solid #a17e38;background:#5e431e;color:#fff;font-weight:750;text-decoration:none}
  .comms-link:hover{background:#745322;border-color:#edc576}
  .comms-link:focus-visible{outline:3px solid #ffd271;outline-offset:3px}
  .comms-limited{margin-top:auto;border:1px solid #5b5e60;padding:10px 12px;
    color:#d0d1d2;font-size:12px;font-weight:700}
  .comms-security{margin-top:18px;padding:23px;background:#171919;border:1px solid #444a4a}
  .comms-security h2{margin:0 0 9px;text-transform:uppercase;font-size:19px}
  .comms-security p{margin:0;color:#d3d6d5;line-height:1.7;font-size:14px}
  @media(max-width:800px){.comms-grid{grid-template-columns:1fr}.comms-context{align-items:flex-start;flex-direction:column}.comms-card{min-height:unset}}
  @media(prefers-reduced-motion:no-preference){.comms-link{transition:background .15s,border-color .15s}}
</style>
