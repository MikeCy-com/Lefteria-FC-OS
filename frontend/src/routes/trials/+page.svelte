<script lang="ts">
  import PublicJourney from '../../lib/public/PublicJourney.svelte';
  import type { PublicPageData } from '../../lib/public/types';
  let {data}:{data:PublicPageData}=$props();
</script>
<svelte:head>
  <title>ΔΟΚΙΜΑΣΤΙΚΑ | ΛΕΥΤΕΡΙΑ FC</title>
  <meta name="description" content="Ενδιαφέρεσαι να γνωρίσεις την πρώτη ομάδα της Λευτεριάς FC; Βρες την ομάδα, τα τελευταία νέα και τις επίσημες πληροφορίες." />
</svelte:head>
<PublicJourney {data} eyebrow="ΠΡΩΤΗ ΟΜΑΔΑ" title="ΔΟΚΙΜΑΣΤΙΚΑ" description="Ενδιαφέρεσαι να γνωρίσεις την πρώτη ομάδα της Λευτεριάς FC; Βρες την ομάδα, τα τελευταία νέα και τις επίσημες πληροφορίες."
  status="Η αποστολή αίτησης δοκιμαστικών και η διαθεσιμότητα θέσεων δεν έχουν ενεργοποιηθεί στο OS. Δεν εμφανίζουμε μη επαληθευμένη διαθεσιμότητα και δεν δεχόμαστε στοιχεία αθλητών εδώ." primaryHref="/team" primaryLabel="Η ΠΡΩΤΗ ΟΜΑΔΑ"
  secondaryHref="/contact" secondaryLabel="ΕΠΙΚΟΙΝΩΝΙΑ"
  organization="lefteria_fc" />
