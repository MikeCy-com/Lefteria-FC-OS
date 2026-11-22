<script lang="ts">
  import { CLUB_SOCIAL_FIELDS, CLUB_TEXT_FIELDS, type ClubEditorController } from './club-controller.ts';
  let { controller }: { controller: ClubEditorController } = $props();
  const fieldGroups = [
    { id: 'identity', title: 'Ταυτότητα συλλόγου', keys: ['name', 'greek_name', 'motto'] },
    { id: 'contact', title: 'Δημόσια επικοινωνία', keys: ['city', 'country', 'address', 'email', 'phone', 'website'] },
    { id: 'images', title: 'Λογότυπο & εικόνες', keys: ['logo_url', 'stadium_image_url'] }
  ];
</script>

<div class="club-editor" data-testid="admin-club-editor">
  <div class="club-content">
    <p class="admin-eyebrow">ΡΥΘΜΙΣΕΙΣ ΣΥΛΛΟΓΟΥ · ΤΑΥΤΟΤΗΤΑ</p><h1>Ταυτότητα & επικοινωνία</h1>
    <p class="admin-muted">Εδώ διαχειρίζεστε το λογότυπο, τα τηλέφωνα, τα email, τη διεύθυνση και τα κοινωνικά δίκτυα
      κάθε οργανισμού. Η Πρώτη Ομάδα και η Ακαδημία έχουν ανεξάρτητα προφίλ.</p>
    <a class="club-back-settings" href="/admin/settings" data-testid="club-back-to-settings">← Ρυθμίσεις συλλόγου</a>
    <div class="club-selector">
      {#if $controller.organizations.length > 1}
        <label for="club-profile-target">ΔΗΜΟΣΙΟ ΠΡΟΦΙΛ ΓΙΑ
          <select id="club-profile-target" data-testid="club-profile-target"
            value={$controller.organizationId ?? ''} disabled={$controller.saving}
            onchange={async e => {
              const input = e.currentTarget;
              const accepted = await controller.selectOrganization(input.value);
              if (!accepted) input.value = $controller.organizationId ?? '';
            }}>
            {#each $controller.organizations as org (org.id)}
              <option value={org.id}>{org.id === 'lefteria_fc' ? 'Λευτεριά FC' : 'Λευτεριά FC Academy'}</option>
            {/each}
          </select>
        </label>
      {:else}
        <span class="admin-organization-context-label" data-testid="club-organization">
          {$controller.organizations[0]?.name ?? 'Δεν υπάρχει πρόσβαση σε προφίλ'}
        </span>
      {/if}
    </div>
    {#if $controller.status === 'loading' || $controller.status === 'idle'}<p role="status">Φόρτωση στοιχείων συλλόγου…</p>
    {:else if $controller.status !== 'ready'}
      <div class="admin-panel"><p role="alert" class="admin-error">{$controller.error}</p>
        {#if $controller.status === 'unavailable'}<button class="admin-secondary" type="button" onclick={() => void controller.load()}>Δοκιμή ξανά</button>{/if}
      </div>
    {:else if $controller.draft}
      {#if $controller.error}<p class="admin-error" role="alert" data-testid="club-error">{$controller.error}</p>{/if}
      {#if $controller.success}<p class="club-success" role="status" data-testid="club-saved">{$controller.success}</p>{/if}
      {#if $controller.conflict}
        <button class="admin-secondary" type="button" onclick={() => void controller.load()} data-testid="club-reload-conflict">Φόρτωση τελευταίας έκδοσης και απόρριψη αλλαγών</button>
      {/if}
      <div class="club-toolbar">
        {#if !$controller.editing && $controller.canWrite}<button class="admin-primary" type="button" disabled={$controller.conflict} onclick={controller.edit} data-testid="club-edit">Επεξεργασία στοιχείων</button>{/if}
        {#if !$controller.canWrite}<p class="admin-muted">Έχετε δικαίωμα προβολής των στοιχείων.</p>{/if}
        {#if !$controller.editing && !$controller.conflict}<button class="admin-secondary" type="button" onclick={() => void controller.load()}>Ανανέωση στοιχείων</button>{/if}
        {#if $controller.dirty}<p role="status" class="club-dirty">Υπάρχουν μη αποθηκευμένες αλλαγές.</p>{/if}
        <a href={`/about?organization_id=${$controller.organizationId}`} target="_blank" rel="noreferrer">Προβολή δημόσιας σελίδας ↗</a>
      </div>
      <nav class="club-sections" aria-label="Ενότητες προφίλ"><a href="#club-identity">Ταυτότητα</a><a href="#club-contact">Επικοινωνία</a><a href="#club-images">Εικόνες</a><a href="#club-story">Ιστορία</a><a href="#club-social">Κοινωνικά δίκτυα</a></nav>
      <form onsubmit={(event) => { event.preventDefault(); void controller.save(); }} aria-busy={$controller.saving}>
        {#each fieldGroups as group (group.id)}
        <section id={`club-${group.id}`} class="admin-panel club-fields" aria-labelledby={`club-${group.id}-title`}>
          <h2 id={`club-${group.id}-title`} class="club-wide">{group.title}</h2>
          {#each CLUB_TEXT_FIELDS.filter(field => group.keys.includes(field.key)) as field}
            <div class="club-field"><label for={`club-${field.key}`}>{field.label}{field.key === 'name' ? ' *' : ''}</label>
              <input id={`club-${field.key}`} type={field.key === 'email' ? 'email' : field.key === 'phone' ? 'tel' : field.key === 'website' ? 'url' : 'text'} value={$controller.draft[field.key]} maxlength={field.limit} required={field.key === 'name'}
                readonly={!$controller.editing} disabled={$controller.saving} autocomplete="off"
                oninput={(event) => controller.change(field.key, event.currentTarget.value)} data-testid={`club-${field.key}`} />
            </div>
          {/each}
          {#if group.id === 'identity'}<div class="club-field"><label for="club-founded">Έτος ίδρυσης</label>
            <input id="club-founded" type="number" min="1800" max="2200" step="1" value={$controller.draft.founded}
              readonly={!$controller.editing} disabled={$controller.saving} oninput={(event) => controller.change('founded', event.currentTarget.value)} />
          </div>{/if}
          {#if group.id === 'images'}<p class="admin-muted club-wide">Για εικόνες χρησιμοποιήστε ασφαλή σύνδεσμο HTTPS ή ήδη διαθέσιμη δημόσια εικόνα. Δεν γίνεται μεταφόρτωση αρχείου σε αυτή τη φόρμα.</p>{/if}
        </section>{/each}
        <section id="club-story" class="admin-panel club-fields" aria-labelledby="club-story-title">
          <h2 id="club-story-title" class="club-wide">Περιγραφή & ιστορία</h2>
          <div class="club-field club-wide"><label for="club-description">Περιγραφή συλλόγου</label>
            <textarea id="club-description" rows="6" maxlength="20000" value={$controller.draft.description} readonly={!$controller.editing}
              disabled={$controller.saving} oninput={(event) => controller.change('description', event.currentTarget.value)}></textarea>
          </div>
          <div class="club-field club-wide"><label for="club-history">Ιστορία συλλόγου</label>
            <textarea id="club-history" rows="14" maxlength="100000" value={$controller.draft.history} readonly={!$controller.editing}
              disabled={$controller.saving} oninput={(event) => controller.change('history', event.currentTarget.value)} data-testid="club-history"></textarea>
          </div>
        </section>
        <section id="club-social" class="admin-panel club-fields" aria-labelledby="club-social-title"><h2 id="club-social-title" class="club-wide">Κοινωνικά δίκτυα</h2>
          {#each CLUB_SOCIAL_FIELDS as key}<div class="club-field"><label for={`club-social-${key}`}>{key === 'twitter' ? 'X / Twitter' : key[0].toUpperCase() + key.slice(1)} (HTTPS)</label>
            <input id={`club-social-${key}`} type="url" pattern="https://.*" maxlength="2048" value={$controller.draft.social_links[key]}
              readonly={!$controller.editing} disabled={$controller.saving} oninput={(event) => controller.changeSocial(key, event.currentTarget.value)} />
          </div>{/each}
        </section>
        {#if $controller.editing && $controller.canWrite}<div class="club-save-actions">
          <button type="submit" class="admin-primary" disabled={!$controller.dirty || $controller.saving || $controller.conflict} data-testid="club-save">{$controller.saving ? 'Αποθήκευση…' : 'Αποθήκευση αλλαγών'}</button>
          <button type="button" class="admin-secondary" disabled={$controller.saving} onclick={controller.cancel}>Ακύρωση αλλαγών</button>
        </div>{/if}
      </form>
    {/if}
  </div>
</div>

<style>
  .club-content { width:100%;min-width:0; }
  .club-back-settings {display:inline-flex;align-items:center;min-height:40px;margin-top:12px;color:#eac168;text-decoration:none;font-size:12px}
  .club-back-settings:focus-visible{outline:2px solid #f8c65a;outline-offset:2px}
  .club-selector { display:grid; gap:8px; max-width:400px; margin:24px 0; }
  .club-selector label { font-weight:600; }
  .club-toolbar { display:flex; align-items:center; flex-wrap:wrap; gap:16px; margin:24px 0; }
  .club-toolbar a { margin-left:auto; }
  .club-sections { display:flex; flex-wrap:wrap; gap:8px 24px; padding:16px 0; border-block:1px solid #ffffff24; }
  .club-sections a { min-height:44px; display:inline-flex; align-items:center; font-size:14px; text-decoration:none; }
  .club-fields { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:20px; }
  .club-field { display:grid; gap:8px; align-content:start; }
  .club-field label { margin:0; }
  .club-wide { grid-column:1/-1; }
  textarea { width:100%; padding:12px; color:#fff; background:#090c0b; border:1px solid #5a6056; border-radius:7px; font:inherit; resize:vertical; }
  input[readonly],textarea[readonly] { color:#cdd3ca; border-color:#353e32; }
  .club-save-actions { position:sticky; bottom:0; z-index:2; display:flex; flex-wrap:wrap; gap:12px; margin:16px 0 0; padding:16px 0; background:#0b0b0bf5; border-top:1px solid #ffffff24; }
  .club-success { padding:12px 16px; border:1px solid #6e9461; background:#1c3722; border-radius:7px; }
  .club-dirty { color:#f7c56d; margin:0; }
  @media(max-width:650px) { .club-fields { grid-template-columns:minmax(0,1fr); } .club-toolbar a { margin-left:0; } }
</style>
