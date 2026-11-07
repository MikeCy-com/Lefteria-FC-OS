<script lang="ts">
  import PublicJourney from '../../lib/public/PublicJourney.svelte';
  import type { PublicPageData } from '../../lib/public/types';
  let {data}:{data:PublicPageData & {partnerReferral?:string|null}}=$props();
</script>
<svelte:head>
  <title>ΔΟΚΙΜΑΣΤΙΚΑ | ΛΕΥΤΕΡΙΑ FC</title>
  <meta name="description" content="Γνώρισε από κοντά τη Λευτεριά FC Academy μέσα από προπονήσεις και μάθε για τα ηλικιακά τμήματα, τους προπονητές και την καθημερινότητά μας." />
</svelte:head>
<PublicJourney {data} eyebrow="ΑΚΑΔΗΜΙΑ ΛΕΥΤΕΡΙΑ FC" title="ΔΟΚΙΜΑΣΤΙΚΑ" description="Γνώρισε από κοντά τη Λευτεριά FC Academy μέσα από προπονήσεις και μάθε για τα ηλικιακά τμήματα, τους προπονητές και την καθημερινότητά μας."
  status="Η ηλεκτρονική φόρμα εκδήλωσης ενδιαφέροντος μεταφέρεται στη νέα εφαρμογή. Για πραγματική επικοινωνία χρησιμοποίησε τα δημοσιευμένα στοιχεία της ακαδημίας· δεν αποθηκεύουμε αιτήσεις από αυτή τη σελίδα." primaryHref="/academy/groups" primaryLabel="ΔΕΣ ΤΑ ΤΜΗΜΑΤΑ"
  secondaryHref="/contact?organization_id=lefteria_fc_academy" secondaryLabel="ΕΠΙΚΟΙΝΩΝΙΑ ΜΕ ΑΚΑΔΗΜΙΑ"
  organization="lefteria_fc_academy" partnerReferral={data.partnerReferral ?? null} />
