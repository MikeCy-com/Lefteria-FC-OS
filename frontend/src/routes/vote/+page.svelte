<script lang="ts">
  import PublicJourney from '../../lib/public/PublicJourney.svelte';
  import type { PublicPageData } from '../../lib/public/types';
  let {data}:{data:PublicPageData}=$props();
</script>
<svelte:head>
  <title>ΠΑΙΚΤΗΣ ΤΟΥ ΜΗΝΑ | ΛΕΥΤΕΡΙΑ FC</title>
  <meta name="description" content="Γνώρισε τους ποδοσφαιριστές μας και παρακολούθησε τα αποτελέσματα της ομάδας." />
</svelte:head>
<PublicJourney {data} eyebrow="ΛΕΥΤΕΡΙΑ FC · ΑΓΩΝΕΣ" title="ΠΑΙΚΤΗΣ ΤΟΥ ΜΗΝΑ" description="Γνώρισε τους ποδοσφαιριστές μας και παρακολούθησε τα αποτελέσματα της ομάδας."
  status="Η ψηφοφορία και τα αποτελέσματά της δεν έχουν ακόμα μεταφερθεί στο OS. Καμία ψήφος δεν υποβάλλεται από αυτή τη σελίδα και δεν εμφανίζονται πλασματικά αποτελέσματα." primaryHref="/team?tab=roster" primaryLabel="ΓΝΩΡΙΣΕ ΤΟ ΡΟΣΤΕΡ"
  secondaryHref="/team?tab=results" secondaryLabel="ΑΠΟΤΕΛΕΣΜΑΤΑ"
  organization="lefteria_fc" />
