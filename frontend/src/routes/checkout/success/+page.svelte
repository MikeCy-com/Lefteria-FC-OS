<script lang="ts">
  import PublicShell from '../../../lib/public/PublicShell.svelte';
  import type { PublicPageData } from '../../../lib/public/types';
  let { data }: { data: PublicPageData & { hasSession: boolean } } = $props();
  // Original CheckoutSuccessPage error state. Card payment is OFF, so a session can never be confirmed here.
  const message = $derived(data.hasSession ? 'Η πληρωμή με κάρτα δεν είναι ακόμη διαθέσιμη. Δεν έχει γίνει καμία χρέωση.' : 'Δεν βρέθηκε ID συνεδρίας πληρωμής');
</script>
<svelte:head><title>Πληρωμή | ΛΕΥΤΕΡΙΑ FC</title><meta name="robots" content="noindex" /></svelte:head>
<PublicShell site={data.site} branding={data.branding} navigationGroups={data.navigationGroups} organization="lefteria_fc">
  <section class="checkout-result" data-testid="stripe-success-page">
    <div class="checkout-result-card" role="status">
      <svg class="checkout-result-icon" width="56" height="56" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="10" /><path d="m15 9-6 6" /><path d="m9 9 6 6" /></svg>
      <h1>ΣΦΑΛΜΑ ΠΛΗΡΩΜΗΣ</h1>
      <p data-testid="checkout-result-message">{message}</p>
      <div class="checkout-result-actions">
        <a href="/shop?tab=merchandise" class="checkout-result-secondary" data-testid="success-back-cart">Επιστροφή στο καλάθι</a>
        <a href="/login" class="checkout-result-primary" data-testid="success-view-orders">Οι παραγγελίες μου</a>
      </div>
    </div>
  </section>
</PublicShell>
