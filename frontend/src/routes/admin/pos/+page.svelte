<script lang="ts">
  import { getContext } from 'svelte';
  import { browser } from '$app/env';
  import { goto } from '$app/navigation';
  import { ADMIN_AUTH_CONTEXT, type AdminAuthController } from '../../../lib/admin/controller.ts';
  import { hasAdminPermission } from '../../../lib/admin/permissions.ts';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';
  import AdminPosBasketPreview from '../../../lib/admin/AdminPosBasketPreview.svelte';
  import AdminPosLocationsPanel from '../../../lib/admin/AdminPosLocationsPanel.svelte';
  import AdminPosCashierShiftsPanel from '../../../lib/admin/AdminPosCashierShiftsPanel.svelte';
  import AdminPosFinanceHandoffPanel from '../../../lib/admin/AdminPosFinanceHandoffPanel.svelte';

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
  const canEdit = $derived(
    $auth.status === 'authenticated' && $auth.session &&
    hasAdminPermission($auth.session.user.permissions, 'shop:write') &&
    !!owner && assignments.some(org => org.id === owner)
  );
  let showCategoryForm = $state(false);
  let editingId = $state<string | null>(null);
  let expectedVersion = $state<number | null>(null);
  let categoryName = $state('');
  let categoryActive = $state(true);
  let writing = $state(false);
  let writeError = $state<string | null>(null);


  type VariantInput = { id: string; name: string; cost: string; price: string };
  let showProductForm = $state(false);
  let editingProductId = $state<string | null>(null);
  let productVersion = $state<number | null>(null);
  let productCategory = $state('');
  let productName = $state('');
  let productImage = $state('');
  let productActive = $state(true);
  let productVariants = $state<VariantInput[]>([]);
  let productWriting = $state(false);
  let productError = $state<string | null>(null);

  function newVariant(index: number): VariantInput {
    return { id: 'variant_' + index, name: '', cost: '0.00', price: '1.00' };
  }
  function startNewProduct() {
    if (!canEdit || !catalog || !catalog.categories.some(c => c.active)) return;
    editingProductId = null;
    productVersion = null;
    productCategory = catalog.categories.find(c => c.active)?.id ?? '';
    productName = '';
    productImage = '';
    productActive = true;
    productVariants = [newVariant(1)];
    productError = null;
    showProductForm = true;
  }
  function editProduct(product: Product) {
    if (!canEdit || !catalog) return;
    editingProductId = product.id;
    productVersion = product.version;
    productCategory = product.category_id;
    productName = product.name;
    productImage = product.image_url ?? '';
    productActive = product.active;
    productVariants = product.variants.map(v => ({
      id: v.id, name: v.name,
      cost: (v.cost_cents / 100).toFixed(2),
      price: (v.price_cents / 100).toFixed(2)
    }));
    productError = null;
    showProductForm = true;
  }
  function centsFromEuros(value: string): number | null {
    if (!/^\d{1,6}([.,]\d{1,2})?$/.test(value)) return null;
    const [whole, decimal = ''] = value.replace(',', '.').split('.');
    const cents = Number(whole) * 100 + Number(decimal.padEnd(2, '0'));
    return Number.isSafeInteger(cents) && cents <= 10000000 ? cents : null;
  }
  function verifiedProduct(raw: unknown, id: string | null, name: string,
    expectedVariants: { id: string; name: string; cost_cents: number; price_cents: number }[]): boolean {
    if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return false;
    const item = raw as Record<string, unknown>;
    if (typeof item.id !== 'string' || !(id === null ? /^prod_[0-9a-f]{32}$/.test(item.id) : item.id === id) ||
        item.category_id !== productCategory || item.name !== name ||
        item.active !== productActive || item.image_url !== (productImage || null) ||
        !Number.isSafeInteger(item.version) || (item.version as number) < 0 ||
        !Array.isArray(item.variants) || item.variants.length !== expectedVariants.length) return false;
    const confirmed = item.variants as unknown[];
    return expectedVariants.every(wanted => confirmed.some((value: unknown) => {
      if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
      const actual = value as Record<string, unknown>;
      return actual.id === wanted.id && actual.name === wanted.name &&
        actual.cost_cents === wanted.cost_cents && actual.price_cents === wanted.price_cents;
    }));
  }
  async function saveProduct() {
    if (!browser || !canEdit || !owner || !showProductForm || productWriting ||
        !catalog || busy || writing) return;
    const selectedOwner = owner;
    const userId = $auth.session?.user.id;
    const currentId = editingProductId;
    const name = productName;
    const image = productImage;
    const selectedCategory = productCategory;
    if (!name || name.length > 100 || name !== name.trim() ||
        !catalog.categories.some(c => c.id === selectedCategory && c.active) ||
        !Array.isArray(productVariants) || productVariants.length < 1 ||
        productVariants.length > 16 || (image && (!/^https:\/\/[^\s]+$/.test(image) ||
        image.length > 2048 || /[\x00-\x1f\x7f]/.test(image))) ||
        (currentId !== null && (productVersion === null ||
        !Number.isSafeInteger(productVersion) || productVersion < 0))) {
      productError = 'Ελέγξτε την κατηγορία, το όνομα, τις παραλλαγές και την έκδοση του προϊόντος.';
      return;
    }
    const variants: { id: string; name: string; cost_cents: number; price_cents: number }[] = [];
    const ids = new Set<string>();
    for (const variant of productVariants) {
      const cost = centsFromEuros(variant.cost);
      const price = centsFromEuros(variant.price);
      if (!/^[A-Za-z0-9_-]{1,60}$/.test(variant.id) || !variant.name ||
          variant.name !== variant.name.trim() || variant.name.length > 80 ||
          ids.has(variant.id) || cost === null || price === null || price <= 0) {
        productError = 'Κάθε παραλλαγή χρειάζεται μοναδικό κωδικό, όνομα και έγκυρα ποσά σε €.';
        return;
      }
      ids.add(variant.id);
      variants.push({id:variant.id,name:variant.name,cost_cents:cost,price_cents:price});
    }
    productWriting = true;
    productError = null;
    try {
      const query = new URLSearchParams({organization_id:selectedOwner});
      const target = '/api/admin/pos/products' +
        (currentId === null ? '' : '/' + encodeURIComponent(currentId)) + '?' + query.toString();
      const payload = {
        category_id:selectedCategory,name,image_url:image || null,active:productActive,variants,
        ...(currentId === null ? {} : {expected_version:productVersion})
      };
      const response = await fetch(target,{
        method:currentId===null ? 'POST' : 'PUT',
        credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify(payload)
      });
      if (owner !== selectedOwner || $auth.session?.user.id !== userId || !canEdit) return;
      if (response.status===401 || response.status===403) {
        void auth.bootstrap();
        showProductForm=false;
        throw new Error('Η πρόσβαση άλλαξε. Επαληθεύστε ξανά τη σύνδεσή σας.');
      }
      if (response.status===409) {
        showProductForm=false;
        retry++;
        throw new Error('Το προϊόν άλλαξε. Ο κατάλογος ανανεώνεται πριν από νέα αποθήκευση.');
      }
      if (!response.ok) throw new Error('Το προϊόν δεν αποθηκεύτηκε. Ελέγξτε τα στοιχεία και τα ποσά.');
      const result: unknown = await response.json();
      if (!verifiedProduct(result,currentId,name,variants))
        throw new Error('Η επιβεβαίωση προϊόντος ή παραλλαγών δεν ήταν έγκυρη.');
      showProductForm=false;
      retry++;
    } catch(error) {
      if (owner===selectedOwner && $auth.session?.user.id===userId)
        productError=error instanceof Error ? error.message : 'Η αλλαγή δεν ολοκληρώθηκε.';
    } finally { productWriting=false; }
  }

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

  function createCategory() {
    showCategoryForm = true;
    editingId = null;
    expectedVersion = null;
    categoryName = '';
    categoryActive = true;
    writeError = null;
  }

  function editCategory(category: Category) {
    showCategoryForm = true;
    editingId = category.id;
    expectedVersion = category.version;
    categoryName = category.name;
    categoryActive = category.active;
    writeError = null;
  }

  async function saveCategory() {
    if (!browser || !canEdit || !owner || !showCategoryForm || writing) return;
    const trimmed = categoryName.trim();
    if (!trimmed || trimmed.length > 100 || trimmed !== categoryName) {
      writeError = 'Εισαγάγετε όνομα χωρίς περιττά κενά (έως 100 χαρακτήρες).';
      return;
    }
    const selected = owner;
    const currentUser = $auth.session?.user.id;
    const modifying = editingId !== null;
    if (modifying && (!Number.isSafeInteger(expectedVersion) || expectedVersion === null || expectedVersion < 0)) {
      writeError = 'Η έκδοση της κατηγορίας δεν είναι έγκυρη. Ανανεώστε τη σελίδα.';
      return;
    }
    writing = true;
    writeError = null;
    try {
      const query = new URLSearchParams({ organization_id: selected });
      const target = modifying
        ? '/api/admin/pos/categories/' + encodeURIComponent(editingId!) + '?' + query.toString()
        : '/api/admin/pos/categories?' + query.toString();
      const payload = modifying
        ? {name:trimmed,active:categoryActive,expected_version:expectedVersion}
        : {name:trimmed,active:categoryActive};
      const response = await fetch(target, {
        method: modifying ? 'PUT' : 'POST',
        credentials: 'same-origin', cache:'no-store',
        headers: {'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body: JSON.stringify(payload)
      });
      if (owner !== selected || $auth.session?.user.id !== currentUser || !canEdit) return;
      if (response.status === 401 || response.status === 403) {
        void auth.bootstrap();
        showCategoryForm = false;
        throw new Error('Η πρόσβαση άλλαξε. Επαληθεύστε ξανά τη σύνδεσή σας.');
      }
      if (response.status === 409) {
        showCategoryForm = false;
        retry++;
        throw new Error('Η κατηγορία άλλαξε. Ο κατάλογος ανανεώνεται.');
      }
      if (!response.ok) throw new Error('Η αλλαγή δεν αποθηκεύτηκε. Ελέγξτε τα στοιχεία σας.');
      const value: unknown = await response.json();
      if (value === null || typeof value !== 'object' || Array.isArray(value) ||
          !('id' in value) || typeof value.id !== 'string' ||
          !('name' in value) || value.name !== trimmed ||
          !('active' in value) || value.active !== categoryActive ||
          !('version' in value) || !Number.isSafeInteger(value.version))
        throw new Error('Η επιβεβαίωση αποθήκευσης δεν είναι έγκυρη.');
      showCategoryForm = false;
      retry++;
    } catch (error) {
      if (owner === selected && $auth.session?.user.id === currentUser)
        writeError = error instanceof Error ? error.message : 'Η αλλαγή δεν ολοκληρώθηκε.';
    } finally { writing = false; }
  }

  $effect(() => {
    const selected = owner;
    const permission = canEdit;
    void selected; void permission;
    showCategoryForm = false;
    editingId = null;
    expectedVersion = null;
    writeError = null;
    showProductForm = false;
    editingProductId = null;
    productVersion = null;
    productError = null;
  });

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
      <div class="admin-pos" data-testid="admin-pos-catalog">
        <p class="admin-eyebrow">POS & ΚΑΤΑΣΤΗΜΑ · ΙΔΙΩΤΙΚΟΣ ΚΑΤΑΛΟΓΟΣ</p>
        <h1>Προϊόντα & κατηγορίες</h1>
        <p class="admin-muted">Ασφαλής, πραγματική προβολή προϊόντων και παραλλαγών για τον επιλεγμένο οργανισμό. Η διαχείριση κατηγοριών και προϊόντων γίνεται με έλεγχο έκδοσης και ασφαλή καταγραφή αλλαγών. Τα ταμεία, οι πωλήσεις και οι πληρωμές παραμένουν ανενεργές.</p>
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
          {#if canEdit && $auth.session && ['manager','super_admin'].includes($auth.session.user.role) && owner}
            <AdminPosCashierShiftsPanel owner={owner as Organization} actorId={$auth.session.user.id}
              onExpired={()=>void auth.bootstrap()} onRevoked={()=>void auth.bootstrap()}/>
          {/if}
          {#if canEdit && $auth.session && ['manager','super_admin'].includes($auth.session.user.role) &&
            hasAdminPermission($auth.session.user.permissions,'finance:read') && owner}
            <AdminPosFinanceHandoffPanel owner={owner as Organization}
              actorId={$auth.session.user.id}
              onExpired={()=>void auth.bootstrap()} onRevoked={()=>void auth.bootstrap()}/>
          {/if}
          {#if canEdit}
            <section class="admin-panel admin-pos-editor" aria-labelledby="pos-editor-heading" data-testid="admin-pos-category-editor">
              <div class="admin-pos-editor-heading">
                <div>
                  <p class="admin-eyebrow">ΔΙΑΧΕΙΡΙΣΗ ΚΑΤΗΓΟΡΙΩΝ</p>
                  <h2 id="pos-editor-heading">Κατηγορίες καταλόγου</h2>
                </div>
                {#if !showCategoryForm}
                  <button type="button" class="admin-secondary" disabled={writing} onclick={createCategory}>Προσθήκη κατηγορίας</button>
                {/if}
              </div>
              {#if showCategoryForm}
                <form class="admin-pos-category-form" data-testid="admin-pos-category-form" onsubmit={event => {event.preventDefault(); void saveCategory();}}>
                  <label for="pos-category-name">Όνομα κατηγορίας</label>
                  <input id="pos-category-name" type="text" maxlength="100" required bind:value={categoryName} disabled={writing}/>
                  <label class="admin-pos-checkbox" for="pos-category-active">
                    <input id="pos-category-active" type="checkbox" bind:checked={categoryActive} disabled={writing}/>
                    Ενεργή
                  </label>
                  <div class="admin-pos-form-actions">
                    <button type="submit" class="admin-secondary" disabled={writing}>{writing ? 'Αποθήκευση…' : 'Αποθήκευση'}</button>
                    <button type="button" class="admin-secondary" disabled={writing}
                      onclick={() => {showCategoryForm=false;writeError=null;}}>Ακύρωση</button>
                  </div>
                </form>
              {/if}
              {#if writeError}
                <p role="alert" class="admin-pos-write-error">{writeError}</p>
              {/if}
            </section>
          {/if}
          {#if canEdit}
            <section class="admin-panel admin-pos-editor" aria-labelledby="pos-products-editor-heading" data-testid="admin-pos-product-editor">
              <div class="admin-pos-editor-heading">
                <div>
                  <p class="admin-eyebrow">ΔΙΑΧΕΙΡΙΣΗ ΠΡΟΪΟΝΤΩΝ · ΤΙΜΕΣ ΣΕ €</p>
                  <h2 id="pos-products-editor-heading">Προϊόντα & παραλλαγές</h2>
                </div>
                {#if !showProductForm}
                  <button type="button" class="admin-secondary" disabled={productWriting || writing || !catalog.categories.some(c => c.active)}
                    onclick={startNewProduct}>Προσθήκη προϊόντος</button>
                {/if}
              </div>
              {#if showProductForm}
                <form class="admin-pos-product-form" data-testid="admin-pos-product-form"
                  onsubmit={event => {event.preventDefault(); void saveProduct();}}>
                  <div class="admin-pos-product-fields">
                    <label>Όνομα προϊόντος
                      <input type="text" maxlength="100" required bind:value={productName} disabled={productWriting}/>
                    </label>
                    <label>Κατηγορία
                      <select required bind:value={productCategory} disabled={productWriting}>
                        {#each catalog.categories.filter(c => c.active || c.id === productCategory) as category (category.id)}
                          <option value={category.id}>{category.name}</option>
                        {/each}
                      </select>
                    </label>
                    <label>HTTPS εικόνας (προαιρετικό)
                      <input type="url" maxlength="2048" placeholder="https://" bind:value={productImage} disabled={productWriting}/>
                    </label>
                    <label class="admin-pos-checkbox">
                      <input type="checkbox" bind:checked={productActive} disabled={productWriting}/>
                      Ενεργό προϊόν
                    </label>
                  </div>
                  <div class="admin-pos-variant-editor">
                    <h3>Παραλλαγές & ποσά (€)</h3>
                    <p class="admin-muted">Τιμές πώλησης και κόστη με έως δύο δεκαδικά. Η αποθήκευση αντικαθιστά ολόκληρο τον κατάλογο παραλλαγών του προϊόντος.</p>
                    {#each productVariants as variant, index (index)}
                      <div class="admin-pos-variant-row">
                        <label>Κωδικός
                          <input type="text" required pattern={'[A-Za-z0-9_-]{1,60}'} maxlength="60"
                            bind:value={variant.id} disabled={productWriting}/>
                        </label>
                        <label>Όνομα / μέγεθος
                          <input type="text" required maxlength="80" bind:value={variant.name} disabled={productWriting}/>
                        </label>
                        <label>Κόστος (€)
                          <input type="text" required inputmode="decimal" pattern={'[0-9]{1,6}([.,][0-9]{1,2})?'}
                            bind:value={variant.cost} disabled={productWriting}/>
                        </label>
                        <label>Τιμή πώλησης (€)
                          <input type="text" required inputmode="decimal" pattern={'[0-9]{1,6}([.,][0-9]{1,2})?'}
                            bind:value={variant.price} disabled={productWriting}/>
                        </label>
                        <button type="button" class="admin-secondary" disabled={productWriting || productVariants.length === 1}
                          aria-label={'Αφαίρεση παραλλαγής '+(index+1)}
                          onclick={() => {productVariants=productVariants.filter((_,i)=>i!==index);}}>Αφαίρεση</button>
                      </div>
                    {/each}
                    <button type="button" class="admin-secondary" disabled={productWriting || productVariants.length >= 16}
                      onclick={() => {
                        let next=1;
                        while(productVariants.some(item => item.id === 'variant_' + next)) next++;
                        productVariants=[...productVariants,newVariant(next)];
                      }}>Προσθήκη παραλλαγής</button>
                  </div>
                  <div class="admin-pos-form-actions">
                    <button type="submit" class="admin-secondary" disabled={productWriting || writing}>{productWriting ? 'Αποθήκευση…' : 'Αποθήκευση προϊόντος'}</button>
                    <button type="button" class="admin-secondary" disabled={productWriting}
                      onclick={() => {showProductForm=false;productError=null;}}>Ακύρωση</button>
                  </div>
                </form>
              {/if}
              {#if productError}
                <p role="alert" class="admin-pos-write-error">{productError}</p>
              {/if}
            </section>
          {/if}
          <AdminPosBasketPreview products={catalog.products} categories={catalog.categories} organization={catalog.organization_id}/>
          <div class="admin-pos-grid">
            <section class="admin-panel" aria-labelledby="pos-categories-heading">
              <h2 id="pos-categories-heading">Κατηγορίες</h2>
              {#if !catalog.categories.length}
                <p class="admin-muted" data-testid="admin-pos-empty-categories">Δεν υπάρχουν καταχωρισμένες κατηγορίες.</p>
              {:else}
                <ul class="admin-pos-categories">
                  {#each catalog.categories as category (category.id)}
                    <li>
                      <span>{category.name}</span>
                      <div class="admin-pos-category-actions">
                        <span class:inactive={!category.active} class="admin-pos-state">{category.active ? 'Ενεργή' : 'Αρχειοθετημένη'}</span>
                        {#if canEdit}
                          <button type="button" class="admin-secondary" disabled={writing}
                            onclick={() => editCategory(category)}>Επεξεργασία</button>
                        {/if}
                      </div>
                    </li>
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
                        <div class="admin-pos-category-actions">
                          <span class:inactive={!product.active} class="admin-pos-state">{product.active ? 'Ενεργό' : 'Αρχειοθετημένο'}</span>
                          {#if canEdit}
                            <button type="button" class="admin-secondary" disabled={productWriting || writing}
                              onclick={() => editProduct(product)}>Επεξεργασία</button>
                          {/if}
                        </div>
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
          {#if owner && $auth.session}
            <AdminPosLocationsPanel owner={owner as Organization}
              actorId={$auth.session.user.id}
              products={catalog.products}
              canWrite={!!canEdit&&['manager','super_admin'].includes($auth.session.user.role)}
              onExpired={()=>auth.invalidateSession()}
              onRevoked={()=>void auth.bootstrap()}/>
          {/if}
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
  .admin-pos-editor{margin:0 0 15px}
  .admin-pos-editor-heading{display:flex;justify-content:space-between;align-items:center;gap:12px;flex-wrap:wrap}
  .admin-pos-editor-heading h2{margin:0}
  .admin-pos-category-form{display:flex;flex-direction:column;gap:9px;max-width:480px;margin-top:16px}
  .admin-pos-category-form>label{color:#b9bcbe;font-size:12px}
  .admin-pos-category-form input[type=text]{width:100%}
  .admin-pos-category-form .admin-pos-checkbox{display:flex;align-items:center;gap:9px;min-height:44px}
  .admin-pos-checkbox input{width:20px;height:20px}
  .admin-pos-form-actions{display:flex;flex-wrap:wrap;gap:9px}
  .admin-pos-category-actions{display:flex;align-items:center;flex-wrap:wrap;justify-content:flex-end;gap:8px}
  .admin-pos-category-actions button{min-height:44px}
  .admin-pos-write-error{color:#ffd29b;margin-top:12px}

  .admin-pos-product-form{display:grid;gap:18px;margin-top:18px}
  .admin-pos-product-fields{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:13px;align-items:end}
  .admin-pos-product-form label{display:flex;flex-direction:column;gap:6px;color:#b9bcbe;font-size:12px;min-width:0}
  .admin-pos-product-form input:not([type=checkbox]),.admin-pos-product-form select{width:100%;min-width:0}
  .admin-pos-product-form .admin-pos-checkbox{display:flex;flex-direction:row;align-items:center;gap:9px;min-height:44px}
  .admin-pos-variant-editor{display:grid;gap:11px}
  .admin-pos-variant-editor h3{margin:0;font-size:19px}
  .admin-pos-variant-editor p{margin:0;font-size:12px}
  .admin-pos-variant-row{display:grid;grid-template-columns:1fr 1.5fr .85fr .85fr auto;gap:9px;align-items:end;padding:12px;border:1px solid #303335;border-radius:10px;background:#0c0e0e}
  .admin-pos-variant-row .admin-secondary{white-space:nowrap}
  @media(max-width:1050px){.admin-pos-variant-row{grid-template-columns:repeat(2,minmax(0,1fr))}.admin-pos-variant-row .admin-secondary{justify-self:start}}
  @media(max-width:600px){.admin-pos-product-fields,.admin-pos-variant-row{grid-template-columns:minmax(0,1fr)}}
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
