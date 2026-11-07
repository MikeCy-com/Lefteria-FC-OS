<script lang="ts">
  import PublicJourney from '../../../lib/public/PublicJourney.svelte';
  import type { PublicPageData } from '../../../lib/public/types';
  let {data}:{data:PublicPageData}=$props();
</script>
<svelte:head>
  <title>ΕΓΓΡΑΦΗ ΑΘΛΗΤΗ | ΛΕΥΤΕΡΙΑ FC</title>
  <meta name="description" content="Ξεκίνα γνωρίζοντας τα ηλικιακά τμήματα της ακαδημίας και την προπονητική μας φιλοσοφία. Η οικογένεια είναι στο επίκεντρο της διαδρομής του αθλητή." />
</svelte:head>
<PublicJourney {data} eyebrow="ΑΚΑΔΗΜΙΑ ΛΕΥΤΕΡΙΑ FC" title="ΕΓΓΡΑΦΗ ΑΘΛΗΤΗ" description="Ξεκίνα γνωρίζοντας τα ηλικιακά τμήματα της ακαδημίας και την προπονητική μας φιλοσοφία. Η οικογένεια είναι στο επίκεντρο της διαδρομής του αθλητή."
  status="Η αυθεντική πολυσταδιακή εγγραφή, οι υπογραφές, οι συναινέσεις και οι πληρωμές δεν έχουν ακόμη μεταφερθεί με ασφάλεια. Δεν συλλέγουμε ιατρικά δεδομένα, στοιχεία ανηλίκων ή χρήματα από αυτή τη σελίδα." primaryHref="/academy/groups" primaryLabel="ΤΑ ΤΜΗΜΑΤΑ ΜΑΣ"
  secondaryHref="/contact?organization_id=lefteria_fc_academy" secondaryLabel="ΡΩΤΗΣΕ ΤΗΝ ΑΚΑΔΗΜΙΑ"
  organization="lefteria_fc_academy" />
