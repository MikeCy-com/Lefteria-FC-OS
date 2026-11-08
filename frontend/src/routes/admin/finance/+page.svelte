<script lang="ts">
  import { getContext } from 'svelte';
  import { browser } from '$app/env';
  import { goto } from '$app/navigation';
  import { ADMIN_AUTH_CONTEXT, type AdminAuthController } from '../../../lib/admin/controller.ts';
  import { hasAdminPermission } from '../../../lib/admin/permissions.ts';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';

  type Owner = 'lefteria_fc' | 'lefteria_fc_academy';
  type Status = { configured: boolean; environment: 'sandbox'; payment_links: boolean;
    subscriptions: boolean; employee_card_payouts: boolean; refunds: boolean; live_transactions: boolean };
  type Payment = { id: string; purpose: string; amount_cents: number;
    currency: 'EUR'; status: string; checkout_url: string | null;
    payer_email: string; created_at: string };
  const auth = getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  const organizations = $derived(
    $auth.status === 'authenticated' && $auth.session &&
    hasAdminPermission($auth.session.user.permissions, 'finance:read')
      ? $auth.session.user.organizations.filter((org): org is typeof org & { id: Owner } =>
          org.id === 'lefteria_fc' || org.id === 'lefteria_fc_academy') : []
  );
  let owner = $state<Owner | null>(null);
  let status = $state<Status | null>(null);
  let payments = $state<Payment[]>([]);
  let loading = $state(false);
  let failed = $state(false);
  let reload = $state(0);
  let purpose = $state('');
  let amount = $state('');
  let email = $state('');
  let first = $state('');
  let last = $state('');
  let writing = $state(false);
  let writeError = $state('');
  let lastLink = $state<string | null>(null);
  let requestId = $state<string | null>(null);
  const canWrite = $derived($auth.status === 'authenticated' && $auth.session &&
    hasAdminPermission($auth.session.user.permissions, 'finance:write') &&
    !!owner && organizations.some(org => org.id === owner));
  const money = (cents: number) => new Intl.NumberFormat('el-CY',
    { style: 'currency', currency: 'EUR' }).format(cents / 100);

  function trustedLink(value: unknown): value is string {
    if (typeof value !== 'string' || value.length > 2048) return false;
    try {
      const uri = new URL(value);
      return uri.protocol === 'https:' && !uri.username && !uri.password &&
        (uri.hostname === 'sandbox.payabl.com' || uri.hostname === 'pay4.sandbox.payabl.com');
    } catch { return false; }
  }
  function verifiedPayment(value: unknown): value is Payment {
    if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
    const row = value as Record<string, unknown>;
    return typeof row.id === 'string' && /^[0-9a-f-]{36}$/i.test(row.id) &&
      typeof row.purpose === 'string' && typeof row.payer_email === 'string' &&
      typeof row.amount_cents === 'number' && Number.isSafeInteger(row.amount_cents) &&
      row.amount_cents > 0 && row.amount_cents <= 100000000 &&
      row.currency === 'EUR' && typeof row.status === 'string' &&
      typeof row.created_at === 'string' &&
      (row.checkout_url === null || trustedLink(row.checkout_url));
  }

  $effect(() => {
    if (browser && $auth.status === 'anonymous') void goto('/admin/login', { replace: true });
  });
  $effect(() => {
    if (!organizations.some(org => org.id === owner)) owner = organizations[0]?.id ?? null;
  });
  $effect(() => {
    const selected = owner;
    const account = $auth.session?.user.id;
    const grants = organizations.map(org => org.id).join('|');
    const permissions = $auth.session?.user.permissions.join('|');
    const refresh = reload;
    void account; void grants; void permissions; void refresh;
    status = null; payments = []; failed = false; loading = false;
    lastLink = null; writeError = ''; requestId = null;
    if (!browser || !selected || $auth.status !== 'authenticated') return;
    const controller = new AbortController();
    loading = true;
    void (async () => {
      try {
        const query = '?organization_id=' + encodeURIComponent(selected);
        const [s,p] = await Promise.all([
          fetch('/api/admin/finance/payabl/status' + query,
            { credentials:'same-origin',cache:'no-store',signal:controller.signal }),
          fetch('/api/admin/finance/payabl/payment-requests' + query,
            { credentials:'same-origin',cache:'no-store',signal:controller.signal })
        ]);
        if (controller.signal.aborted) return;
        if (s.status === 401 || p.status === 401) { auth.invalidateSession(); return; }
        if (s.status === 403 || p.status === 403) { void auth.bootstrap(); throw Error('Access changed'); }
        if (!s.ok || !p.ok) throw Error('Finance service unavailable');
        const info: unknown = await s.json();
        const ledger: unknown = await p.json();
        if (!info || typeof info !== 'object' || (info as Status).environment !== 'sandbox' ||
            typeof (info as Status).configured !== 'boolean' ||
            (info as Status).live_transactions !== false ||
            !ledger || typeof ledger !== 'object' ||
            (ledger as {organization_id?:unknown}).organization_id !== selected ||
            !Array.isArray((ledger as {payments?:unknown}).payments) ||
            !(ledger as {payments:unknown[]}).payments.every(verifiedPayment))
          throw Error('Unverified Payabl response');
        if (!controller.signal.aborted && owner === selected) {
          status = info as Status;
          payments = (ledger as {payments:Payment[]}).payments;
        }
      } catch {
        if (!controller.signal.aborted) failed = true;
      } finally { if (!controller.signal.aborted) loading = false; }
    })();
    return () => controller.abort();
  });

  function parseCents(value: string): number | null {
    if (!/^\d{1,6}([.,]\d{1,2})?$/.test(value)) return null;
    const [whole, fraction=''] = value.replace(',', '.').split('.');
    const cents = Number(whole) * 100 + Number(fraction.padEnd(2,'0'));
    return Number.isSafeInteger(cents) && cents >= 1 && cents <= 100000000 ? cents : null;
  }

  async function createLink(event: SubmitEvent) {
    event.preventDefault();
    if (!browser || !canWrite || !owner || !status?.configured || writing) return;
    const selected = owner;
    const userId = $auth.session?.user.id;
    const cents = parseCents(amount);
    if (cents === null || purpose.trim() !== purpose || purpose.length < 3 ||
        purpose.length > 140 || !/^[\x20-\x7e]+$/.test(purpose) ||
        email.length > 50 || !/^[\x20-\x7e]+$/.test(email) || !email.includes('@') ||
        first.length > 50 || last.length > 50 ||
        !/^[\x20-\x7e]+$/.test(first) || !/^[\x20-\x7e]+$/.test(last)) {
      writeError = 'Ελέγξτε το ποσό και χρησιμοποιήστε δοκιμαστικά στοιχεία με λατινικούς χαρακτήρες.';
      return;
    }
    requestId ??= crypto.randomUUID();
    const originalRequestId = requestId;
    writing = true; writeError = ''; lastLink = null;
    try {
      const response = await fetch('/api/admin/finance/payabl/payment-requests?organization_id=' +
        encodeURIComponent(selected), {
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({client_request_id:originalRequestId,amount_cents:cents,
          currency:'EUR',purpose,payer_email:email,
          payer_first_name:first,payer_last_name:last})
      });
      if (owner !== selected || $auth.session?.user.id !== userId) return;
      if (response.status === 401) { auth.invalidateSession(); return; }
      if (response.status === 403) { void auth.bootstrap(); return; }
      if (!response.ok) throw Error('Το αίτημα δεν ολοκληρώθηκε. Μην επαναλάβετε με άλλο αναγνωριστικό πριν από έλεγχο.');
      const output: unknown = await response.json();
      if (!output || typeof output !== 'object') throw Error('Δεν επαληθεύτηκε η απάντηση.');
      const result = output as Record<string,unknown>;
      if (result.status !== 'ready' || !trustedLink(result.checkout_url))
        throw Error('Δεν υπάρχει διαθέσιμος επαληθευμένος σύνδεσμος. Ελέγξτε την κατάσταση.');
      lastLink = result.checkout_url;
      requestId = null;
      // Do not call this a settled payment. Only the provider callback can do that.
      reload += 1;
    } catch(error) {
      if (owner === selected) writeError =
        error instanceof Error ? error.message : 'Ο πάροχος δεν απάντησε.';
    } finally { writing = false; }
  }
</script>

<svelte:head><title>payabl. · Οικονομικά | Λευτεριά FC</title></svelte:head>

{#if $auth.status === 'authenticated' && $auth.session}
  <AdminFrame user={$auth.session.user} active="finance" busy={$auth.busy} onLogout={() => void auth.logout()}>
    <div class="admin-workspace-intro" data-testid="payabl-admin-sandbox">
      <p class="admin-eyebrow">ΟΙΚΟΝΟΜΙΚΑ · PAYABL. · SANDBOX</p>
      <h1>Σύνδεσμοι πληρωμής</h1>
      <p class="admin-muted">Δοκιμαστικό περιβάλλον: καμία πραγματική χρέωση. Η ένδειξη «πληρώθηκε» εμφανίζεται μόνο έπειτα από επαληθευμένη ειδοποίηση του παρόχου. Μην εισάγετε πραγματικά στοιχεία πελατών στο δημόσιο sandbox.</p>
    </div>
    {#if organizations.length}
      <section class="admin-panel">
        <label for="payabl-owner">Οργανισμός</label>
        <select id="payabl-owner" value={owner ?? ''} onchange={event => {
          const chosen = event.currentTarget.value;
          if (organizations.some(org => org.id === chosen)) owner = chosen as Owner;
        }}>
          {#each organizations as org (org.id)}<option value={org.id}>{org.name}</option>{/each}
        </select>
      </section>
      {#if loading}
        <section class="admin-panel" role="status">Ανάκτηση ασφαλών στοιχείων πληρωμών…</section>
      {:else if failed}
        <section class="admin-panel" role="alert">
          Τα στοιχεία πληρωμών δεν είναι διαθέσιμα. Δεν εμφανίζονται υποθέσεις για εξοφλήσεις.
          <button type="button" class="admin-secondary" onclick={() => reload++}>Ανανέωση</button>
        </section>
      {:else if status}
        <section class="admin-panel">
          <h2>Σύνδεση παρόχου</h2>
          <p>{status.configured ? 'Sandbox ενεργό · δοκιμαστικοί σύνδεσμοι μόνο.' :
            'Δεν έχουν ρυθμιστεί δοκιμαστικά διαπιστευτήρια payabl. από τον διαχειριστή συστήματος.'}</p>
          <p class="admin-muted">Συνδρομές, επιστροφές και πληρωμές προσωπικού: απαιτούν επιπλέον έγκριση παρόχου και ολοκλήρωση της ασφαλούς διαδικασίας.</p>
        </section>
        {#if canWrite && status.configured}
          <section class="admin-panel">
            <h2>Νέο δοκιμαστικό αίτημα</h2>
            <form onsubmit={createLink} class="payabl-form">
              <label for="payment-purpose">Αιτιολογία</label>
              <input id="payment-purpose" required maxlength="140" bind:value={purpose} placeholder="Academy fee test" />
              <label for="payment-amount">Ποσό EUR</label>
              <input id="payment-amount" required inputmode="decimal" bind:value={amount} placeholder="15.00" />
              <label for="payment-email">Δοκιμαστικό email</label>
              <input id="payment-email" type="email" required maxlength="50" bind:value={email} placeholder="test@example.com" />
              <label for="payment-first">Όνομα (λατινικά)</label>
              <input id="payment-first" required maxlength="50" bind:value={first} placeholder="Test" />
              <label for="payment-last">Επώνυμο (λατινικά)</label>
              <input id="payment-last" required maxlength="50" bind:value={last} placeholder="Customer" />
              <button type="submit" disabled={writing}>{writing ? 'Επικοινωνία με sandbox…' : 'Δημιουργία δοκιμαστικού συνδέσμου'}</button>
            </form>
            {#if writeError}<p role="alert">{writeError}</p>{/if}
            {#if lastLink}<p>Ο σύνδεσμος δημιουργήθηκε αλλά δεν έχει εξοφληθεί.
              <a href={lastLink} rel="noreferrer noopener" target="_blank">Άνοιγμα δοκιμαστικού checkout</a></p>{/if}
          </section>
        {/if}
        <section class="admin-panel" data-testid="payabl-payment-ledger">
          <h2>Ιστορικό αιτημάτων · {owner}</h2>
          {#if payments.length === 0}
            <p>Δεν έχουν καταγραφεί αιτήματα για αυτόν τον οργανισμό.</p>
          {:else}
            <ul class="payabl-list">
              {#each payments as payment (payment.id)}
                <li>
                  <strong>{payment.purpose}</strong>
                  <span>{money(payment.amount_cents)} · {payment.status === 'paid' ? 'Επαληθευμένη πληρωμή' :
                    payment.status === 'ready' ? 'Έτοιμο προς πληρωμή' :
                    payment.status === 'review_required' ? 'Απαιτείται συμφωνία συναλλαγής' :
                    'Σε εξέλιξη'}</span>
                  {#if trustedLink(payment.checkout_url) && payment.status === 'ready'}
                    <a href={payment.checkout_url} target="_blank" rel="noreferrer noopener">Δοκιμαστικός σύνδεσμος</a>
                  {/if}
                </li>
              {/each}
            </ul>
          {/if}
          <button type="button" class="admin-secondary" onclick={() => reload++}>Επαλήθευση εκ νέου</button>
        </section>
      {/if}
    {:else}
      <section class="admin-panel" role="alert">Δεν υπάρχει δικαίωμα ανάγνωσης οικονομικών για ενεργό οργανισμό.</section>
    {/if}
  </AdminFrame>
{/if}

<style>
  .payabl-form{display:grid;grid-template-columns:1fr;gap:.65rem;max-width:32rem}
  .payabl-form input{width:100%;padding:.75rem;border:1px solid var(--border-color,#9ca3af);border-radius:.5rem;background:var(--surface,#fff);color:inherit}
  .payabl-form button{padding:.8rem;border-radius:.5rem}
  .payabl-list{list-style:none;margin:1rem 0;padding:0;display:grid;gap:.7rem}
  .payabl-list li{display:flex;gap:.6rem;flex-wrap:wrap;align-items:center;padding:.9rem;border:1px solid currentColor;border-radius:.5rem}
  .payabl-list span{margin-left:auto}
</style>
