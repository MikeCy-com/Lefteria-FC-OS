<script lang="ts">
  import { getContext } from 'svelte';
  import { browser } from '$app/env';
  import { page } from '$app/state';
  import { goto } from '$app/navigation';
  import { ADMIN_AUTH_CONTEXT, type AdminAuthController } from '../../../lib/admin/controller.ts';
  import { hasAdminPermission } from '../../../lib/admin/permissions.ts';
  import { preferredAdminOrganization } from '../../../lib/admin/admin-organization.ts';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';
  import { greekDate, fixtureStatus } from '../../../lib/public/content.ts';
  import type { PublicFixture } from '../../../lib/public/types.ts';

  type Organization = 'lefteria_fc' | 'lefteria_fc_academy';
  type Tab = 'upcoming' | 'results' | 'cup' | 'all';
  type Choice = { id: string; name: string };
  const auth = getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  const requestedOrganization = $derived(page.url.searchParams.get('organization_id'));
  const organization = $derived(
    (requestedOrganization==='lefteria_fc'||requestedOrganization==='lefteria_fc_academy') &&
    $auth.session?.user.organizations.some(org=>org.id===requestedOrganization)
      ? requestedOrganization : preferredAdminOrganization($auth.session?.user ?? null));
  const canRead = $derived(
    $auth.status === 'authenticated' && !!$auth.session && !!organization &&
    $auth.session.user.organizations.some(org => org.id === organization) &&
    (organization === 'lefteria_fc'
      ? hasAdminPermission($auth.session.user.permissions, 'teams:read')
      : hasAdminPermission($auth.session.user.permissions, 'academy:read'))
  );

  let fixtures = $state<PublicFixture[]>([]);
  let choices = $state<Choice[]>([]);
  let tab = $state<Tab>('upcoming');
  let team = $state('all');
  let season = $state('all');
  let loading = $state(false);
  let unavailable = $state(false);
  let reload = $state(0);

  const completed = (fixture: PublicFixture) => fixture.status === 'Completed';
  const upcoming = (fixture: PublicFixture) =>
    fixture.status === 'Scheduled' || fixture.status === 'Live' || fixture.status === 'Half Time';
  const cup = (fixture: PublicFixture) => /(?:cup|κύπελλο|κυπελλο)/iu.test(fixture.competition ?? '');
  const safeLogo = (url: string | null | undefined) => url &&
    (/^https:\/\/[^\s"'<>{}]+$/u.test(url) || /^\/media\/[a-zA-Z0-9/_%-]+$/u.test(url)) ? url : null;

  function verifiedSite(value: unknown, owner: Organization): value is {
    organization_id: Organization; fixtures: PublicFixture[];
    teams: Choice[]; academy_groups: Choice[];
  } {
    if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
    const row = value as Record<string, unknown>;
    if (row.organization_id !== owner || !Array.isArray(row.fixtures) || row.fixtures.length > 50 ||
      !Array.isArray(row.teams) || !Array.isArray(row.academy_groups)) return false;
    const validChoice = (item: unknown): item is Choice => {
      if (!item || typeof item !== 'object' || Array.isArray(item)) return false;
      const x = item as Record<string, unknown>;
      return typeof x.id === 'string' && x.id.length > 0 && x.id.length <= 128 &&
        typeof x.name === 'string' && x.name.length > 0 && x.name.length <= 200 &&
        x.organization_id === owner;
    };
    return row.teams.every(validChoice) && row.academy_groups.every(validChoice) &&
      row.fixtures.every((item: unknown) => {
        if (!item || typeof item !== 'object' || Array.isArray(item)) return false;
        const x = item as Record<string, unknown>;
        return typeof x.id === 'string' && x.id.length > 0 && x.organization_id === owner &&
          typeof x.home_team === 'string' && typeof x.away_team === 'string' &&
          typeof x.match_date === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(x.match_date) &&
          typeof x.status === 'string' && x.status.length <= 50 &&
          (x.home_score === null || Number.isInteger(x.home_score) && (x.home_score as number) >= 0) &&
          (x.away_score === null || Number.isInteger(x.away_score) && (x.away_score as number) >= 0);
      });
  }

  const seasons = $derived([...new Set(fixtures.map(item => item.match_date.slice(0, 4)))].sort().reverse());
  const filtered = $derived(fixtures.filter(fixture => {
    if (team !== 'all' && fixture.team_id !== team && fixture.academy_group_id !== team) return false;
    if (season !== 'all' && !fixture.match_date.startsWith(season)) return false;
    if (tab === 'upcoming' && !upcoming(fixture)) return false;
    if (tab === 'results' && !completed(fixture)) return false;
    if (tab === 'cup' && !cup(fixture)) return false;
    return true;
  }).sort((a, b) => {
    const order = a.match_date.localeCompare(b.match_date);
    return tab === 'results' ? -order : order;
  }));
  const upcomingCount = $derived(fixtures.filter(upcoming).length);
  const resultsCount = $derived(fixtures.filter(completed).length);
  const cupCount = $derived(fixtures.filter(cup).length);
  const tabs: { id: Tab; label: string }[] = [
    { id: 'upcoming', label: 'ΠΡΟΓΡΑΜΜΑΤΙΣΜΕΝΟΙ' },
    { id: 'results', label: 'ΑΠΟΤΕΛΕΣΜΑΤΑ' },
    { id: 'cup', label: 'ΚΥΠΕΛΛΟ' },
    { id: 'all', label: 'ΟΛΟΙ ΟΙ ΑΓΩΝΕΣ' }
  ];

  $effect(() => {
    if (browser && $auth.status === 'anonymous') void goto('/admin/login', { replace: true });
  });

  $effect(() => {
    const owner = organization;
    const access = canRead;
    const actor = $auth.session?.user.id;
    void actor; void reload;
    fixtures = []; choices = []; loading = false; unavailable = false;
    team = 'all'; season = 'all';
    const requestedGroup=page.url.searchParams.get('group_id');
    if (!browser || !owner || !access) return;
    const request = new AbortController();
    loading = true;
    void (async () => {
      try {
        // The existing public-content API returns the safe, published
        // projection only; this page never accesses a private roster or
        // fabricates results/standings. The endpoint caps fixtures at 50.
        const response = await fetch('/api/public/site?organization_id=' + encodeURIComponent(owner), {
          credentials: 'same-origin', cache: 'no-store',
          headers: { Accept: 'application/json' }, signal: request.signal
        });
        if (!response.ok) throw new Error('Published matches unavailable.');
        const data: unknown = await response.json();
        if (!verifiedSite(data, owner)) throw new Error('Unverified public matches.');
        if (!request.signal.aborted) {
          fixtures = data.fixtures;
          choices = owner === 'lefteria_fc' ? data.teams : data.academy_groups;
          // Deep links originate from a verified, selected group. Reject unknown
          // ids rather than showing another organization's fixtures.
          if (requestedGroup && choices.some(item=>item.id===requestedGroup))team=requestedGroup;
        }
      } catch {
        if (!request.signal.aborted) unavailable = true;
      } finally {
        if (!request.signal.aborted) loading = false;
      }
    })();
    return () => request.abort();
  });
</script>

<svelte:head>
  <title>Αγώνες & αποτελέσματα | Διαχείριση Λευτεριά FC</title>
  <meta name="robots" content="noindex, nofollow" />
</svelte:head>

{#if $auth.status === 'authenticated' && $auth.session}
  <AdminFrame user={$auth.session.user} active="matches" busy={$auth.busy} onLogout={() => void auth.logout()}>
    <section class="admin-matches" data-testid="admin-matches-page">
      <header class="admin-matches-heading">
        
        <h1>ΑΓΩΝΕΣ <span>&amp; ΑΠΟΤΕΛΕΣΜΑΤΑ</span></h1>
        {#if team!=='all' && choices.some(item=>item.id===team)}
          <a class="admin-match-back-to-group"
             href={`/admin/groups?group_id=${encodeURIComponent(team)}`}
             data-testid="admin-match-back-to-group">← Επιστροφή στο τμήμα</a>
        {/if}
        <p>Αγωνιστικό πρόγραμμα και αποτελέσματα που έχουν ήδη δημοσιευθεί. Η δημιουργία αγώνων και η επεξεργασία σκορ δεν έχουν ενεργοποιηθεί.</p>
      </header>
      {#if !canRead}
        <section class="admin-panel" role="alert" data-testid="admin-matches-denied">
          Δεν υπάρχει δικαίωμα προβολής αγώνων για τον επιλεγμένο οργανισμό. Δεν υπάρχει εξουσιοδοτημένος οργανισμός της διαχείρισης.
        </section>
      {:else}
        <div class="admin-matches-overview" aria-label="Σύνοψη δημοσιευμένων αγώνων">
          <div><span>ΕΠΟΜΕΝΟΙ</span><strong>{loading || unavailable ? '—' : upcomingCount}</strong></div>
          <div><span>ΑΠΟΤΕΛΕΣΜΑΤΑ</span><strong>{loading || unavailable ? '—' : resultsCount}</strong></div>
          <div><span>ΚΥΠΕΛΛΟ</span><strong>{loading || unavailable ? '—' : cupCount}</strong></div>
        </div>
        <div class="admin-matches-tabs" role="group" aria-label="Κατηγορία αγώνων">
          {#each tabs as item (item.id)}
            <button type="button" aria-pressed={tab === item.id}
              onclick={() => tab = item.id}>{item.label}</button>
          {/each}
        </div>
        <section class="admin-matches-panel" aria-label="Δημοσιευμένοι αγώνες">
          <div class="admin-matches-controls">
            <h2>{tabs.find(item => item.id === tab)?.label}</h2>
            <div class="admin-matches-filters">
              <label>Ομάδα / τμήμα
                <select bind:value={team} aria-label="Φίλτρο ομάδας">
                  <option value="all">Όλες οι ομάδες</option>
                  {#each choices as item (item.id)}<option value={item.id}>{item.name}</option>{/each}
                </select>
              </label>
              <label>Έτος
                <select bind:value={season} aria-label="Φίλτρο έτους">
                  <option value="all">Όλα</option>
                  {#each seasons as year (year)}<option value={year}>{year}</option>{/each}
                </select>
              </label>
              <button type="button" disabled={loading} onclick={() => reload++}>Ανανέωση</button>
            </div>
          </div>
          {#if loading}
            <p class="admin-matches-state" role="status">Φόρτωση επαληθευμένων αγώνων…</p>
          {:else if unavailable}
            <div class="admin-matches-state" role="alert" data-testid="admin-matches-unavailable">
              <p>Το πρόγραμμα δεν είναι διαθέσιμο. Δεν εμφανίζουμε υποθετικούς αγώνες ή αποτελέσματα.</p>
              <button type="button" onclick={() => reload++}>Δοκιμή ξανά</button>
            </div>
          {:else if filtered.length === 0}
            <p class="admin-matches-state" data-testid="admin-matches-empty">Δεν υπάρχουν δημοσιευμένοι αγώνες για τα επιλεγμένα φίλτρα.</p>
          {:else}
            <div class="admin-matches-list" data-testid="admin-matches-list">
              {#each filtered as fixture (fixture.id)}
                <article class="admin-match-card">
                  <div class="admin-match-date">
                    <time datetime={fixture.match_date}>{greekDate(fixture.match_date)}</time>
                    <span>{fixture.match_time ?? 'Ώρα μη διαθέσιμη'}</span>
                  </div>
                  <div class="admin-match-pair">
                    <div class="admin-match-team">
                      {#if safeLogo(fixture.home_team_logo)}
                        <img src={safeLogo(fixture.home_team_logo) ?? ''} alt="" width="42" height="42" loading="lazy" />
                      {/if}
                      <strong>{fixture.home_team}</strong>
                    </div>
                    <b class="admin-match-score">{fixture.home_score !== null && fixture.away_score !== null
                      ? fixture.home_score + ' : ' + fixture.away_score : 'VS'}</b>
                    <div class="admin-match-team">
                      {#if safeLogo(fixture.away_team_logo)}
                        <img src={safeLogo(fixture.away_team_logo) ?? ''} alt="" width="42" height="42" loading="lazy" />
                      {/if}
                      <strong>{fixture.away_team}</strong>
                    </div>
                  </div>
                  <div class="admin-match-meta">
                    <span class="admin-match-status">{fixtureStatus(fixture)}</span>
                    <small>{fixture.competition ?? 'Διοργάνωση μη διαθέσιμη'}</small>
                    <small>{fixture.venue ?? 'Γήπεδο μη διαθέσιμο'}</small>
                  </div>
                  <a class="admin-match-detail" href={'/match/' + encodeURIComponent(fixture.id)}
                    aria-label={'Λεπτομέρειες αγώνα ' + fixture.home_team + ' – ' + fixture.away_team}>
                    ΛΕΠΤΟΜΕΡΕΙΕΣ <span aria-hidden="true">↗</span>
                  </a>
                </article>
              {/each}
            </div>
          {/if}
          <p class="admin-matches-footnote">Το δημόσιο API επιστρέφει έως 50 πρόσφατους και επόμενους δημοσιευμένους αγώνες. Οι αριθμοί αφορούν μόνο αυτή την προβολή, όχι όλη τη σεζόν. Η βαθμολογία και η επεξεργασία απαιτούν ξεχωριστά επαληθευμένα API.</p>
        </section>
      {/if}
    </section>
  </AdminFrame>
{:else}
  <AdminSessionStatus unavailable={$auth.status === 'unavailable'} busy={$auth.busy}
    error={$auth.error} onRetry={() => void auth.bootstrap()} onLogout={() => void auth.logout()} />
{/if}

<style>
  .admin-matches{width:100%;max-width:none;min-width:0;margin:0 auto;padding:0 0 60px;color:#fff}
  .admin-matches-heading{padding:28px 0 24px;border-bottom:1px solid #3b3d3f}
  .admin-matches-heading h1{font:italic 900 clamp(37px,4.4vw,67px)/1 'Fira Sans Extra Condensed',sans-serif;letter-spacing:.01em;margin:6px 0 12px}
  .admin-matches-heading h1 span{color:#f6bb38}
  .admin-matches-heading>p:last-child{color:#c5c6c8;font-size:14px;max-width:750px}
  .admin-matches-overview{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px;margin:22px 0}
  .admin-matches-overview>div{display:flex;align-items:center;justify-content:space-between;gap:10px;padding:17px;border:1px solid #43464a;border-top:2px solid #f6bb38;background:linear-gradient(145deg,#17191b,#101112)}
  .admin-matches-overview span{font:800 17px/1.1 'Fira Sans Extra Condensed',sans-serif;color:#d9dadd}
  .admin-matches-overview strong{font:900 39px/1 'Fira Sans Extra Condensed',sans-serif;color:#f6bb38}
  .admin-matches-tabs{display:flex;flex-wrap:wrap;gap:0;border-bottom:2px solid #f6bb38;margin:0 0 21px}
  .admin-matches-tabs button{min-height:47px;padding:11px 17px;border:1px solid #515255;border-bottom:0;border-radius:0;color:white;background:#131516;font:800 15px/1.2 'Fira Sans Extra Condensed',sans-serif}
  .admin-matches-tabs button[aria-pressed=true]{background:#f6bb38;color:#090909;border-color:#f6bb38}
  .admin-matches-panel{border:1px solid #383b3d;background:#111314;min-width:0}
  .admin-matches-controls{display:flex;align-items:end;justify-content:space-between;flex-wrap:wrap;gap:15px;border-bottom:1px solid #4e4140;padding:17px 21px}
  .admin-matches-controls h2{font:italic 800 27px/1.2 'Fira Sans Extra Condensed',sans-serif;margin:0;color:white}
  .admin-matches-filters{display:flex;align-items:end;flex-wrap:wrap;gap:9px}
  .admin-matches-filters label{font:600 12px/1.5 'Fira Sans',sans-serif;color:#babbbd;margin:0}
  .admin-matches-filters select,.admin-matches-filters button{display:block;min-height:42px;width:auto;max-width:220px;margin:4px 0 0;padding:8px 12px;border:1px solid #626264;background:#090b0c;color:#fff;font:700 13px 'Fira Sans',sans-serif}
  .admin-matches-filters button{color:#f6bb38}
  .admin-matches-state{margin:0;padding:27px 20px;color:#c6c8c9}
  .admin-matches-state button{display:block;margin-top:15px;color:#f6bb38}
  .admin-match-card{display:grid;grid-template-columns:150px minmax(0,1fr) 180px 124px;align-items:center;gap:15px;min-height:111px;padding:17px 20px;border-bottom:1px solid #393b3d}
  .admin-match-card:last-child{border-bottom:0}
  .admin-match-date{display:flex;flex-direction:column;gap:7px;font-size:13px;color:#fff}
  .admin-match-date span{color:#f6bb38;font-size:12px}
  .admin-match-pair{display:flex;align-items:center;justify-content:space-around;gap:10px;min-width:0}
  .admin-match-team{display:flex;align-items:center;gap:8px;min-width:0;flex:1;justify-content:center}
  .admin-match-team strong{font:800 17px/1.15 'Fira Sans Extra Condensed',sans-serif;overflow-wrap:anywhere}
  .admin-match-team img{flex:0 0 42px;object-fit:contain}
  .admin-match-score{flex:0 0 53px;text-align:center;font:italic 900 24px/1 'Fira Sans Extra Condensed',sans-serif}
  .admin-match-meta{display:flex;flex-direction:column;gap:6px}
  .admin-match-meta small{font-size:11px;color:#c4c7c8}
  .admin-match-status{font:800 14px/1.2 'Fira Sans Extra Condensed',sans-serif;color:#f6bb38}
  .admin-match-detail{display:flex;justify-content:space-between;gap:8px;align-items:center;padding:10px;border:1px solid #f6bb38;color:#f6bb38!important;text-decoration:none;font:800 14px/1.2 'Fira Sans Extra Condensed',sans-serif}
  .admin-matches-footnote{margin:0;padding:15px 20px;border-top:1px solid #373b3f;font-size:12px;color:#aeb1b2}
  @media(max-width:1100px){.admin-match-card{grid-template-columns:125px minmax(0,1fr);}.admin-match-meta{grid-column:1}.admin-match-detail{grid-column:2;justify-self:end}}
  @media(max-width:700px){
    .admin-matches-heading h1{font-size:36px}
    .admin-matches-overview{gap:7px}
    .admin-matches-overview>div{padding:11px;flex-direction:column;align-items:start}
    .admin-matches-overview span{font-size:12px}.admin-matches-overview strong{font-size:31px}
    .admin-matches-tabs button{flex:1 1 40%;font-size:13px;padding:8px}
    .admin-matches-controls{padding:15px}
    .admin-matches-filters{width:100%}.admin-matches-filters label{flex:1 1 130px}
    .admin-matches-filters select{width:100%}
    .admin-match-card{display:flex;flex-direction:column;align-items:stretch;padding:15px}
    .admin-match-date{flex-direction:row;flex-wrap:wrap;gap:9px}
    .admin-match-team{flex-direction:column;text-align:center;gap:6px}
    .admin-match-pair{width:100%}.admin-match-team strong{font-size:15px}
    .admin-match-meta{flex-direction:row;flex-wrap:wrap;align-items:center}
    .admin-match-detail{align-self:flex-end}
  }
  @media(prefers-reduced-motion:reduce){.admin-matches *{scroll-behavior:auto}}
</style>
