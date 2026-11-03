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
<style>
  .access-heading{padding:clamp(40px,7vw,92px) clamp(24px,5vw,72px) 30px;max-width:1100px}.access-heading h1{font-size:clamp(48px,6vw,86px);margin:20px 0}.access-heading h1 span{color:#f5a623}.access-heading>p:last-child{font-size:18px;color:#bbb;line-height:1.7}.access-options{display:grid;grid-template-columns:1fr 1fr;gap:1px;background:#303030;border:1px solid #303030;margin:0 clamp(24px,5vw,72px) 80px}.access-options>a{display:flex;gap:24px;padding:clamp(24px,4vw,50px);color:inherit;text-decoration:none;background:#111;position:relative;min-height:250px}.access-options>a:hover{background:#1c1911}.access-number{font-size:13px;color:#837d6e;padding-top:4px}.access-options h2{font-size:clamp(28px,3vw,46px);margin:17px 0 25px}.access-options p:not(.public-eyebrow){line-height:1.8;max-width:320px;color:#bbb}.access-arrow{margin-left:auto;font-size:28px;color:#f5a623}@media(max-width:750px){.access-options{grid-template-columns:1fr}.access-options>a{min-height:200px}}
</style>
