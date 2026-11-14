<script lang="ts">
  import PublicShell from '../../lib/public/PublicShell.svelte';
  import Icon from '../../lib/public/PublicIcon.svelte';
  import type { PublicPageData } from '../../lib/public/types';
  let { data }: { data: PublicPageData } = $props();
</script>
<svelte:head><title>Σύνδεση | ΛΕΥΤΕΡΙΑ FC</title><meta name="robots" content="noindex" /></svelte:head>
<!-- Original CheckoutPage sends signed-out visitors to customer sign-in; customer accounts and card payment arrive in plan 3.5. -->
<PublicShell site={data.site} branding={data.branding} navigationGroups={data.navigationGroups} organization="lefteria_fc">
  <section class="checkout-access" data-testid="checkout-page">
    <div class="checkout-access-card">
      <a href="/" class="checkout-back"><Icon name="back" size={14} />Αρχική</a>
      <h1>ΣΥΝΔΕΣΗ</h1>
      <p class="checkout-subtitle">Συνδεθείτε στον λογαριασμό σας</p>
      <div class="checkout-unavailable" role="status" data-testid="checkout-unavailable">
        <strong>Η ολοκλήρωση παραγγελίας δεν είναι ακόμη διαθέσιμη</strong>
        <p>Οι λογαριασμοί πελατών και η πληρωμή με κάρτα δεν είναι ενεργοί σε αυτή την έκδοση. Δεν έχει γίνει καμία χρέωση.</p>
      </div>
      <a href="/shop?tab=merchandise" class="public-button checkout-shop" data-testid="checkout-back-shop">Επιστροφή στο κατάστημα</a>
    </div>
  </section>
</PublicShell>
