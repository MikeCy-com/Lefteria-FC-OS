<script lang="ts">
  import { getContext } from 'svelte';
  import { browser } from '$app/env';
  import { goto } from '$app/navigation';
  import type { components } from '../../../lib/api/schema';
  import { ADMIN_AUTH_CONTEXT, type AdminAuthController } from '../../../lib/admin/controller.ts';
  import { hasAdminPermission } from '../../../lib/admin/permissions.ts';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';

  type Article = components['schemas']['PublicArticle'];
  type Result = { items: Article[]; total: number; offset: number; limit: number };
  type Organization = 'lefteria_fc' | 'lefteria_fc_academy';
  const auth = getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  const pageSize = 20;
  const allowedOrganizations = $derived(
    $auth.status === 'authenticated' && $auth.session &&
    hasAdminPermission($auth.session.user.permissions, 'news:read')
      ? $auth.session.user.organizations.filter((org): org is typeof org & {id: Organization} =>
          org.id === 'lefteria_fc' || org.id === 'lefteria_fc_academy')
      : []
  );
  let organization = $state<Organization | null>(null);
  let offset = $state(0);
  let retry = $state(0);
  let articles = $state<Article[]>([]);
  let total = $state(0);
  let loading = $state(false);
  let unavailable = $state(false);

  $effect(() => {
    if (browser && $auth.status === 'anonymous') void goto('/admin/login', { replace: true });
  });

  $effect(() => {
    // Never keep the prior organization's selection across fresh revocations.
    if (!allowedOrganizations.some(org => org.id === organization)) {
      organization = allowedOrganizations[0]?.id ?? null;
      offset = 0;
    }
  });

  function verifiedResponse(value: unknown, owner: Organization, requestedOffset: number): value is Result {
    if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
    const data = value as Record<string, unknown>;
    return Array.isArray(data.items) && Number.isSafeInteger(data.total) && (data.total as number) >= 0 &&
      data.offset === requestedOffset && data.limit === pageSize &&
      data.items.length <= pageSize && data.items.every((item: unknown) => {
        if (!item || typeof item !== 'object' || Array.isArray(item)) return false;
        const row = item as Record<string, unknown>;
        return row.organization_id === owner && typeof row.id === 'string' && row.id.length > 0 &&
          typeof row.title === 'string' && typeof row.excerpt === 'string' &&
          typeof row.category === 'string' && (row.created_at === null || typeof row.created_at === 'string');
      });
  }

  $effect(() => {
    // Read only after the controller has a current, scoped admin session.
    // The server also revalidates cookie, role and organization on EVERY GET.
    const currentUser = $auth.session?.user.id;
    const currentPermissions = $auth.session?.user.permissions.join('|');
    const currentGrants = allowedOrganizations.map(org => org.id).join('|');
    const selected = organization;
    const requestedOffset = offset;
    const generation = retry;
    void currentUser; void currentPermissions; void currentGrants; void generation;

    articles = [];
    total = 0;
    loading = false;
    unavailable = false;

    if (!browser || $auth.status !== 'authenticated' || !$auth.session || !selected ||
        !allowedOrganizations.some(org => org.id === selected)) return;

    const controller = new AbortController();
    loading = true;
    void (async () => {
      try {
        const query = new URLSearchParams({organization_id: selected, offset: String(requestedOffset), limit: String(pageSize)});
        const response = await fetch('/api/admin/website/news?' + query.toString(), {
          method: 'GET', credentials: 'same-origin', cache: 'no-store', signal: controller.signal
        });
        if (controller.signal.aborted) return;
        if (response.status === 401) {
          auth.invalidateSession();
          return;
        }
        if (response.status === 403) {
          void auth.bootstrap();
          throw new Error('Authorization changed.');
        }
        if (!response.ok) throw new Error('News unavailable.');
        const raw: unknown = await response.json();
        if (!verifiedResponse(raw, selected, requestedOffset)) throw new Error('Unverified news.');
        if (!controller.signal.aborted) {
          articles = raw.items;
          total = raw.total;
        }
      } catch {
        if (!controller.signal.aborted) unavailable = true;
      } finally {
        if (!controller.signal.aborted) loading = false;
      }
    })();
    return () => controller.abort();
  });
</script>

<svelte:head><title>Άρθρα & νέα | Διαχείριση Λευτεριά FC</title></svelte:head>

{#if $auth.status === 'authenticated' && $auth.session}
  <AdminFrame user={$auth.session.user} active="news" busy={$auth.busy} onLogout={() => void auth.logout()}>
    {#if allowedOrganizations.length}
      <div class="admin-news" data-testid="admin-news-readonly">
        <header class="admin-news-heading">
          <div>
            <p class="admin-eyebrow">ΙΣΤΟΣΕΛΙΔΑ · ΔΗΜΟΣΙΕΥΜΕΝΟ ΠΕΡΙΕΧΟΜΕΝΟ</p>
            <h1>Άρθρα & νέα</h1>
            <p class="admin-muted">Επαληθευμένη προβολή των δημοσιευμένων άρθρων του οργανισμού. Η δημιουργία, η επεξεργασία και η δημοσίευση παραμένουν ανενεργές.</p>
          </div>
          <a class="admin-news-public" href="/news">Δημόσια νέα <span aria-hidden="true">↗</span></a>
        </header>
        <section class="admin-panel" aria-label="Φίλτρα δημοσιευμένων άρθρων">
          <label for="admin-news-owner">Οργανισμός</label>
          <select id="admin-news-owner" data-testid="admin-news-organization" value={organization ?? ''}
            onchange={event => {
              const next = event.currentTarget.value;
              if (allowedOrganizations.some(org => org.id === next)) {
                organization = next as Organization;
                offset = 0;
              }
            }}>
            {#each allowedOrganizations as org (org.id)}
              <option value={org.id}>{org.name}</option>
            {/each}
          </select>
        </section>
        {#if loading}
          <div class="admin-panel admin-news-state" role="status" data-testid="admin-news-loading">Φόρτωση επαληθευμένων άρθρων…</div>
        {:else if unavailable}
          <div class="admin-panel admin-news-state" role="alert" data-testid="admin-news-unavailable">
            <p>Τα άρθρα δεν είναι διαθέσιμα. Δεν εμφανίζουμε αποθηκευμένα ή μη επαληθευμένα δεδομένα.</p>
            <button type="button" class="admin-secondary" onclick={() => retry++}>Δοκιμή ξανά</button>
          </div>
        {:else if articles.length === 0}
          <div class="admin-panel admin-news-state" role="status" data-testid="admin-news-empty">
            {#if total === 0}Δεν υπάρχουν δημοσιευμένα άρθρα για αυτόν τον οργανισμό.
            {:else}Δεν υπάρχουν άρθρα σε αυτή τη σελίδα.{/if}
          </div>
        {:else}
          <section class="admin-panel" aria-label="Λίστα δημοσιευμένων άρθρων" data-testid="admin-news-list">
            <div class="admin-news-list-heading">
              <h2>Δημοσιεύσεις</h2>
              <span>{total} άρθρα</span>
            </div>
            <ul class="admin-news-list">
              {#each articles as item (item.id)}
                <li class="admin-news-article">
                  <div>
                    <span class="admin-news-category">{item.category}</span>
                    <h3>{item.title}</h3>
                    <p>{item.excerpt}</p>
                    {#if item.created_at && !Number.isNaN(Date.parse(item.created_at))}
                      <time datetime={item.created_at}>{new Intl.DateTimeFormat('el-GR', {dateStyle: 'medium', timeZone: 'Asia/Nicosia'}).format(new Date(item.created_at))}</time>
                    {/if}
                  </div>
                  <span class="admin-news-status">Δημοσιευμένο</span>
                </li>
              {/each}
            </ul>
          </section>
        {/if}
        {#if !unavailable && !loading && total > pageSize}
          <nav class="admin-news-pagination" aria-label="Σελίδες άρθρων">
            <button type="button" class="admin-secondary" disabled={offset === 0 || $auth.busy}
              onclick={() => { offset = Math.max(0, offset - pageSize); }}>Προηγούμενη</button>
            <span>Σελίδα {Math.floor(offset / pageSize) + 1} / {Math.ceil(total / pageSize)}</span>
            <button type="button" class="admin-secondary" disabled={offset + pageSize >= total || $auth.busy}
              onclick={() => { offset += pageSize; }}>Επόμενη</button>
          </nav>
        {/if}
      </div>
    {:else}
      <section class="admin-panel" data-testid="admin-news-denied">
        <h1>Δεν έχετε πρόσβαση στα άρθρα ιστοσελίδας.</h1>
        <a href="/admin">Επιστροφή στη διαχείριση</a>
      </section>
    {/if}
  </AdminFrame>
{:else}
  <AdminSessionStatus unavailable={$auth.status === 'unavailable'} busy={$auth.busy} error={$auth.error}
    onRetry={() => void auth.bootstrap()} onLogout={() => void auth.logout()} />
{/if}

<style>
  .admin-news{max-width:1100px}
  .admin-news-heading{display:flex;justify-content:space-between;align-items:flex-end;flex-wrap:wrap;gap:16px;margin-bottom:20px}
  .admin-news-heading h1{margin:0 0 12px}
  .admin-news-heading .admin-muted{max-width:67ch;margin:0}
  .admin-news-public{min-height:44px;display:inline-flex;align-items:center;gap:9px}
  .admin-news .admin-panel{margin:0 0 14px}
  .admin-news label{display:block;margin:0 0 7px;font-size:12px}
  .admin-news select{max-width:340px}
  .admin-news-list-heading{display:flex;align-items:center;justify-content:space-between;gap:15px}
  .admin-news-list-heading h2{margin:0}
  .admin-news-list-heading span{color:#a7adb0;font-size:12px}
  .admin-news-list{list-style:none;margin:16px 0 0;padding:0}
  .admin-news-article{display:flex;align-items:start;justify-content:space-between;gap:16px;padding:18px 0;border-top:1px solid #303335}
  .admin-news-article>div{min-width:0}
  .admin-news-category{font-size:11px;color:#f5a623}
  .admin-news-article h3{font-size:19px;margin:6px 0 8px;overflow-wrap:anywhere}
  .admin-news-article p{margin:0 0 8px;font-size:13px;color:#b9bdc0;overflow-wrap:anywhere}
  .admin-news-article time{font-size:11px;color:#a7adb0}
  .admin-news-status{flex:0 0 auto;color:#ffca46;border:1px solid #634b23;background:#302311;border-radius:7px;padding:5px 8px;font-size:11px}
  .admin-news-state{padding:23px;color:#c2c6c7}
  .admin-news-pagination{display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:12px}
  .admin-news-pagination span{color:#d5d7d8;font-size:12px}
  @media(max-width:720px){.admin-news-article{flex-direction:column;gap:8px}.admin-news select{max-width:100%}.admin-news-heading{align-items:flex-start}}
</style>
