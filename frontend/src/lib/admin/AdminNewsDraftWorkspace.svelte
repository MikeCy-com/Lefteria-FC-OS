<script lang="ts">
  import { getContext } from 'svelte';
  import { ADMIN_AUTH_CONTEXT, type AdminAuthController } from './controller.ts';

  type Organization = 'lefteria_fc' | 'lefteria_fc_academy';
  type Editable = {
    title: string; content: string; excerpt: string; image_url: string | null;
    category: string; audience: string; is_featured: boolean;
  };
  type Draft = {
    id: string; organization_id: Organization; status: 'draft';
    version: number; article: Editable; updated_at: string;
  };
  type DraftPage = { organization_id: Organization; drafts: Draft[] };
  const auth = getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  let { owner, canWrite }: { owner: Organization; canWrite: boolean } = $props();

  const empty = (): Editable => ({
    title: '', content: '', excerpt: '', image_url: null,
    category: 'Νέα', audience: 'both', is_featured: false
  });
  let drafts = $state<Draft[]>([]);
  let selectedId = $state<string | null>(null);
  let form = $state<Editable>(empty());
  let baseline = $state(JSON.stringify(empty()));
  let currentVersion = $state<number | null>(null);
  let editorOpen = $state(false);
  let reload = $state(0);
  let loading = $state(false);
  let saving = $state(false);
  let conflict = $state(false);
  let error = $state('');
  let notice = $state('');
  const dirty = $derived(editorOpen && JSON.stringify(form) !== baseline);
  const allowed = $derived($auth.status === 'authenticated' && !!$auth.session &&
    $auth.session.user.organizations.some(org => org.id === owner) &&
    $auth.session.user.permissions.some(p => p === 'news:read' || p === 'news:*' || p === '*:*'));

  function validDraft(raw: unknown, organization: Organization): raw is Draft {
    if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return false;
    const entry = raw as Record<string, unknown>;
    if (entry.organization_id !== organization ||
        typeof entry.id !== 'string' || !/^draft_[0-9a-f]{32}$/.test(entry.id) ||
        entry.status !== 'draft' || !Number.isSafeInteger(entry.version) ||
        (entry.version as number) < 1 || typeof entry.updated_at !== 'string' ||
        !entry.article || typeof entry.article !== 'object' || Array.isArray(entry.article)) return false;
    const article = entry.article as Record<string, unknown>;
    return typeof article.title === 'string' && typeof article.content === 'string' &&
      typeof article.excerpt === 'string' && typeof article.category === 'string' &&
      typeof article.audience === 'string' && typeof article.is_featured === 'boolean' &&
      (article.image_url === null || typeof article.image_url === 'string');
  }

  function sameArticle(a: Editable, b: Editable) {
    return a.title === b.title && a.content === b.content && a.excerpt === b.excerpt &&
      a.image_url === b.image_url && a.category === b.category &&
      a.audience === b.audience && a.is_featured === b.is_featured;
  }

  $effect(() => {
    const organization = owner, actor = $auth.session?.user.id;
    const canRead = allowed, generation = reload;
    void actor; void generation;
    drafts = []; selectedId = null; form = empty(); baseline = JSON.stringify(form);
    editorOpen = false; currentVersion = null; loading = false;
    error = ''; notice = ''; conflict = false;
    if (!canRead) return;
    const controller = new AbortController();
    loading = true;
    void (async () => {
      try {
        const response = await fetch(
          '/api/admin/website/news-drafts?organization_id=' + encodeURIComponent(organization), {
            credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json' }, signal: controller.signal
          });
        if (response.status === 401) { auth.invalidateSession(); return; }
        if (response.status === 403) { void auth.bootstrap(); throw new Error('Η πρόσβαση στα πρόχειρα άλλαξε.'); }
        if (!response.ok) throw new Error('Τα πρόχειρα δεν είναι διαθέσιμα.');
        const raw: unknown = await response.json();
        if (!raw || typeof raw !== 'object' || Array.isArray(raw))
          throw new Error('Μη επαληθευμένη απόκριση.');
        const data = raw as Record<string, unknown>;
        if (data.organization_id !== organization || !Array.isArray(data.drafts) ||
            data.drafts.length > 30 || !data.drafts.every(item => validDraft(item, organization)))
          throw new Error('Η λίστα προχείρων δεν επαληθεύτηκε.');
        if (!controller.signal.aborted) drafts = data.drafts as Draft[];
      } catch {
        if (!controller.signal.aborted) error = 'Δεν φορτώθηκαν τα πρόχειρα. Δοκιμάστε ξανά.';
      } finally {
        if (!controller.signal.aborted) loading = false;
      }
    })();
    return () => controller.abort();
  });

  function canDiscard() {
    return !dirty || window.confirm('Υπάρχουν μη αποθηκευμένες αλλαγές. Να απορριφθούν;');
  }

  function newDraft() {
    if (!canWrite || saving || !canDiscard()) return;
    selectedId = null; currentVersion = null;
    form = empty(); baseline = JSON.stringify(form);
    conflict = false; error = ''; notice = ''; editorOpen = true;
  }

  function openDraft(item: Draft) {
    if (saving || !canDiscard()) return;
    selectedId = item.id; currentVersion = item.version;
    form = { ...item.article }; baseline = JSON.stringify(form);
    conflict = false; error = ''; notice = ''; editorOpen = true;
  }

  function cancel() {
    if (saving || !canDiscard()) return;
    editorOpen = false; conflict = false; error = ''; notice = '';
  }

  async function save(event: SubmitEvent) {
    event.preventDefault();
    if (!canWrite || !allowed || !editorOpen || saving || conflict) return;
    const ownerAtStart = owner, selectedAtStart = selectedId;
    const actor = $auth.session?.user.id;
    const pending: Editable = { ...form, image_url: form.image_url?.trim() || null };
    if (!pending.title.trim() || pending.title.length > 250 ||
        pending.content.length > 100000 || pending.excerpt.length > 2000 ||
        pending.category.length > 120 || pending.audience.length > 120 ||
        pending.image_url && pending.image_url.length > 2048) {
      error = 'Ελέγξτε το μέγεθος και το περιεχόμενο των πεδίων.';
      return;
    }
    const oldVersion = currentVersion;
    if (selectedAtStart && oldVersion === null) return;
    saving = true; error = ''; notice = '';
    try {
      const path = '/api/admin/website/news-drafts' +
        (selectedAtStart ? '/' + encodeURIComponent(selectedAtStart) : '') +
        '?organization_id=' + encodeURIComponent(ownerAtStart);
      const response = await fetch(path, {
        method: selectedAtStart ? 'PUT' : 'POST', credentials: 'same-origin', cache: 'no-store',
        headers: { 'Content-Type': 'application/json', 'X-Lefteria-Admin': '1', Accept: 'application/json' },
        body: JSON.stringify(selectedAtStart ?
          { expected_version: oldVersion, article: pending } : { article: pending })
      });
      if (response.status === 401) { auth.invalidateSession(); throw new Error('Η σύνδεση έληξε.'); }
      if (response.status === 403) { void auth.bootstrap(); throw new Error('Η πρόσβαση ανακλήθηκε.'); }
      if (response.status === 409) {
        conflict = true;
        throw new Error('Το πρόχειρο έχει τροποποιηθεί. Αντιγράψτε το κείμενο και ανανεώστε πριν από νέα αποθήκευση.');
      }
      if (!response.ok) throw new Error('Η αποθήκευση του προχείρου απέτυχε.');
      const result: unknown = await response.json();
      if (!validDraft(result, ownerAtStart) || !sameArticle(result.article, pending) ||
          selectedAtStart && (result.id !== selectedAtStart || result.version !== (oldVersion ?? 0) + 1) ||
          !selectedAtStart && result.version !== 1)
        throw new Error('Η επιβεβαίωση αποθήκευσης δεν επαληθεύτηκε. Ανανέωση απαιτείται.');
      if (owner !== ownerAtStart || $auth.session?.user.id !== actor || selectedId !== selectedAtStart) return;
      selectedId = result.id; currentVersion = result.version;
      form = { ...result.article }; baseline = JSON.stringify(form);
      drafts = [result, ...drafts.filter(d => d.id !== result.id)]
        .sort((a, b) => b.updated_at.localeCompare(a.updated_at)).slice(0, 30);
      notice = 'Το πρόχειρο αποθηκεύτηκε ιδιωτικά. Δεν δημοσιεύθηκε.';
    } catch (reason) {
      error = reason instanceof Error ? reason.message : 'Η αποθήκευση απέτυχε.';
    } finally {
      saving = false;
    }
  }
</script>

<section class="news-drafts" data-testid="admin-news-drafts">
  <div class="news-drafts-head">
    <div>
      <p class="admin-eyebrow">ΙΔΙΩΤΙΚΟΣ ΧΩΡΟΣ ΣΥΝΤΑΞΗΣ</p>
      <h2>ΠΡΟΧΕΙΡΑ ΑΡΘΡΩΝ</h2>
      <p>Τα πρόχειρα είναι διαθέσιμα μόνο σε εξουσιοδοτημένους διαχειριστές. Δεν εμφανίζονται στην ιστοσελίδα ή στην εφαρμογή.</p>
    </div>
    {#if canWrite}
      <button type="button" class="draft-new" onclick={newDraft} disabled={loading || saving}>+ ΝΕΟ ΠΡΟΧΕΙΡΟ</button>
    {/if}
  </div>
  {#if loading}
    <p role="status" class="drafts-feedback">Φόρτωση ιδιωτικών προχείρων…</p>
  {:else if !allowed}
    <p role="alert" class="drafts-feedback">Δεν υπάρχει πρόσβαση στα πρόχειρα αυτού του οργανισμού.</p>
  {:else}
    {#if error && !editorOpen}<p role="alert" class="drafts-feedback">{error} <button type="button" onclick={() => reload++}>Ανανέωση</button></p>{/if}
    {#if drafts.length > 0}
      <div class="draft-list" aria-label="Ιδιωτικά πρόχειρα άρθρων">
        {#each drafts as draft (draft.id)}
          <button type="button" class="draft-row" onclick={() => openDraft(draft)} disabled={saving}>
            <span><strong>{draft.article.title}</strong><small>Πρόχειρο · έκδοση {draft.version}</small></span>
            <span class="draft-open">ΠΡΟΒΟΛΗ <span aria-hidden="true">↗</span></span>
          </button>
        {/each}
      </div>
    {:else if !loading && !error}
      <p class="drafts-feedback" data-testid="admin-news-drafts-empty">Δεν υπάρχουν ιδιωτικά πρόχειρα.</p>
    {/if}
    {#if editorOpen}
      <form class="draft-form" onsubmit={save} data-testid="admin-news-draft-form">
        <div class="draft-form-head">
          <h3>{selectedId ? 'ΕΠΕΞΕΡΓΑΣΙΑ ΠΡΟΧΕΙΡΟΥ' : 'ΝΕΟ ΠΡΟΧΕΙΡΟ'}</h3>
          <button type="button" class="draft-cancel" onclick={cancel} disabled={saving}>Κλείσιμο</button>
        </div>
        <label>Τίτλος
          <input bind:value={form.title} maxlength="250" required disabled={!canWrite || saving || conflict} />
        </label>
        <label>Κείμενο (Markdown)
          <textarea bind:value={form.content} maxlength="100000" rows="9" disabled={!canWrite || saving || conflict}></textarea>
        </label>
        <label>Σύντομη περιγραφή
          <textarea bind:value={form.excerpt} maxlength="2000" rows="3" disabled={!canWrite || saving || conflict}></textarea>
        </label>
        <div class="draft-form-fields">
          <label>Κατηγορία
            <input bind:value={form.category} maxlength="120" disabled={!canWrite || saving || conflict} />
          </label>
          <label>Κοινό
            <select bind:value={form.audience} disabled={!canWrite || saving || conflict}>
              <option value="both">Όλοι</option>
              <option value="first_team">Πρώτη ομάδα</option>
              <option value="academy">Ακαδημία</option>
            </select>
          </label>
        </div>
        <label>Διεύθυνση εικόνας (εγκεκριμένη HTTPS ή /media/)
          <input type="text" maxlength="2048" value={form.image_url ?? ''}
            oninput={e => { form.image_url = e.currentTarget.value || null; }}
            disabled={!canWrite || saving || conflict} />
        </label>
        <label class="draft-featured">
          <input type="checkbox" bind:checked={form.is_featured} disabled={!canWrite || saving || conflict} />
          Προτεινόμενο όταν δημοσιευθεί
        </label>
        {#if error}<p role="alert" class="draft-error">{error}</p>{/if}
        {#if notice}<p role="status" class="draft-success">{notice}</p>{/if}
        <div class="draft-actions">
          {#if canWrite}
            <button type="submit" class="draft-save" disabled={saving || conflict || selectedId !== null && !dirty}>
              {saving ? 'Αποθήκευση…' : 'ΑΠΟΘΗΚΕΥΣΗ ΠΡΟΧΕΙΡΟΥ'}
            </button>
          {/if}
          {#if conflict}
            <button type="button" class="draft-cancel"
              onclick={() => { if (window.confirm('Οι αλλαγές που δεν αποθηκεύτηκαν θα χαθούν. Συνέχεια;')) reload++; }}>
              Ανανέωση προχείρων
            </button>
          {/if}
          <button type="button" class="draft-cancel" onclick={cancel} disabled={saving}>Άκυρο</button>
        </div>
      </form>
    {/if}
  {/if}
</section>

<style>
  .news-drafts{margin:18px 0 24px;border:1px solid #444039;border-top:3px solid #f6bb38;background:#111315;color:#fff;padding:20px}
  .news-drafts-head{display:flex;justify-content:space-between;align-items:start;gap:16px;flex-wrap:wrap}
  .news-drafts h2{margin:5px 0 8px;font:italic 900 26px/1.1 'Fira Sans Extra Condensed',sans-serif}
  .news-drafts-head p:last-child{margin:0 0 18px;font-size:13px;color:#c3c5c6;max-width:70ch}
  .draft-new,.draft-save{border:0;background:#f6bb38;color:#101010;font:900 15px/1.2 'Fira Sans Extra Condensed',sans-serif;padding:12px 17px;min-height:44px;cursor:pointer}
  .draft-new:disabled,.draft-save:disabled{opacity:.5;cursor:not-allowed}
  .drafts-feedback{padding:18px;color:#c7cbcb;font-size:13px}
  .drafts-feedback button,.draft-cancel{background:transparent;color:#f6bb38;border:1px solid #6b5a35;padding:10px 13px;min-height:40px}
  .draft-list{border-block:1px solid #3a3d3e}
  .draft-row{display:flex;align-items:center;justify-content:space-between;gap:15px;width:100%;text-align:left;border:0;border-bottom:1px solid #363839;padding:15px 6px;background:transparent;color:#fff;cursor:pointer}
  .draft-row:last-child{border-bottom:0}.draft-row:hover{background:#191919}
  .draft-row span:first-child{display:flex;flex-direction:column;gap:6px}
  .draft-row strong{font:800 18px/1.3 'Fira Sans Extra Condensed',sans-serif;overflow-wrap:anywhere}
  .draft-row small{font-size:11px;color:#abb0b1}
  .draft-open{color:#f6bb38;white-space:nowrap;font:800 13px 'Fira Sans Extra Condensed',sans-serif}
  .draft-form{display:grid;gap:14px;margin-top:18px;padding-top:16px;border-top:1px solid #665129}
  .draft-form-head{display:flex;justify-content:space-between;align-items:center;gap:10px}
  .draft-form h3{margin:0;font:italic 900 22px 'Fira Sans Extra Condensed',sans-serif}
  .draft-form label{display:flex;flex-direction:column;gap:7px;font:700 13px 'Fira Sans',sans-serif;color:#e5e7e7}
  .draft-form input:not([type=checkbox]),.draft-form textarea,.draft-form select{
    width:100%;min-height:43px;border:1px solid #595d5e;border-radius:0;padding:10px 12px;
    background:#090b0c;color:#fff;font:400 14px/1.5 'Fira Sans',sans-serif
  }
  .draft-form :is(input,textarea,select):focus-visible{outline:2px solid #f6bb38;outline-offset:2px}
  .draft-form-fields{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:13px}
  .draft-form .draft-featured{display:flex;flex-direction:row;align-items:center;gap:10px}
  .draft-actions{display:flex;align-items:center;flex-wrap:wrap;gap:10px}
  .draft-error{font-size:13px;color:#ff9c9c;margin:0}.draft-success{color:#93db9e;font-size:13px;margin:0}
  @media(max-width:650px){.news-drafts{padding:15px}.draft-form-fields{grid-template-columns:1fr}.draft-open{font-size:12px}}
</style>
