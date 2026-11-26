<script lang="ts">
  import PublicShell from '../../lib/public/PublicShell.svelte';
  import PublicIcon from '../../lib/public/PublicIcon.svelte';
  import Unavailable from '../../lib/public/Unavailable.svelte';
  import type { PublicPageData } from '../../lib/public/types';
  import { safeContactEmail, contactEmailDraftHref } from '../../lib/public/contact-email';

  let { data }: { data: PublicPageData & {contactLead:string|null} } = $props();
  const club = $derived(data.site?.club);
  const owner = $derived(data.organization === 'lefteria_fc_academy' ? 'ΑΚΑΔΗΜΙΑ' : 'ΛΕΥΤΕΡΙΑ FC');
  const validEmail = $derived(safeContactEmail(club?.email));
  let senderName = $state('');
  let senderEmail = $state('');
  let subject = $state('');
  let message = $state('');
  let mailClientOpened = $state(false);

  // The old website POSTed /api/contact, which has not yet been safely migrated.
  // This route deliberately does not claim a sent/saved enquiry. It opens the
  // visitor's own email client, which retains control of final transmission.
  function openEmailDraft(event: SubmitEvent) {
    event.preventDefault();
    mailClientOpened = false;
    const href = contactEmailDraftHref(validEmail,{
      name:senderName,replyEmail:senderEmail,
      organizationLabel:owner,subject,message
    });
    if (!href) return;
    window.location.href = href;
    mailClientOpened = true;
  }
</script>

<svelte:head>
  <title>ΕΠΙΚΟΙΝΩΝΙΑ | ΛΕΥΤΕΡΙΑ FC</title>
  <meta name="description" content="Επικοινωνήστε με τη Λευτεριά FC και την Ακαδημία. Διεύθυνση, τηλέφωνο και email από τα επίσημα δημοσιευμένα στοιχεία." />
</svelte:head>

<PublicShell site={data.site} branding={data.branding} organization={data.organization} navigationGroups={data.navigationGroups}>
  <section class="public-page-heading contact-page-heading" data-testid="contact-page">
    <p class="public-eyebrow">ΕΠΙΚΟΙΝΩΝΗΣΤΕ ΜΑΖΙ ΜΑΣ</p>
    <h1>ΕΠΙΚΟΙΝΩΝΙΑ</h1>
    <p class="contact-lead">{data.contactLead ?? 'Εδώ θα βρείτε τους επίσημους τρόπους επικοινωνίας με τη Λευτεριά FC και την Ακαδημία.'}</p>
    <nav class="public-subnav" aria-label="Στοιχεία οργανισμού">
      <a href="/contact?organization_id=lefteria_fc" aria-current={data.organization === 'lefteria_fc' ? 'page' : undefined}>ΛΕΥΤΕΡΙΑ FC</a>
      <a href="/contact?organization_id=lefteria_fc_academy" aria-current={data.organization === 'lefteria_fc_academy' ? 'page' : undefined}>ΑΚΑΔΗΜΙΑ</a>
    </nav>
  </section>

  {#if !data.available}
    <Unavailable />
  {:else}
    <section class="contact-original-layout public-section" data-testid="contact-details">
      <div class="contact-info-column">
        <h2>ΠΛΗΡΟΦΟΡΙΕΣ ΣΥΛΛΟΓΟΥ</h2>
        <p class="contact-section-caption">{owner} · Επιβεβαιωμένα στοιχεία επικοινωνίας</p>
        <div class="contact-info-cards">
          {#if club?.address}
            <article class="contact-info-card">
              <span class="contact-icon"><PublicIcon name="pin" size={26} /></span>
              <div><h3>ΔΙΕΥΘΥΝΣΗ</h3><p>{club.address}</p>
                {#if club.city || club.country}<p>{[club.city,club.country].filter(Boolean).join(' · ')}</p>{/if}
              </div>
            </article>
          {/if}
          {#if club?.email}
            <article class="contact-info-card">
              <span class="contact-icon"><PublicIcon name="mail" size={26} /></span>
              <div><h3>EMAIL</h3>
                <p><a href={'mailto:'+club.email}>{club.email}</a></p>
              </div>
            </article>
          {/if}
          {#if club?.phone}
            <article class="contact-info-card">
              <span class="contact-icon"><PublicIcon name="phone" size={26} /></span>
              <div><h3>ΤΗΛΕΦΩΝΟ</h3><p><a href={'tel:'+club.phone}>{club.phone}</a></p></div>
            </article>
          {/if}
          {#if !club?.address && !club?.phone && !club?.email}
            <div class="contact-info-card"><p class="public-empty">Δεν έχουν καταχωριστεί στοιχεία επικοινωνίας για την επιλεγμένη ενότητα.</p></div>
          {/if}
        </div>
        <div class="contact-other-links">
          <a href={data.organization === 'lefteria_fc_academy' ? '/academy/registration' : '/events'}>
            {data.organization === 'lefteria_fc_academy' ? 'ΠΛΗΡΟΦΟΡΙΕΣ ΕΓΓΡΑΦΗΣ ΑΚΑΔΗΜΙΑΣ' : 'ΕΚΔΗΛΩΣΕΙΣ ΤΟΥ ΣΥΛΛΟΓΟΥ'}
            <PublicIcon name="arrow" size={17} />
          </a>
        </div>
      </div>

      <div class="contact-form-column">
        <h2>ΣΤΕΙΛΤΕ ΜΗΝΥΜΑ</h2>
        <p class="contact-section-caption">Συμπληρώστε τα στοιχεία σας και ανοίξτε την εφαρμογή email για να αποστείλετε το μήνυμα.</p>
        <form class="contact-message-form" onsubmit={openEmailDraft} data-testid="contact-form">
          <label for="contact-name">ΟΝΟΜΑΤΕΠΩΝΥΜΟ</label>
          <input id="contact-name" data-testid="contact-name" type="text" required minlength="2" maxlength="120"
            autocomplete="name" bind:value={senderName} placeholder="Εισάγετε το όνομά σας" />
          <label for="contact-email">EMAIL</label>
          <input id="contact-email" data-testid="contact-email" type="email" required maxlength="254"
            autocomplete="email" bind:value={senderEmail} placeholder="Εισάγετε το email σας" />
          <label for="contact-subject">ΘΕΜΑ</label>
          <select id="contact-subject" data-testid="contact-subject" bind:value={subject} required>
            <option value="" disabled>Επιλέξτε θέμα</option>
            <option value="Γενική Ερώτηση">Γενική Ερώτηση</option>
            <option value="Εγγραφή Ακαδημίας">Εγγραφή Ακαδημίας</option>
            <option value="Πληροφορίες Εισιτηρίων">Πληροφορίες Εισιτηρίων</option>
            <option value="Συνεργασία/Χορηγία">Συνεργασία/Χορηγία</option>
            <option value="Άλλο">Άλλο</option>
          </select>
          <label for="contact-message">ΜΗΝΥΜΑ</label>
          <textarea id="contact-message" data-testid="contact-message" required maxlength="2000"
            bind:value={message} placeholder="Γράψτε το μήνυμά σας" rows="5"></textarea>
          {#if validEmail}
            <button class="public-button contact-send-button" type="submit" data-testid="contact-submit">
              ΑΝΟΙΓΜΑ EMAIL ΓΙΑ ΑΠΟΣΤΟΛΗ <PublicIcon name="arrow" size={18} />
            </button>
            {#if mailClientOpened}
              <p role="status" class="contact-compose-status" data-testid="contact-email-compose-status">
                Το πρόχειρο άνοιξε στην εφαρμογή email σας. Για να ολοκληρωθεί η επικοινωνία, πατήστε Αποστολή εκεί. Δεν έχουμε λάβει ακόμη επιβεβαίωση αποστολής.
              </p>
            {/if}
          {:else}
            <p class="contact-unavailable" role="status">
              Δεν έχει οριστεί έγκυρη διεύθυνση email για την επιλεγμένη ενότητα. Επιλέξτε διαθέσιμο τηλέφωνο ή επικοινωνήστε μέσω των υπόλοιπων στοιχείων.
            </p>
            <button class="public-button contact-send-button" type="submit" disabled data-testid="contact-submit">ΑΠΟΣΤΟΛΗ ΜΗ ΔΙΑΘΕΣΙΜΗ</button>
          {/if}
          <p class="contact-form-note">Η φόρμα αυτή δεν αποθηκεύει ούτε στέλνει δεδομένα στον σύλλογο αυτόματα. Μην συμπεριλαμβάνετε ιατρικά ή άλλα ευαίσθητα δεδομένα παιδιών στο email.</p>
        </form>
      </div>
    </section>
  {/if}
</PublicShell>

<style>
  .contact-page-heading{padding-bottom:28px;background:var(--lf-color-bg,#0b0b0b)}
  .contact-lead{color:var(--lf-color-muted,#b7b7b7);line-height:1.6;max-width:64ch;margin:18px 0 28px}
  .contact-original-layout{display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1fr);gap:clamp(30px,4vw,70px);background:var(--lf-color-bg,#0a0a0a);align-items:start}
  .contact-info-column h2,.contact-form-column h2{font:800 clamp(23px,2.15vw,33px)/1.12 'Fira Sans Extra Condensed',sans-serif;letter-spacing:.02em;border-bottom:3px solid var(--public-gold);padding-bottom:19px;margin-bottom:15px}
  .contact-section-caption{font-size:14px;line-height:1.6;color:var(--lf-color-muted,#b4b4b4);margin-bottom:25px}
  .contact-info-cards{display:flex;flex-direction:column;gap:22px}
  .contact-info-card{display:flex;gap:20px;align-items:flex-start;border:1px solid var(--lf-color-border,#383735);background:linear-gradient(145deg,var(--lf-color-surface,#191919),var(--lf-color-bg,#101010));padding:24px;min-height:110px}
  .contact-icon{color:var(--public-gold);flex-shrink:0;margin-top:3px}
  .contact-info-card h3{font:800 23px/1.1 'Fira Sans Extra Condensed',sans-serif;margin-bottom:9px}
  .contact-info-card p{color:var(--lf-color-muted,#bcbcbc);line-height:1.6;font-size:15px;overflow-wrap:anywhere}
  .contact-info-card a{color:var(--public-gold);text-decoration:underline;text-underline-offset:4px}
  .contact-other-links{margin-top:26px;border-top:1px solid var(--lf-color-border,#51482c);padding-top:17px}
  .contact-other-links a{display:inline-flex;align-items:center;gap:10px;color:var(--public-gold);font-size:14px;font-weight:800;text-decoration:none}
  .contact-message-form{display:flex;flex-direction:column;gap:11px;background:var(--lf-color-surface,#111111);padding:clamp(20px,2.5vw,32px);border:1px solid var(--lf-color-border,#343434)}
  .contact-message-form label{font-size:13px;font-weight:800;color:var(--lf-color-muted,#dbdbdb);margin-top:6px}
  .contact-message-form input,.contact-message-form select,.contact-message-form textarea{box-sizing:border-box;display:block;width:100%;min-height:50px;background:var(--lf-color-bg,#070707);border:1px solid var(--lf-color-border,#595959);border-radius:3px;color:var(--lf-color-text,#ffffff);padding:13px 15px;font-family:inherit;font-weight:400;font-size:16px;line-height:1.45}
  .contact-message-form textarea{min-height:150px;resize:vertical}
  .contact-message-form :is(input,select,textarea):focus-visible,.contact-info-card a:focus-visible,.contact-other-links a:focus-visible{outline:3px solid var(--public-gold);outline-offset:2px}
  .contact-send-button{min-height:50px;width:100%;justify-content:center;display:flex;align-items:center;gap:10px;margin-top:15px}
  .contact-send-button:disabled{opacity:.5;cursor:not-allowed}
  .contact-compose-status{padding:15px;background:var(--lf-feedback-success-surface,#171b17);border-left:3px solid var(--lf-feedback-success,#b1cd9b);color:var(--lf-color-text,#f0f0f0);line-height:1.6}
  .contact-unavailable{color:var(--lf-color-accent,#e1c390);line-height:1.6}
  .contact-form-note{font-size:12px;color:var(--lf-color-muted,#a7a7a7);line-height:1.6;margin-top:12px}
  @media(max-width:950px){.contact-original-layout{grid-template-columns:1fr}.contact-page-heading{padding-bottom:28px}}
  @media(max-width:500px){.contact-original-layout{padding:30px 16px}.contact-info-card{padding:18px}.contact-message-form{padding:17px}.contact-info-column h2,.contact-form-column h2{font-size:30px}}
</style>
