<script lang="ts">
  import { hasAdminPermission } from './permissions.ts';
  import { preferredAdminOrganization, type AdminOrganizationUser } from './admin-organization.ts';
  import { verifiedAdminAudiences, verifiedAdminPublishedNews } from './admin-overview-data.ts';
  import type { AdminOverviewMetric, AdminPublishedNewsMetric } from './admin-overview-data.ts';

  let { user }: { user: AdminOrganizationUser & { permissions: string[] } } = $props();
  const organization = $derived(preferredAdminOrganization(user));
  const canSeeRoster = $derived(organization === 'lefteria_fc'
    ? hasAdminPermission(user.permissions, 'teams:read')
    : hasAdminPermission(user.permissions, 'academy:read'));
  const canSeeNews = $derived(hasAdminPermission(user.permissions, 'news:read'));
  let roster = $state<AdminOverviewMetric | null>(null);
  let news = $state<AdminPublishedNewsMetric | null>(null);
  let loading = $state(false);
  let error = $state(false);
  let refresh = $state(0);

  $effect(() => {
    const owner = organization, actor = user.id, rosterAllowed = canSeeRoster, newsAllowed = canSeeNews;
    void actor; void refresh;
    roster = null; news = null; error = false; loading = false;
    if (!owner || (!rosterAllowed && !newsAllowed) || typeof window === 'undefined') return;
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
    </div>
    {#if loading}<p role="status" class="admin-muted">Φόρτωση στοιχείων…</p>{/if}
    {#if error}<p role="alert" class="admin-muted">Κάποια στατιστικά δεν είναι διαθέσιμα. Δεν εμφανίζουμε μη επαληθευμένες τιμές.</p>{/if}
  {/if}
</section>
