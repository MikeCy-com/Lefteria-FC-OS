<script lang="ts">
  import PublicShell from '../../lib/public/PublicShell.svelte';
  import type { PublicPageData } from '../../lib/public/types';
  import type { components } from '../../lib/api/schema';
  type FirstTeamTrialSettings = components['schemas']['FirstTeamTrialsSettings'];
  let { data }: { data: PublicPageData & { trialSettings: FirstTeamTrialSettings } } = $props();
</script>

<svelte:head>
  <title>ΔΟΚΙΜΑΣΤΙΚΑ ΠΡΩΤΗΣ ΟΜΑΔΑΣ | ΛΕΥΤΕΡΙΑ FC</title>
  <meta name="description" content="Πληροφορίες για τις αιτήσεις δοκιμαστικών της πρώτης ομάδας Λευτεριά FC. Η διαθεσιμότητα και η αποστολή αίτησης δεν έχουν ακόμη ενεργοποιηθεί στο OS." />
</svelte:head>

<PublicShell site={data.site} branding={data.branding} organization="lefteria_fc" navigationGroups={data.navigationGroups}>
<div class="trials-page" data-testid="trials-page">
  <div class="trials-shell">
    <a class="back-link" href="/" data-testid="trials-back-link">
      <span aria-hidden="true">←</span> Πίσω στην αρχική
    </a>

    <header class="trials-header">
      <div class="eyebrow">
        <svg aria-hidden="true" viewBox="0 0 24 24" width="28" height="28" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
          <path d="M8 21h8m-4-4v4M7 4h10v7a5 5 0 0 1-10 0V4Zm10 1h4v3a4 4 0 0 1-4 4M7 5H3v3a4 4 0 0 0 4 4"/>
        </svg>
        <span class="team-badge">ΠΡΩΤΗ ΟΜΑΔΑ</span>
      </div>
      <h1 data-testid="trials-headline">{data.trialSettings.headline.toLocaleUpperCase("el-CY") || "ΕΤΟΙΜΟΣ ΝΑ ΞΕΚΙΝΗΣΕΙΣ ΤΟ ΤΑΞΙΔΙ ΣΟΥ;"}</h1>
      <p class="subtitle">{data.trialSettings.subtitle || "Γίνε μέλος της πρώτης ομάδας LEFTERIA FC και κάνε το πρώτο βήμα για να γίνεις επαγγελματίας ποδοσφαιριστής."}</p>
    </header>

    <section class="closed-card" aria-labelledby="trials-closed-heading" data-testid="trials-closed">
      <div class="closed-mark" aria-hidden="true">
        <svg viewBox="0 0 24 24" width="28" height="28" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round">
          <circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>
        </svg>
      </div>
      <h2 id="trials-closed-heading">ΟΙ ΑΙΤΗΣΕΙΣ ΔΕΝ ΕΙΝΑΙ ΑΚΟΜΗ ΑΝΟΙΧΤΕΣ</h2>
      <p class="source-closed">{data.trialSettings.closed_message || "Οι εγγραφές για δοκιμαστικά είναι προσωρινά κλειστές. Παρακαλούμε δοκιμάστε ξανά σύντομα."}</p>
      <p class="safety-note">Δεν έχει επιβεβαιωθεί διαθέσιμη περίοδος δοκιμαστικών στο OS. Η υποβολή αίτησης δεν είναι ενεργή, επομένως δεν ζητάμε προσωπικά στοιχεία αθλητών και δεν εμφανίζουμε ψευδή επιβεβαίωση.</p>
      <div class="actions" aria-label="Επίσημες πληροφορίες">
        <a class="action-primary" href="/team">ΓΝΩΡΙΣΕ ΤΗΝ ΠΡΩΤΗ ΟΜΑΔΑ <span aria-hidden="true">↗</span></a>
        <a class="action-secondary" href="/contact">ΕΠΙΚΟΙΝΩΝΙΑ <span aria-hidden="true">→</span></a>
      </div>
    </section>
  </div>
</div>
</PublicShell>

