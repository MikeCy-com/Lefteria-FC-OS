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

<style>
  .shop-page { background: #080808; min-height: 75vh; color: #fff; }
  .shop-container { width: min(1180px, calc(100% - 40px)); margin: 0 auto; }
  .shop-intro { padding: 54px 0 44px; background: linear-gradient(110deg, #101010 0%, #090909 75%); border-bottom: 1px solid #2b2b2b; }
  .shop-back { display: inline-block; margin-bottom: 22px; color: #aaa; font-size: 13px; text-decoration: none; }
  .shop-back:hover { color: #f5a623; }
  .shop-intro h1 { margin: 0 0 12px; font-size: clamp(42px, 5vw, 62px); line-height: 1.03; }
  .shop-intro h1 span { color: #f5a623; }
  .shop-intro p { margin: 0; color: #a1a1aa; font-size: 15px; line-height: 1.65; }
  .shop-content { padding-block: 34px 70px; }
  .shop-tabs { display: flex; flex-wrap: wrap; gap: 14px; padding-bottom: 15px; margin-bottom: 26px; border-bottom: 1px solid #2b2b2b; }
  .shop-tabs a { display: inline-flex; align-items: center; min-height: 44px; padding: 10px 18px; border: 1px solid transparent; color: #aaa; text-decoration: none; font-size: 13px; font-weight: 700; }
  .shop-tabs a:hover, .shop-tabs .active { background: #f5a62317; border-color: #f5a62332; color: #f5a623; }
  .shop-notice { padding: 18px 20px; background: #201a10; border: 1px solid #604a29; margin-bottom: 28px; }
  .shop-notice strong { display: block; font-size: 13px; color: #f5a623; margin-bottom: 8px; }
  .shop-notice p { margin: 0; color: #d0c4b3; font-size: 13px; line-height: 1.7; }
  .shop-section h2 { font-size: 32px; margin: 0 0 24px; }
  .shop-empty { background: #111; border: 1px solid #272727; padding: 28px; font-size: 14px; color: #b1b1b1; }
  .shop-meta { color: #f5a623 !important; font-size: 11px !important; font-weight: 600; letter-spacing: .06em; text-transform: uppercase; }
  .shop-ticket-list { display: grid; gap: 14px; }
  .shop-ticket { display: flex; align-items: center; justify-content: space-between; gap: 20px; padding: 24px; background: #111; border: 1px solid #292929; }
  .shop-ticket h3 { font-size: 25px; margin: 8px 0 9px; }
  .shop-ticket-main > p:last-child, .shop-product-body > p:not(.shop-meta) { color: #a5a5a5; font-size: 14px; line-height: 1.6; margin: 0; }
  .shop-ticket-side { flex: 0 0 auto; display: grid; justify-items: end; gap: 10px; }
  .shop-ticket-side strong, .shop-product-bottom strong { font-size: 28px; color: #f5a623; font-weight: 700; }
  .shop-ticket-side span, .shop-product-bottom span { color: #9ac89a; font-size: 11px; font-weight: 700; }
  .shop-ticket-side .unavailable, .shop-product-bottom .unavailable { color: #aaa; }
  .shop-products { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 20px; }
  .shop-product { min-width: 0; background: #111; border: 1px solid #292929; overflow: hidden; }
  .shop-product-image { background: #1a1a1a; aspect-ratio: 1.3; overflow: hidden; }
  .shop-product-image :global(img) { width: 100%; height: 100%; object-fit: contain; }
  .shop-product-body { padding: 22px; }
  .shop-product h3 { margin: 9px 0 12px; font-size: 24px; }
  .shop-product-body .shop-sizes { margin-top: 12px !important; color: #d0d0d0 !important; }
  .shop-product-bottom { display: flex; justify-content: space-between; flex-wrap: wrap; align-items: baseline; gap: 8px; margin-top: 20px; border-top: 1px solid #272727; padding-top: 15px; }
  @media (max-width: 1000px) { .shop-products { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
  @media (max-width: 650px) {
    .shop-container { width: min(100% - 28px, 1180px); }
    .shop-intro { padding-block: 40px 32px; }
    .shop-products { grid-template-columns: 1fr; }
    .shop-ticket { align-items: flex-start; flex-direction: column; }
    .shop-ticket-side { width: 100%; justify-items: start; }
    .shop-tabs { gap: 6px; }
    .shop-tabs a { padding-inline: 12px; }
  }
</style>
