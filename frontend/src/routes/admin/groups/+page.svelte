<script lang="ts">
  import { getContext } from 'svelte';
  import { browser } from '$app/env';
  import { goto } from '$app/navigation';
  import { ADMIN_AUTH_CONTEXT, type AdminAuthController } from '../../../lib/admin/controller.ts';
  import { hasAdminPermission } from '../../../lib/admin/permissions.ts';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';
  import AdminRosterPlayersPanel from '../../../lib/admin/AdminRosterPlayersPanel.svelte';
  import AdminCoachDirectoryPanel from '../../../lib/admin/AdminCoachDirectoryPanel.svelte';
  import AdminAcademyGroupProfilePanel from '../../../lib/admin/AdminAcademyGroupProfilePanel.svelte';
  import AdminFirstTeamProfilePanel from '../../../lib/admin/AdminFirstTeamProfilePanel.svelte';

  type Organization = 'lefteria_fc' | 'lefteria_fc_academy';
  type Audience = { id: string; name: string; kind: 'team' | 'academy_group'; active_players: number };
  type Directory = {organization_id: Organization; ready: true; unassigned_players: number; groups: Audience[]};
  const auth = getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);

  const assignments = $derived(
    $auth.status === 'authenticated' && $auth.session
      ? $auth.session.user.organizations.filter((org): org is typeof org & {id: Organization} =>
          (org.id === 'lefteria_fc' && hasAdminPermission($auth.session!.user.permissions, 'teams:read')) ||
          (org.id === 'lefteria_fc_academy' && hasAdminPermission($auth.session!.user.permissions, 'academy:read')))
      : []
  );

  let organization = $state<Organization | null>(null);
  let directory = $state<Directory | null>(null);
  let loading = $state(false);
  let unavailable = $state(false);
  let retry = $state(0);
  let showCreate = $state(false);
  let groupName = $state('');
  let saving = $state(false);
  let createError = $state<string | null>(null);
  const canCreate = $derived(
    $auth.status === 'authenticated' && $auth.session && !!organization &&
    assignments.some(org => org.id === organization) &&
    hasAdminPermission($auth.session.user.permissions,
      organization === 'lefteria_fc' ? 'teams:write' : 'academy:write')
  );

  async function createGroup() {
    if (!browser || !canCreate || !organization || !directory || loading ||
        saving || !showCreate) return;
    const name = groupName;
    if (!name || name.length > 120 || name !== name.trim() ||
        /[\x00-\x1f\x7f]/.test(name)) {
      createError = 'Εισάγετε όνομα τμήματος χωρίς περιττά κενά (έως 120 χαρακτήρες).';
      return;
    }
    const selected = organization;
    const actor = $auth.session?.user.id;
    saving = true;
    createError = null;
    try {
      const query = new URLSearchParams({organization_id:selected});
      const response = await fetch('/api/admin/football/groups?' + query.toString(), {
        method:'POST', credentials:'same-origin', cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({name})
      });
      if (organization !== selected || $auth.session?.user.id !== actor || !canCreate) return;
      if (response.status === 401 || response.status === 403) {
        void auth.bootstrap();
        showCreate = false;
        throw new Error('Τα δικαιώματα ή η σύνδεσή σας άλλαξαν.');
      }
      if (response.status === 409) {
        showCreate = false;
        retry++;
        throw new Error('Το τμήμα άλλαξε ή υπάρχει σύγκρουση. Ανανέωση καταλόγου.');
      }
      if (!response.ok) throw new Error('Το τμήμα δεν δημιουργήθηκε. Ελέγξτε το όνομα.');
      const saved: unknown = await response.json();
      if (!saved || typeof saved !== 'object' || Array.isArray(saved))
        throw new Error('Μη επαληθευμένη απάντηση αποθήκευσης.');
      const value = saved as Record<string,unknown>;
      if (typeof value.id !== 'string' || !/^grp_[0-9a-f]{32}$/.test(value.id) ||
          value.name !== name || value.kind !==
            (selected === 'lefteria_fc' ? 'team' : 'academy_group') ||
          value.active_players !== 0)
        throw new Error('Μη επαληθευμένη καταχώριση τμήματος.');
      showCreate = false;
      groupName = '';
      retry++;
    } catch(error) {
      if (organization === selected && $auth.session?.user.id === actor)
        createError = error instanceof Error ? error.message : 'Η δημιουργία δεν ολοκληρώθηκε.';
    } finally { saving = false; }
  }


  function verifiedDirectory(value: unknown, expected: Organization): value is Directory {
    if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
    const response = value as Record<string, unknown>;
    const groupKind = expected === 'lefteria_fc' ? 'team' : 'academy_group';
    const uniqueIds = new Set<string>();
    return response.organization_id === expected && response.ready === true &&
      Number.isSafeInteger(response.unassigned_players) && (response.unassigned_players as number) >= 0 &&
      Array.isArray(response.groups) && response.groups.length <= 150 &&
      response.groups.every((group: unknown) => {
        if (!group || typeof group !== 'object' || Array.isArray(group)) return false;
        const row = group as Record<string, unknown>;
        if (typeof row.id !== 'string' || row.id.length < 1 ||
            row.id.length > 128 || uniqueIds.has(row.id)) return false;
        uniqueIds.add(row.id);
        return typeof row.name === 'string' && row.name.length > 0 && row.name.length <= 200 &&
          row.kind === groupKind && Number.isSafeInteger(row.active_players) &&
          (row.active_players as number) >= 0;
      });
  }

  $effect(() => {
    if (browser && $auth.status === 'anonymous') void goto('/admin/login', {replace:true});
  });
  $effect(() => {
    const selected = organization;
    const allowed = canCreate;
    void selected; void allowed;
    showCreate = false;
    groupName = '';
    createError = null;
  });
  $effect(() => {
    // An owner revoked by the server may never remain selected in the UI.
    if (!assignments.some(org => org.id === organization))
      organization = assignments[0]?.id ?? null;
  });
  $effect(() => {
    const sessionId = $auth.session?.user.id;
    const role = $auth.session?.user.role;
    const permissions = $auth.session?.user.permissions.join('|');
    const grants = assignments.map(org => org.id).join('|');
    const owner = organization;
    const generation = retry;
    void sessionId; void role; void permissions; void grants; void generation;

    directory = null;
    loading = false;
    unavailable = false;
    if (!browser || $auth.status !== 'authenticated' || !$auth.session || !owner ||
        !assignments.some(org => org.id === owner)) return;

    const controller = new AbortController();
    loading = true;
    void (async () => {
      try {
        const query = new URLSearchParams({organization_id:owner});
        const response = await fetch('/api/admin/football/audiences?' + query.toString(), {
          method:'GET', credentials:'same-origin', cache:'no-store', signal:controller.signal
        });
        if (controller.signal.aborted) return;
        if (response.status === 401) {
          auth.invalidateSession();
          return;
        }
        if (response.status === 403) {
          void auth.bootstrap();
          throw new Error('Audience permissions were revoked.');
        }
        if (!response.ok) throw new Error('Roster is unavailable.');
        const raw:unknown = await response.json();
        if (!verifiedDirectory(raw,owner)) throw new Error('Unverified roster audience.');
        if (!controller.signal.aborted) directory = raw;
      } catch {
        if (!controller.signal.aborted) unavailable = true;
      } finally { if (!controller.signal.aborted) loading = false; }
    })();
    return () => controller.abort();
  });
</script>

<svelte:head><title>Ομάδες & τμήματα | Διαχείριση Λευτεριά FC</title></svelte:head>

{#if $auth.status === 'authenticated' && $auth.session}
  <AdminFrame user={$auth.session.user} active="groups" busy={$auth.busy} onLogout={() => void auth.logout()}>
    {#if assignments.length}
      <div class="admin-audiences" data-testid="admin-live-audiences">
        <p class="admin-eyebrow">ΑΓΩΝΙΣΤΙΚΟ · ΑΚΑΔΗΜΙΑ · ΠΡΑΓΜΑΤΙΚΑ ΜΕΛΗ</p>
        <h1>Ομάδες & τμήματα</h1>
        <p class="admin-muted">Ζωντανή εικόνα των ομάδων και των τμημάτων από το επαληθευμένο ρόστερ, όχι διπλές στατικές καταχωρίσεις. Οι αριθμοί περιλαμβάνουν μόνο ενεργούς παίκτες χωρίς αρχειοθέτηση ή μεταγραφή.</p>
        <section class="admin-panel admin-audiences-select" aria-label="Οργανισμός ομάδων">
          <label for="admin-audience-owner">Οργανισμός</label>
          <select id="admin-audience-owner" data-testid="admin-audience-owner" value={organization ?? ''}
            onchange={event => {
              if (assignments.some(org => org.id === event.currentTarget.value))
                organization = event.currentTarget.value as Organization;
            }}>
            {#each assignments as org (org.id)}
              <option value={org.id}>{org.name}</option>
            {/each}
          </select>
          <button type="button" class="admin-secondary" onclick={() => retry++} disabled={loading}>Ανανέωση</button>
        </section>

        {#if loading}
          <section class="admin-panel" role="status" data-testid="admin-audiences-loading">Φόρτωση πραγματικών τμημάτων…</section>
        {:else if unavailable || !directory}
          <section class="admin-panel" role="alert" data-testid="admin-audiences-unavailable">
            <p>Ο κατάλογος τμημάτων δεν είναι διαθέσιμος. Δεν εμφανίζουμε υποθετικές ομάδες ή στοιχεία παιδιών.</p>
            <button type="button" class="admin-secondary" onclick={() => retry++}>Δοκιμή ξανά</button>
          </section>
        {:else}
          <div class="admin-audiences-stats" aria-label="Πραγματική σύνοψη">
            <div><span>Ομάδες / τμήματα</span><strong>{directory.groups.length}</strong></div>
            <div><span>Ενεργές συμμετοχές</span><strong>{directory.groups.reduce((n,g) => n+g.active_players,0)}</strong></div>
            <div><span>Χωρίς τμήμα</span><strong>{directory.unassigned_players}</strong></div>
          </div>
          {#if canCreate}
            <section class="admin-panel admin-group-create" data-testid="admin-group-create" aria-labelledby="admin-group-create-heading">
              <div class="admin-group-create-heading">
                <div>
                  <p class="admin-eyebrow">ΔΗΜΙΟΥΡΓΙΑ · ΕΛΕΓΧΟΣ ΔΙΚΑΙΩΜΑΤΩΝ</p>
                  <h2 id="admin-group-create-heading">Νέο {organization === 'lefteria_fc' ? 'τμήμα / ομάδα' : 'τμήμα Ακαδημίας'}</h2>
                </div>
                {#if !showCreate}
                  <button type="button" class="admin-secondary" onclick={() => {showCreate=true;createError=null;}} disabled={saving}>Προσθήκη τμήματος</button>
                {/if}
              </div>
              {#if showCreate}
                <form class="admin-group-create-form" data-testid="admin-group-create-form"
                  onsubmit={event => {event.preventDefault(); void createGroup();}}>
                  <label for="admin-group-name">Όνομα τμήματος</label>
                  <input id="admin-group-name" type="text" required maxlength="120"
                    bind:value={groupName} disabled={saving}/>
                  <div class="admin-group-create-actions">
                    <button type="submit" class="admin-secondary" disabled={saving}>{saving ? 'Δημιουργία…' : 'Δημιουργία'}</button>
                    <button type="button" class="admin-secondary" disabled={saving}
                      onclick={() => {showCreate=false;createError=null;}}>Ακύρωση</button>
                  </div>
                </form>
              {/if}
              {#if createError}
                <p role="alert" class="admin-group-error">{createError}</p>
              {/if}
            </section>
          {/if}
          <section class="admin-panel" aria-labelledby="admin-audiences-title">
            <div class="admin-audiences-heading">
              <h2 id="admin-audiences-title">{organization === 'lefteria_fc' ? 'Ομάδες πρώτης ομάδας' : 'Τμήματα Ακαδημίας'}</h2>
              <span class="admin-audiences-caption">Ενημέρωση από το ρόστερ</span>
            </div>
            {#if directory.groups.length === 0}
              <p class="admin-muted" data-testid="admin-audiences-empty">Δεν υπάρχουν τμήματα στο επαληθευμένο ρόστερ αυτού του οργανισμού.</p>
            {:else}
              <ul class="admin-audiences-list" data-testid="admin-audiences-groups">
                {#each directory.groups as group (group.id)}
                  <li>
                    <div><span class="admin-audiences-type">{group.kind === 'team' ? 'ΟΜΑΔΑ' : 'ΤΜΗΜΑ ΑΚΑΔΗΜΙΑΣ'}</span><strong>{group.name}</strong></div>
                    <div class="admin-audiences-count"><strong>{group.active_players}</strong><span>ενεργοί παίκτες</span></div>
                  </li>
                {/each}
              </ul>
            {/if}
          </section>
          {#if canCreate && organization && $auth.session}
            {#key organization + ':' + $auth.session.user.id + ':' + $auth.session.user.role + ':' + $auth.session.user.permissions.join('|')}
              <AdminRosterPlayersPanel owner={organization as Organization}
                actorId={$auth.session.user.id} groups={directory.groups}
                onExpired={() => auth.invalidateSession()}
                onRevoked={() => void auth.bootstrap()}
                onChanged={() => retry++}/>
              {#if organization === 'lefteria_fc'}
                <AdminFirstTeamProfilePanel actorId={$auth.session.user.id}
                  groups={directory.groups}
                  onExpired={() => auth.invalidateSession()}
                  onRevoked={() => void auth.bootstrap()}/>
              {/if}
              {#if organization === 'lefteria_fc_academy'}
                <AdminAcademyGroupProfilePanel actorId={$auth.session.user.id}
                  groups={directory.groups}
                  onExpired={() => auth.invalidateSession()}
                  onRevoked={() => void auth.bootstrap()}/>
              {/if}
              <AdminCoachDirectoryPanel owner={organization as Organization}
                actorId={$auth.session.user.id} groups={directory.groups}
                onExpired={() => auth.invalidateSession()}
                onRevoked={() => void auth.bootstrap()}/>
            {/key}
          {/if}
          <p class="admin-audiences-note">Οι λίστες παραληπτών ανακοινώσεων και ομαδικών συνομιλιών θα χρησιμοποιούν αυτές τις ίδιες αναθέσεις, με ανεξάρτητη επαλήθευση συμμετοχής πριν από την αποστολή. Η αποστολή μηνυμάτων δεν έχει ενεργοποιηθεί.</p>
        {/if}
      </div>
    {:else}
      <section class="admin-panel" data-testid="admin-audiences-denied">
        <h1>Δεν έχετε πρόσβαση σε αυτόν τον κατάλογο ομάδων.</h1>
        <a href="/admin">Επιστροφή στη διαχείριση</a>
      </section>
    {/if}
  </AdminFrame>
{:else}
  <AdminSessionStatus unavailable={$auth.status === 'unavailable'} busy={$auth.busy} error={$auth.error}
    onRetry={() => void auth.bootstrap()} onLogout={() => void auth.logout()}/>
{/if}

<style>
  .admin-audiences{max-width:1190px}
  .admin-audiences>h1{margin:0 0 10px}
  .admin-audiences>.admin-muted{max-width:80ch;margin-bottom:18px}
  .admin-audiences-select{display:flex;align-items:end;flex-wrap:wrap;gap:12px;margin-bottom:14px}
  .admin-audiences-select label{display:flex;flex-direction:column;gap:7px;font-size:12px;color:#b9bcbe}
  .admin-audiences-select select{width:min(100%,350px)}
  .admin-group-create{margin:0 0 15px}
  .admin-group-create-heading{display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:12px}
  .admin-group-create-heading h2{margin:0}
  .admin-group-create-form{display:grid;gap:10px;max-width:500px;margin-top:16px}
  .admin-group-create-form>label{font-size:12px;color:#b9bcbe}
  .admin-group-create-form input{width:100%}
  .admin-group-create-actions{display:flex;flex-wrap:wrap;gap:9px}
  .admin-group-error{color:#ffd29b;margin-top:12px}
  .admin-audiences-stats{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:11px;margin:0 0 16px}
  .admin-audiences-stats>div{padding:16px;background:#121415;border:1px solid #303335;border-radius:12px;display:flex;flex-direction:column;gap:7px}
  .admin-audiences-stats span,.admin-audiences-caption,.admin-audiences-count span{font-size:12px;color:#aab0b3}
  .admin-audiences-stats strong{font-family:'Fira Sans Extra Condensed',sans-serif;font-size:29px;color:#ffca46}
  .admin-audiences-heading{display:flex;justify-content:space-between;align-items:center;flex-wrap:wrap;gap:10px}
  .admin-audiences-heading h2{margin:0}
  .admin-audiences-list{margin:12px 0 0;padding:0;list-style:none}
  .admin-audiences-list li{border-top:1px solid #303335;padding:13px 0;display:flex;justify-content:space-between;align-items:center;gap:16px;min-height:66px}
  .admin-audiences-list li>div:first-child{display:grid;gap:5px;min-width:0}
  .admin-audiences-list li strong{overflow-wrap:anywhere}
  .admin-audiences-type{color:#ffca46;letter-spacing:.075em;font-size:10px}
  .admin-audiences-count{display:flex;align-items:baseline;gap:9px;white-space:nowrap}
  .admin-audiences-count strong{font-family:'Fira Sans Extra Condensed',sans-serif;color:#ffca46;font-size:23px}
  .admin-audiences-note{max-width:80ch;color:#b5babd;font-size:12px;margin-top:18px}
  @media(max-width:650px){.admin-audiences-stats{grid-template-columns:1fr}.admin-audiences-list li{align-items:start}.admin-audiences-count{flex-direction:column;align-items:flex-end;gap:2px}}
</style>
