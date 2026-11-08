<script lang="ts">
  import { getContext } from 'svelte';
  import { browser } from '$app/env';
  import { goto } from '$app/navigation';
  import { ADMIN_AUTH_CONTEXT, type AdminAuthController } from '../../../lib/admin/controller.ts';
  import { hasAdminPermission } from '../../../lib/admin/permissions.ts';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';

  type Organization = 'lefteria_fc' | 'lefteria_fc_academy';
  type Category = { id: string; name: string; active: boolean; version: number };
  type Variant = { id: string; name: string; cost_cents: number; price_cents: number };
  type Product = { id: string; category_id: string; name: string; image_url: string | null;
    active: boolean; version: number; variants: Variant[] };
  type Catalog = { organization_id: Organization; categories: Category[]; products: Product[] };
  const auth = getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);

  const assignments = $derived(
    $auth.status === 'authenticated' && $auth.session &&
    hasAdminPermission($auth.session.user.permissions, 'shop:read')
      ? $auth.session.user.organizations.filter((org): org is typeof org & {id: Organization} =>
          org.id === 'lefteria_fc' || org.id === 'lefteria_fc_academy')
      : []
  );
  let owner = $state<Organization | null>(null);
  let catalog = $state<Catalog | null>(null);
  let busy = $state(false);
  let unavailable = $state(false);
  let retry = $state(0);

  const euro = (cents: number) => new Intl.NumberFormat('el-CY',
    { style: 'currency', currency: 'EUR' }).format(cents / 100);

  function isCatalog(raw: unknown, selected: Organization): raw is Catalog {
    if (raw === null || typeof raw !== 'object' || Array.isArray(raw)) return false;
    const value = raw as Record<string, unknown>;
    if (value.organization_id !== selected || !Array.isArray(value.categories) || !Array.isArray(value.products) ||
        value.categories.length > 100 || value.products.length > 250) return false;
    const isCategory = (entry: unknown): entry is Category => {
      if (entry === null || typeof entry !== 'object' || Array.isArray(entry)) return false;
      const item = entry as Record<string, unknown>;
      return typeof item.id === 'string' && item.id.length > 0 && typeof item.name === 'string' &&
        typeof item.active === 'boolean' && Number.isSafeInteger(item.version) && (item.version as number) >= 0;
    };
    const categories = value.categories;
    if (!categories.every(isCategory)) return false;
    return value.products.every((entry: unknown) => {
      if (entry === null || typeof entry !== 'object' || Array.isArray(entry)) return false;
      const item = entry as Record<string, unknown>;
      return typeof item.id === 'string' && item.id.length > 0 && typeof item.name === 'string' &&
        typeof item.category_id === 'string' &&
        categories.some(category => category.id === item.category_id) &&
        (item.image_url === null || typeof item.image_url === 'string') &&
        typeof item.active === 'boolean' && Number.isSafeInteger(item.version) && (item.version as number) >= 0 &&
        Array.isArray(item.variants) && item.variants.length <= 100 &&
        item.variants.every((variant: unknown) => {
          if (variant === null || typeof variant !== 'object' || Array.isArray(variant)) return false;
          const row = variant as Record<string, unknown>;
          return typeof row.id === 'string' && typeof row.name === 'string' &&
            Number.isSafeInteger(row.price_cents) && (row.price_cents as number) > 0 &&
            (row.price_cents as number) <= 10000000 &&
            Number.isSafeInteger(row.cost_cents) && (row.cost_cents as number) >= 0 &&
            (row.cost_cents as number) <= 10000000;
        });
    });
  }

  $effect(() => {
    if (browser && $auth.status === 'anonymous') void goto('/admin/login', { replace: true });
  });

  $effect(() => {
    if (!assignments.some(org => org.id === owner)) owner = assignments[0]?.id ?? null;
  });

  $effect(() => {
    // Re-fetch if role/grants/session changes; an old owner must never remain
    // visible while an authorization response is pending.
    const sessionId = $auth.session?.user.id;
    const role = $auth.session?.user.role;
    const permissions = $auth.session?.user.permissions.join('|');
    const grants = assignments.map(org => org.id).join('|');
    const selected = owner;
    const generation = retry;
    void sessionId; void role; void permissions; void grants; void generation;

    catalog = null;
    busy = false;
    unavailable = false;

    if (!browser || $auth.status !== 'authenticated' || !$auth.session || !selected ||
        !assignments.some(org => org.id === selected)) return;
    const controller = new AbortController();
    busy = true;
    void (async () => {
      try {
        const query = new URLSearchParams({organization_id: selected});
        const response = await fetch('/api/admin/pos/catalog?' + query.toString(), {
          method:'GET', credentials:'same-origin', cache:'no-store', signal: controller.signal
        });
        if (controller.signal.aborted) return;
        if (response.status === 401) {
          void auth.bootstrap();
          throw new Error('Expired session');
        }
        if (response.status === 403) {
          void auth.bootstrap();
          throw new Error('Revoked POS permission');
        }
        if (!response.ok) throw new Error('POS catalog unavailable');
        const body: unknown = await response.json();
        if (!isCatalog(body, selected)) throw new Error('POS catalog response is not verified');
        if (!controller.signal.aborted) catalog = body;
      } catch {
        if (!controller.signal.aborted) unavailable = true;
      } finally {
        if (!controller.signal.aborted) busy = false;
      }
    })();
    return () => controller.abort();
  });
</script>

<svelte:head><title>Κατάλογος POS | Διαχείριση Λευτεριά FC</title></svelte:head>

{#if $auth.status === 'authenticated' && $auth.session}
  <AdminFrame user={$auth.session.user} active="pos" busy={$auth.busy} onLogout={() => void auth.logout()}>
    {#if assignments.length}
      <div class="admin-pos" data-testid="admin-pos-readonly">
        <p class="admin-eyebrow">POS & ΚΑΤΑΣΤΗΜΑ · ΙΔΙΩΤΙΚΟΣ ΚΑΤΑΛΟΓΟΣ</p>
        <h1>Προϊόντα & κατηγορίες</h1>
        <p class="admin-muted">Ασφαλής, πραγματική προβολή προϊόντων και παραλλαγών για τον επιλεγμένο οργανισμό. Οι αλλαγές καταλόγου, τα ταμεία, οι πωλήσεις και οι πληρωμές δεν είναι ακόμη διαθέσιμες.</p>
        <section class="admin-panel admin-pos-owner" aria-label="Επιλογή οργανισμού καταλόγου">
          <label for="admin-pos-organization">Οργανισμός</label>
          <select id="admin-pos-organization" value={owner ?? ''} data-testid="admin-pos-organization"
            onchange={event => {
              const selected = event.currentTarget.value;
              if (assignments.some(org => org.id === selected)) owner = selected as Organization;
            }}>
            {#each assignments as org (org.id)}
              <option value={org.id}>{org.name}</option>
            {/each}
          </select>
        </section>
        {#if busy}
          <section class="admin-panel" role="status" data-testid="admin-pos-loading">Φόρτωση επαληθευμένου καταλόγου…</section>
        {:else if unavailable || !catalog}
          <section class="admin-panel" role="alert" data-testid="admin-pos-unavailable">
            <p>Ο κατάλογος POS δεν είναι διαθέσιμος. Δεν εμφανίζουμε προσωρινά ή μη επαληθευμένα οικονομικά στοιχεία.</p>
            <button type="button" class="admin-secondary" onclick={() => retry++}>Δοκιμή ξανά</button>
          </section>
        {:else}
          <div class="admin-pos-stats" aria-label="Στοιχεία πραγματικού καταλόγου">
            <div><span>Κατηγορίες</span><strong>{catalog.categories.length}</strong></div>
            <div><span>Προϊόντα</span><strong>{catalog.products.length}</strong></div>
            <div><span>Παραλλαγές</span><strong>{catalog.products.reduce((count, p) => count + p.variants.length, 0)}</strong></div>
          </div>
          <div class="admin-pos-grid">
            <section class="admin-panel" aria-labelledby="pos-categories-heading">
              <h2 id="pos-categories-heading">Κατηγορίες</h2>
              {#if !catalog.categories.length}
                <p class="admin-muted" data-testid="admin-pos-empty-categories">Δεν υπάρχουν καταχωρισμένες κατηγορίες.</p>
              {:else}
                <ul class="admin-pos-categories">
                  {#each catalog.categories as category (category.id)}
                    <li><span>{category.name}</span><span class:inactive={!category.active} class="admin-pos-state">{category.active ? 'Ενεργή' : 'Αρχειοθετημένη'}</span></li>
                  {/each}
                </ul>
              {/if}
            </section>
            <section class="admin-panel" aria-labelledby="pos-products-heading">
              <h2 id="pos-products-heading">Προϊόντα & τιμές</h2>
              {#if !catalog.products.length}
                <p class="admin-muted" data-testid="admin-pos-empty-products">Δεν υπάρχουν καταχωρισμένα προϊόντα.</p>
              {:else}
                <ul class="admin-pos-products" data-testid="admin-pos-products">
                  {#each catalog.products as product (product.id)}
                    <li>
                      <div class="admin-pos-product-title">
                        <div><h3>{product.name}</h3><small>{catalog.categories.find(c => c.id === product.category_id)?.name ?? '—'}</small></div>
                        <span class:inactive={!product.active} class="admin-pos-state">{product.active ? 'Ενεργό' : 'Αρχειοθετημένο'}</span>
                      </div>
                      <ul class="admin-pos-variants">
                        {#each product.variants as variant (variant.id)}
                          <li><div><strong>{variant.name}</strong><small>Κόστος {euro(variant.cost_cents)}</small></div><span>{euro(variant.price_cents)}</span></li>
                        {/each}
                      </ul>
                    </li>
                  {/each}
                </ul>
              {/if}
            </section>
          </div>
        {/if}
      </div>
    {:else}
      <section class="admin-panel" data-testid="admin-pos-denied"><h1>Δεν έχετε πρόσβαση στον κατάλογο POS.</h1><a href="/admin">Επιστροφή στη διαχείριση</a></section>
    {/if}
  </AdminFrame>
{:else}
  <AdminSessionStatus unavailable={$auth.status === 'unavailable'} busy={$auth.busy} error={$auth.error}
    onRetry={() => void auth.bootstrap()} onLogout={() => void auth.logout()} />
{/if}

<style>
  .admin-pos{max-width:1200px}
  .admin-pos>h1{margin:0 0 10px}
  .admin-pos>.admin-muted{max-width:75ch;margin:0 0 20px}
  .admin-pos-owner{margin:0 0 15px}
  .admin-pos-owner label{display:block;color:#b9bcbe;font-size:12px;margin-bottom:6px}
  .admin-pos-owner select{width:min(100%,350px)}
  .admin-pos-stats{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:10px;margin:0 0 16px}
  .admin-pos-stats>div{display:flex;flex-direction:column;gap:5px;border:1px solid #303335;border-radius:12px;padding:15px;background:#131516}
  .admin-pos-stats span{font-size:12px;color:#b4babc}
  .admin-pos-stats strong{font-family:'Fira Sans Extra Condensed',sans-serif;font-size:29px;color:#ffca46}
  .admin-pos-grid{display:grid;grid-template-columns:minmax(260px,1fr) minmax(0,1.8fr);align-items:start;gap:14px}
  .admin-pos-grid>section{min-width:0;margin:0}
  .admin-pos-categories,.admin-pos-products,.admin-pos-variants{padding:0;margin:0;list-style:none}
  .admin-pos-categories>li{display:flex;justify-content:space-between;align-items:center;gap:10px;min-height:55px;border-top:1px solid #292b2c;padding:10px 0;font-size:13px}
  .admin-pos-state{display:inline-flex;flex:0 0 auto;font-size:11px;color:#ffca46;padding:5px 7px;border:1px solid #6c5122;border-radius:7px;background:#302311}
  .admin-pos-state.inactive{background:#222426;border-color:#444;color:#b1b7bb}
  .admin-pos-products>li{padding:15px 0;border-top:1px solid #292b2c}
  .admin-pos-product-title{display:flex;justify-content:space-between;align-items:start;gap:12px}
  .admin-pos-product-title h3{margin:0 0 4px;font-size:19px;overflow-wrap:anywhere}
  .admin-pos-product-title small,.admin-pos-variants small{display:block;color:#a7adb0;font-size:11px}
  .admin-pos-variants{margin:12px 0 0 0;display:grid;gap:6px}
  .admin-pos-variants li{display:flex;justify-content:space-between;gap:12px;padding:11px;border-radius:8px;background:#0c0e0e}
  .admin-pos-variants strong{font-size:13px}
  .admin-pos-variants>li>span{white-space:nowrap;color:#ffca46;font-family:'Fira Sans Extra Condensed',sans-serif;font-size:19px}
  @media(max-width:800px){.admin-pos-grid{grid-template-columns:minmax(0,1fr)}}
  @media(max-width:480px){.admin-pos-stats{gap:7px}.admin-pos-stats>div{padding:11px}.admin-pos-stats strong{font-size:25px}}
</style>
