<script lang="ts">
  import PublicShell from '../../lib/public/PublicShell.svelte';
  import PublicImage from '../../lib/public/PublicImage.svelte';
  import Unavailable from '../../lib/public/Unavailable.svelte';
  import { formatShopPrice } from '../../lib/public/shop';
  import type { PublicShopData } from '../../lib/server/public-shop';

  let { data }: { data: PublicShopData } = $props();
</script>

<svelte:head>
  <title>ΕΠΙΣΗΜΟ ΚΑΤΑΣΤΗΜΑ | ΛΕΥΤΕΡΙΑ FC</title>
  <meta name="description" content="Επίσημα προϊόντα και εισιτήρια της Λευτεριάς FC και της Ακαδημίας." />
</svelte:head>

<PublicShell site={data.site} branding={data.branding} organization="lefteria_fc" navigationGroups={data.navigationGroups}>
  <div class="shop-page" data-testid="shop-page">
    <section class="shop-intro" aria-labelledby="shop-title">
      <div class="shop-container">
        <a class="shop-back" href="/">← Αρχική</a>
        <h1 id="shop-title" data-testid="shop-title">ΕΠΙΣΗΜΟ <span>ΚΑΤΑΣΤΗΜΑ</span></h1>
        <p>Εισιτήρια αγώνων και επίσημα προϊόντα ΛΕΥΤΕΡΙΑ FC.</p>
      </div>
    </section>

    <div class="shop-container shop-content">
      <nav class="shop-tabs" aria-label="Κατηγορίες καταστήματος">
        <a href="/shop" aria-current={data.tab === 'tickets' ? 'page' : undefined}
          class:active={data.tab === 'tickets'} data-testid="tab-tickets">ΕΙΣΙΤΗΡΙΑ</a>
        <a href="/shop?tab=merchandise" aria-current={data.tab === 'merchandise' ? 'page' : undefined}
          class:active={data.tab === 'merchandise'} data-testid="tab-merchandise">ΡΟΥΧΙΣΜΟΣ & ΠΡΟΪΟΝΤΑ</a>
      </nav>

      {#if !data.catalogAvailable}
        <Unavailable />
      {:else}
        <div class="shop-notice" role="status" data-testid="shop-checkout-status">
          <strong>Under Developments</strong>
          <p>Το ηλεκτρονικό καλάθι και οι πληρωμές δεν έχουν ενεργοποιηθεί ακόμη. Οι τιμές και η διαθεσιμότητα που εμφανίζονται προέρχονται από τον δημοσιευμένο κατάλογο του συλλόγου.</p>
        </div>

        {#if data.tab === 'tickets'}
          <section class="shop-section" aria-labelledby="shop-tickets-heading" data-testid="tickets-section">
            <h2 id="shop-tickets-heading">ΕΙΣΙΤΗΡΙΑ</h2>
            {#if data.tickets.length === 0}
              <p class="shop-empty">Δεν υπάρχουν δημοσιευμένα εισιτήρια αυτή τη στιγμή.</p>
            {:else}
              <div class="shop-ticket-list">
                {#each data.tickets as ticket (ticket.id)}
                  <article class="shop-ticket" data-testid={'ticket-' + ticket.id}>
                    <div class="shop-ticket-main">
                      <p class="shop-meta">{ticket.ticket_type === 'seasonal' ? 'ΕΙΣΙΤΗΡΙΟ ΔΙΑΡΚΕΙΑΣ' : ticket.ticket_type === 'match' ? 'ΕΙΣΙΤΗΡΙΟ ΑΓΩΝΑ' : ticket.ticket_type} · {ticket.organization_id === 'lefteria_fc_academy' ? 'ΑΚΑΔΗΜΙΑ' : 'ΛΕΥΤΕΡΙΑ FC'}</p>
                      <h3>{ticket.name}</h3>
                      {#if ticket.description}<p>{ticket.description}</p>{/if}
                    </div>
                    <div class="shop-ticket-side">
                      <strong>{formatShopPrice(ticket.price)}</strong>
                      <span class:unavailable={!ticket.available}>{ticket.available ? 'ΚΑΤΑΧΩΡΙΣΜΕΝΟ' : 'ΜΗ ΔΙΑΘΕΣΙΜΟ'}</span>
                    </div>
                  </article>
                {/each}
              </div>
            {/if}
          </section>
        {:else}
          <section class="shop-section" aria-labelledby="shop-products-heading" data-testid="merchandise-section">
            <h2 id="shop-products-heading">ΕΠΙΣΗΜΑ ΠΡΟΪΟΝΤΑ</h2>
            {#if data.products.length === 0}
              <p class="shop-empty">Δεν υπάρχουν δημοσιευμένα προϊόντα αυτή τη στιγμή.</p>
            {:else}
              <div class="shop-products">
                {#each data.products as product (product.id)}
                  <article class="shop-product" data-testid={'product-' + product.id}>
                    <div class="shop-product-image"><PublicImage src={product.image_url} alt={product.name} /></div>
                    <div class="shop-product-body">
                      <p class="shop-meta">{product.category} · {product.organization_id === 'lefteria_fc_academy' ? 'ΑΚΑΔΗΜΙΑ' : 'ΛΕΥΤΕΡΙΑ FC'}</p>
                      <h3>{product.name}</h3>
                      {#if product.description}<p>{product.description}</p>{/if}
                      {#if product.sizes.length}<p class="shop-sizes">Μεγέθη: {product.sizes.join(' · ')}</p>{/if}
                      <div class="shop-product-bottom">
                        <strong>{formatShopPrice(product.price)}</strong>
                        <span class:unavailable={!product.in_stock}>{product.in_stock ? 'ΣΕ ΑΠΟΘΕΜΑ' : 'ΕΞΑΝΤΛΗΘΗΚΕ'}</span>
                      </div>
                    </div>
                  </article>
                {/each}
              </div>
            {/if}
          </section>
        {/if}
      {/if}
    </div>
  </div>
</PublicShell>

