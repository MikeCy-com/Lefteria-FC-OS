<script lang="ts">
  import { getContext } from 'svelte';
  import { ADMIN_AUTH_CONTEXT, type AdminAuthController } from './controller.ts';
  import { articleLink } from '../public/content.ts';
  import type { PublicArticle } from '../public/types.ts';

  type Organization = 'lefteria_fc' | 'lefteria_fc_academy';
  type Edit = {
    title: string; content: string; excerpt: string; image_url: string | null;
    category: string; audience: string; is_featured: boolean;
  };
  type Versioned = { version: number; article: PublicArticle };
  let { owner, articleId, canWrite, onClose, onSaved }: {
    owner: Organization; articleId: string; canWrite: boolean;
    onClose: () => void; onSaved: () => void;
  } = $props();

  const auth = getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  let article = $state<PublicArticle | null>(null);
  let version = $state<number | null>(null);
  let draft = $state<Edit | null>(null);
  let baseline = $state('');
  let loading = $state(false);
  let saving = $state(false);
  let problem = $state('');
  let notice = $state('');
  let conflict = $state(false);
  let reload = $state(0);
  const dirty = $derived(draft !== null && JSON.stringify(draft) !== baseline);
  const sessionAllowed = $derived($auth.status === 'authenticated' && !!$auth.session &&
    $auth.session.user.organizations.some(org => org.id === owner) &&
    $auth.session.user.permissions.some(permission => permission === '*:*' ||
      permission === 'news:*' || permission === 'news:read'));

  function verified(value: unknown, id: string, org: Organization): value is Versioned {
    if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
    const data = value as Record<string, unknown>;
    if (!Number.isSafeInteger(data.version) || (data.version as number) < 0 ||
        !data.article || typeof data.article !== 'object' || Array.isArray(data.article)) return false;
    const article = data.article as Record<string, unknown>;
    return article.id === id && article.organization_id === org &&
      typeof article.title === 'string' && typeof article.content === 'string' &&
      typeof article.excerpt === 'string' && typeof article.category === 'string' &&
      typeof article.audience === 'string' && typeof article.is_featured === 'boolean' &&
      (article.image_url === null || typeof article.image_url === 'string');
  }

  function editable(item: PublicArticle): Edit {
    return {
      title: item.title, content: item.content, excerpt: item.excerpt,
      image_url: item.image_url ?? null,
      category: item.category, audience: item.audience, is_featured: item.is_featured
    };
  }

  $effect(() => {
    const actor = $auth.session?.user.id;
    const selected = owner, id = articleId, currentReload = reload, authorized = sessionAllowed;
    void actor; void currentReload;
    article = null; version = null; draft = null; baseline = '';
    loading = false; problem = ''; notice = ''; conflict = false;
    if (!authorized || !id || !selected) return;
    const controller = new AbortController();
    loading = true;
    void (async () => {
      try {
        const url = '/api/admin/website/news/' + encodeURIComponent(id) +
          '?organization_id=' + encodeURIComponent(selected);
        const response = await fetch(url, {
          credentials: 'same-origin', cache: 'no-store',
          headers: { Accept: 'application/json' }, signal: controller.signal
        });
        if (response.status === 401) { auth.invalidateSession(); return; }
        if (response.status === 403) { void auth.bootstrap(); throw new Error('Τα δικαιώματα πρόσβασης άλλαξαν.'); }
        if (!response.ok) throw new Error('Το άρθρο δεν είναι διαθέσιμο.');
        const data: unknown = await response.json();
        if (!verified(data, id, selected)) throw new Error('Η απόκριση του άρθρου δεν επαληθεύτηκε.');
        if (!controller.signal.aborted) {
          article = data.article;
          version = data.version;
          draft = editable(data.article);
          baseline = JSON.stringify(draft);
        }
      } catch (error) {
        if (!controller.signal.aborted)
          problem = error instanceof Error ? error.message : 'Αδύνατη φόρτωση άρθρου.';
      } finally {
        if (!controller.signal.aborted) loading = false;
      }
    })();
    return () => controller.abort();
  });

  function close() {
    if (saving || dirty && !window.confirm('Υπάρχουν μη αποθηκευμένες αλλαγές. Κλείσιμο χωρίς αποθήκευση;')) return;
    onClose();
  }

  async function save(event: SubmitEvent) {
    event.preventDefault();
    if (!draft || !article || version === null || !canWrite || !sessionAllowed ||
        saving || loading || conflict) return;
    const id = articleId, org = owner, actor = $auth.session?.user.id;
    const pending = { ...draft, image_url: draft.image_url?.trim() || null };
    if (!pending.title.trim() || pending.title.length > 250 ||
        !pending.content.trim() || pending.content.length > 100000 ||
        pending.excerpt.length > 2000 || !pending.category.trim() ||
        pending.category.length > 120 || !pending.audience.trim() ||
        pending.audience.length > 120 || pending.image_url && pending.image_url.length > 2048) {
      problem = 'Ελέγξτε το κείμενο, την κατηγορία, το κοινό και την εικόνα του άρθρου.';
      return;
    }
    saving = true; problem = ''; notice = '';
    try {
      const response = await fetch(
        '/api/admin/website/news/' + encodeURIComponent(id) +
          '?organization_id=' + encodeURIComponent(org), {
          method: 'PUT', credentials: 'same-origin', cache: 'no-store',
          headers: { 'Content-Type': 'application/json', 'X-Lefteria-Admin': '1', Accept: 'application/json' },
          body: JSON.stringify({ expected_version: version, article: pending })
        });
      if (response.status === 401) { auth.invalidateSession(); throw new Error('Η σύνδεση έληξε.'); }
      if (response.status === 403) { void auth.bootstrap(); throw new Error('Η άδεια επεξεργασίας ανακλήθηκε.'); }
      if (response.status === 409) {
        conflict = true;
        throw new Error('Το άρθρο άλλαξε από άλλον διαχειριστή. Αντιγράψτε το κείμενό σας και ανανεώστε πριν από νέα αποθήκευση.');
      }
      if (!response.ok) throw new Error('Η αποθήκευση δεν ολοκληρώθηκε. Ελέγξτε τα πεδία.');
      const result: unknown = await response.json();
      if (!verified(result, id, org) || result.version <= version ||
          result.article.title !== pending.title || result.article.content !== pending.content ||
          result.article.category !== pending.category || result.article.excerpt !== pending.excerpt ||
          result.article.image_url !== pending.image_url ||
          result.article.audience !== pending.audience || result.article.is_featured !== pending.is_featured)
        throw new Error('Η επιβεβαίωση αποθήκευσης δεν επαληθεύτηκε. Ανανέωση απαιτείται.');
      if (owner !== org || articleId !== id || $auth.session?.user.id !== actor) return;
      article = result.article;
      version = result.version;
      draft = editable(result.article);
      baseline = JSON.stringify(draft);
      notice = 'Το δημοσιευμένο άρθρο ενημερώθηκε.';
      onSaved();
    } catch (error) {
      problem = error instanceof Error ? error.message : 'Η αποθήκευση απέτυχε.';
    } finally {
      saving = false;
    }
  }
</script>

<section class="admin-news-editor admin-panel" data-testid="admin-news-editor" aria-label="Επεξεργασία δημοσιευμένου άρθρου">
  <header class="admin-news-editor-top">
    <div>
      <p class="admin-eyebrow">ΙΣΤΟΣΕΛΙΔΑ · ΑΡΘΡΟ</p>
      <h2>Προβολή & επεξεργασία άρθρου</h2>
      <p class="admin-muted">Κάθε αποθήκευση ενημερώνει το ήδη δημοσιευμένο άρθρο. Δεν δημιουργείται νέο άρθρο ούτε αλλάζει η ημερομηνία δημοσίευσης.</p>
    </div>
    <button class="admin-secondary" type="button" onclick={close} disabled={saving}>Κλείσιμο</button>
  </header>
  {#if loading}
    <p role="status">Φόρτωση άρθρου…</p>
  {:else if !sessionAllowed}
    <p role="alert">Δεν υπάρχει εξουσιοδοτημένη πρόσβαση σε αυτό το άρθρο.</p>
  {:else if draft && article}
    <div class="admin-news-editor-meta">
      <span>Οργανισμός: {owner === 'lefteria_fc' ? 'Λευτεριά FC' : 'Λευτεριά FC Academy'}</span>
      <span>Έκδοση: {version}</span>
      <a href={articleLink(article, owner)} target="_blank" rel="noopener noreferrer">Προβολή στην ιστοσελίδα ↗</a>
    </div>
    <form onsubmit={save} data-testid="admin-news-edit-form">
      <label>Τίτλος άρθρου
        <input required maxlength="250" bind:value={draft.title} disabled={!canWrite || saving || conflict} />
      </label>
      <label>Περιγραφή / σύνοψη
        <textarea rows="3" maxlength="2000" bind:value={draft.excerpt}
          disabled={!canWrite || saving || conflict}></textarea>
      </label>
      <label>Κυρίως κείμενο (Markdown)
        <textarea rows="11" maxlength="100000" bind:value={draft.content} required
          disabled={!canWrite || saving || conflict}></textarea>
      </label>
      <div class="admin-news-editor-columns">
        <label>Κατηγορία
          <input required maxlength="120" bind:value={draft.category}
            disabled={!canWrite || saving || conflict} />
        </label>
        <label>Κοινό
          <input required maxlength="120" bind:value={draft.audience}
            disabled={!canWrite || saving || conflict} />
        </label>
      </div>
      <label>URL δημοσιευμένης εικόνας
        <input type="url" maxlength="2048" placeholder="https://…" value={draft.image_url ?? ''}
          oninput={event => { if (draft) draft.image_url = event.currentTarget.value || null; }}
          disabled={!canWrite || saving || conflict} />
      </label>
      <label class="admin-news-editor-check">
        <input type="checkbox" bind:checked={draft.is_featured} disabled={!canWrite || saving || conflict} />
        Προτεινόμενο άρθρο
      </label>
      {#if !canWrite}<p>Ο λογαριασμός έχει δικαίωμα ανάγνωσης, όχι επεξεργασίας.</p>{/if}
      {#if notice}<p role="status" class="admin-news-editor-success">{notice}</p>{/if}
      {#if problem}<p role="alert" class="admin-news-editor-error">{problem}</p>{/if}
      <div class="admin-news-editor-actions">
        {#if canWrite}
          <button class="admin-primary" type="submit" disabled={!dirty || saving || conflict}>
            {saving ? 'Αποθήκευση…' : 'Αποθήκευση αλλαγών'}
          </button>
        {/if}
        {#if conflict}
          <button class="admin-secondary" type="button"
            onclick={() => { if (window.confirm('Η ανανέωση θα απορρίψει τις τοπικές αλλαγές. Συνέχεια;')) reload++; }}>
            Ανανέωση άρθρου
          </button>
        {/if}
        <button class="admin-secondary" type="button" onclick={close} disabled={saving}>Άκυρο</button>
      </div>
    </form>
  {:else}
    <p role="alert">{problem || 'Δεν είναι διαθέσιμα επαληθευμένα στοιχεία άρθρου.'}</p>
    <button type="button" class="admin-secondary" onclick={() => reload++}>Δοκιμή ξανά</button>
  {/if}
</section>

<style>
  .admin-news-editor{margin:12px 0 20px;padding:22px;border:1px solid #5c4a26;border-top:3px solid #f6bb38;background:#121416;color:#fff}
  .admin-news-editor-top{display:flex;align-items:start;justify-content:space-between;gap:15px;flex-wrap:wrap}
  .admin-news-editor-top h2{font:italic 900 26px/1.15 'Fira Sans Extra Condensed',sans-serif;margin:7px 0 10px}
  .admin-news-editor-top p{font-size:13px;max-width:700px}
  .admin-news-editor-meta{display:flex;flex-wrap:wrap;gap:14px;border-block:1px solid #3c3d3e;margin:14px 0;padding:10px 0;font-size:12px;color:#bfc2c3}
  .admin-news-editor-meta a{color:#f6bb38}
  .admin-news-editor form{display:grid;gap:15px;margin-top:18px}
  .admin-news-editor form label{display:flex;flex-direction:column;gap:6px;font-size:13px;font-weight:700;color:#e2e4e5}
  .admin-news-editor input:not([type=checkbox]),.admin-news-editor textarea{
    width:100%;background:#0e0f10;border:1px solid #55595a;color:white;padding:11px;font:400 14px/1.5 'Fira Sans',sans-serif
  }
  .admin-news-editor input:focus-visible,.admin-news-editor textarea:focus-visible{outline:2px solid #f6bb38;outline-offset:2px}
  .admin-news-editor-columns{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:14px}
  .admin-news-editor form .admin-news-editor-check{display:flex;flex-direction:row;align-items:center;gap:9px}
  .admin-news-editor-actions{display:flex;flex-wrap:wrap;gap:12px;margin-top:7px}
  .admin-news-editor-success{color:#8fd7a2;font-size:13px}
  .admin-news-editor-error{color:#ff9797;font-size:13px}
  @media(max-width:650px){.admin-news-editor{padding:15px}.admin-news-editor-columns{grid-template-columns:1fr}}
</style>
