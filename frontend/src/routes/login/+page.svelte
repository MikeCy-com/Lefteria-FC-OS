<script lang="ts">
  import PublicShell from '../../lib/public/PublicShell.svelte';
  import type { PublicPageData } from '../../lib/public/types';
  let { data }: { data: PublicPageData } = $props();
</script>
<svelte:head><title>Σύνδεση | ΛΕΥΤΕΡΙΑ FC</title><meta name="robots" content="noindex" /></svelte:head>
<PublicShell site={data.site} branding={data.branding} navigationGroups={data.navigationGroups} organization="lefteria_fc">
  <section class="access-heading"><p class="public-eyebrow">Η ΟΙΚΟΓΕΝΕΙΑ ΤΗΣ ΛΕΥΤΕΡΙΑΣ</p><h1>Ο ΛΟΓΑΡΙΑΣΜΟΣ ΣΟΥ.<br /><span>Η ΟΜΑΔΑ ΣΟΥ.</span></h1><p>Επίλεξε την υπηρεσία στην οποία θέλεις να συνδεθείς.</p></section>
  <nav class="access-options" aria-label="Υπηρεσίες σύνδεσης">
    <a href="/app/login"><span class="access-number" aria-hidden="true">01</span><div><p class="public-eyebrow">ΕΠΙΣΗΜΗ ΕΦΑΡΜΟΓΗ</p><h2>ΠΑΙΚΤΕΣ, ΓΟΝΕΙΣ<br />& ΦΙΛΟΙ ΤΗΣ ΟΜΑΔΑΣ</h2><p>Σύνδεση στην εφαρμογή με τον αριθμό του κινητού σου.</p></div><span class="access-arrow" aria-hidden="true">↗</span></a>
    <a href="/admin/login"><span class="access-number" aria-hidden="true">02</span><div><p class="public-eyebrow">ΔΙΑΧΕΙΡΙΣΗ ΣΥΛΛΟΓΟΥ</p><h2>ΕΙΣΟΔΟΣ<br />ΔΙΑΧΕΙΡΙΣΗΣ</h2><p>Σύνδεση εξουσιοδοτημένων λογαριασμών διαχείρισης.</p></div><span class="access-arrow" aria-hidden="true">↗</span></a>
  </nav>
</PublicShell>
