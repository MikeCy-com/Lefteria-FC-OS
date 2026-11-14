<script lang="ts">
  import { onMount } from 'svelte';
  import { hasAdminPermission } from './permissions.ts';
  import { preferredAdminOrganization, type AdminOrganizationUser } from './admin-organization.ts';
  import { verifiedAdminAudiences, verifiedAdminPublishedNews, verifiedAdminNewLeads } from './admin-overview-data.ts';
  import type { AdminOverviewMetric, AdminPublishedNewsMetric } from './admin-overview-data.ts';

  let { user }: { user: AdminOrganizationUser & { permissions: string[] } } = $props();
  const organization = $derived(preferredAdminOrganization(user));
  const canSeeRoster = $derived(organization === 'lefteria_fc'
    ? hasAdminPermission(user.permissions, 'teams:read')
    : hasAdminPermission(user.permissions, 'academy:read'));
  const canSeeNews = $derived(hasAdminPermission(user.permissions, 'news:read'));
  const canSeeNewLeads = $derived(organization === 'lefteria_fc_academy' &&
    ['super_admin','manager'].includes(user.role) && hasAdminPermission(user.permissions,'marketing:read'));
  let roster = $state<AdminOverviewMetric | null>(null);
  let news = $state<AdminPublishedNewsMetric | null>(null);
  let pendingLeads = $state<number | null>(null);
  let leadIncrease = $state<number | null>(null);
  let previousLeadCount: number | null = null;
  let loading = $state(false);
  let error = $state(false);
  let refresh = $state(0);
  onMount(() => {
    let last = 0;
    const check = () => {
      if (document.visibilityState !== 'visible' || Date.now() - last < 30_000) return;
      last = Date.now(); refresh++;
    };
    window.addEventListener('focus', check);
    document.addEventListener('visibilitychange', check);
    return () => {
      window.removeEventListener('focus', check);
      document.removeEventListener('visibilitychange', check);
    };
  });

  $effect(() => {
    const owner = organization, actor = user.id, rosterAllowed = canSeeRoster, newsAllowed = canSeeNews,
      leadsAllowed = canSeeNewLeads;
    void actor; void refresh;
    roster = null; news = null; pendingLeads = null; error = false; loading = false;
    if (!owner || (!rosterAllowed && !newsAllowed && !leadsAllowed) || typeof window === 'undefined') return;
    const signal = new AbortController();
    loading = true;
    void (async () => {
      const requests = await Promise.allSettled([
        rosterAllowed
          ? fetch('/api/admin/football/audiences?organization_id=' + encodeURIComponent(owner),
              { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }, signal: signal.signal })
          : Promise.resolve(null),
        newsAllowed
          ? fetch('/api/admin/website/news?organization_id=' + encodeURIComponent(owner) + '&offset=0&limit=20',
              { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }, signal: signal.signal })
          : Promise.resolve(null),
        leadsAllowed
          ? fetch('/api/admin/crm/academy/leads/?organization_id=' + encodeURIComponent(owner) + '&offset=0&limit=25&status=new',
              { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }, signal: signal.signal })
          : Promise.resolve(null)
      ]);
      if (signal.signal.aborted) return;
      const values: (unknown | null)[] = [];
      for (const request of requests) {
        if (request.status !== 'fulfilled') { values.push(null); error = true; continue; }
        const response = request.value;
        if (!response) { values.push(null); continue; }
        if (!response.ok) { values.push(null); error = true; continue; }
        try { values.push(await response.json()); }
        catch { values.push(null); error = true; }
      }
      if (signal.signal.aborted) return;
      if (rosterAllowed) {
        roster = verifiedAdminAudiences(values[0], owner);
        if (!roster) error = true;
      }
      if (newsAllowed) {
        news = verifiedAdminPublishedNews(values[1], 20);
        if (!news) error = true;
      }
      if (leadsAllowed) {
        const current = verifiedAdminNewLeads(values[2], owner, 25);
        pendingLeads = current;
        if (current === null) error = true;
        else {
          if (previousLeadCount !== null && current > previousLeadCount)
            leadIncrease = current - previousLeadCount;
          previousLeadCount = current;
        }
      } else { previousLeadCount = null; leadIncrease = null; }
      loading = false;
    })().catch(() => { if (!signal.signal.aborted) { error = true; loading = false; } });
    return () => signal.abort();
  });
</script>

<section class="admin-panel admin-club-metrics" aria-labelledby="admin-club-metrics-heading" data-testid="admin-club-metrics">
  <header class="admin-club-metrics-heading">
    <div>
      <p class="admin-eyebrow">ΕΠΙΣΚΟΠΗΣΗ · {organization === 'lefteria_fc' ? 'ΣΥΛΛΟΓΟΣ' : 'ΑΚΑΔΗΜΙΑ'}</p>
      <h2 id="admin-club-metrics-heading">Στοιχεία οργανισμού</h2>
      <p class="admin-muted">Μόνο επιβεβαιωμένα στοιχεία από το API. Δεν συμπληρώνονται ενδεικτικοί αριθμοί.</p>
    </div>
    <button type="button" class="admin-secondary" disabled={loading} onclick={() => refresh++}>Ανανέωση</button>
  </header>
  {#if !organization}
    <p role="status">Δεν υπάρχει ενεργός οργανισμός.</p>
  {:else}
    <div class="admin-club-metrics-grid" aria-busy={loading}>
      {#if canSeeRoster}
        <div><span>Ομάδες / τμήματα</span><strong>{roster?.groupCount ?? '—'}</strong><small>Καταχωρισμένα τμήματα</small></div>
        <div><span>Ενεργές συμμετοχές</span><strong>{roster?.activeAssignments ?? '—'}</strong><small>Άθροισμα ανά τμήμα — όχι μοναδικά άτομα</small></div>
        <div><span>Παίκτες χωρίς τμήμα</span><strong>{roster?.unassignedPlayers ?? '—'}</strong><small>Εκκρεμής ανάθεση</small></div>
      {/if}
      {#if canSeeNews}
        <div><span>Δημοσιευμένα άρθρα</span><strong>{news?.publishedArticles ?? '—'}</strong><small>Περιεχόμενο τρέχοντος οργανισμού</small></div>
      {/if}
      {#if canSeeNewLeads}
        <a class="admin-metric-link" href="/admin/leads"><span>Νέα αιτήματα ενδιαφέροντος</span><strong>{pendingLeads ?? '—'}</strong><small>Επαληθευμένες εκκρεμότητες CRM</small></a>
      {/if}
    </div>
    {#if leadIncrease !== null && leadIncrease > 0}
      <div class="admin-metric-notice" role="status" data-testid="admin-new-leads-notice">
        <strong>Νέα αιτήματα Ακαδημίας: +{leadIncrease}</strong>
        <a href="/admin/leads">Άνοιγμα CRM</a>
        <button type="button" aria-label="Κλείσιμο ειδοποίησης" onclick={() => leadIncrease = null}>×</button>
      </div>
    {/if}
    {#if loading}<p role="status" class="admin-muted">Φόρτωση στοιχείων…</p>{/if}
    {#if error}<p role="alert" class="admin-muted">Κάποια στατιστικά δεν είναι διαθέσιμα. Δεν εμφανίζουμε μη επαληθευμένες τιμές.</p>{/if}
  {/if}
</section>
