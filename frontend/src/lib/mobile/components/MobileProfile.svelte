<script lang="ts">
  import type { components } from '../../api/schema';
  import type { FavoriteTeam } from '../controller.ts';
  import './mobile-theme.css';

  interface Props {
    user: components['schemas']['MobileUserDto'];
    busy?: boolean;
    error?: string | null;
    onSaveFavoriteTeam: (value: FavoriteTeam) => boolean | Promise<boolean>;
    onLogout: () => void;
  }

  let { user, busy = false, error = null, onSaveFavoriteTeam, onLogout }: Props = $props();
  const id = $props.id();
  let draft = $state('');
  let submitting = $state(false);
  let saved = $state(false);
  let avatarFailed = $state(false);
  const identityId = $derived(user.id);
  const savedPreference = $derived(user.favorite_team ?? null);
  const avatar = $derived(user.avatar_url);
  const initial = $derived(user.name.trim().slice(0, 1).toLocaleUpperCase('el-GR') || '?');
  const isBusy = $derived(busy || submitting);
  const unknownPreference = $derived(savedPreference !== null && savedPreference !== 'first_team' && savedPreference !== 'academy' && savedPreference !== 'both');
  const changed = $derived((draft === '' ? null : draft) !== savedPreference);

  $effect(() => {
    void identityId;
    draft = savedPreference === 'first_team' || savedPreference === 'academy' || savedPreference === 'both' ? savedPreference : '';
    saved = false;
  });
  $effect(() => { void avatar; avatarFailed = false; });

  async function save(event: SubmitEvent) {
    event.preventDefault();
    if (isBusy || !changed) return;
    const selection = draft === 'first_team' || draft === 'academy' || draft === 'both' ? draft : null;
    saved = false;
    submitting = true;
    try { saved = await onSaveFavoriteTeam(selection); }
    finally { submitting = false; }
  }
</script>

<section class="lf-mobile-theme profile" aria-labelledby={`${id}-title`} aria-busy={isBusy} data-testid="fan-profile">
  <header class="profile-heading">
    <div class="avatar" aria-hidden="true">
      {#if avatar && !avatarFailed}<img src={avatar} alt="" onerror={() => { avatarFailed = true; }} />{:else}<span>{initial}</span>{/if}
    </div>
    <div><p>Το προφίλ μου</p><h1 id={`${id}-title`}>{user.name}</h1></div>
  </header>

  <dl class="identity-details">
    <div><dt>Τηλέφωνο</dt><dd>{user.phone}</dd></div>
    {#if user.birthday}<div><dt>Ημερομηνία γέννησης</dt><dd>{user.birthday}</dd></div>{/if}
  </dl>

  <form onsubmit={save} aria-labelledby={`${id}-preference-title`} data-testid="favorite-team-form">
    <h2 id={`${id}-preference-title`}>Η ομάδα που υποστηρίζω</h2>
    <p id={`${id}-preference-help`}>Διάλεξε την προτίμησή σου για την πρώτη ομάδα και την Ακαδημία.</p>
    {#if unknownPreference}<p class="hint">Η αποθηκευμένη επιλογή δεν είναι διαθέσιμη. Μπορείς να επιλέξεις νέα προτίμηση.</p>{/if}
    <label for={`${id}-favorite-team`}>Προτίμηση ομάδας</label>
    <select id={`${id}-favorite-team`} bind:value={draft} disabled={isBusy}
      onchange={() => { saved = false; }} aria-describedby={`${id}-preference-help`} data-testid="favorite-team-select">
      <option value="">Χωρίς προτίμηση</option>
      <option value="first_team">Πρώτη ομάδα</option>
      <option value="academy">Ακαδημία</option>
      <option value="both">Όλη η Λευτεριά</option>
    </select>
    {#if error}<p class="error" role="alert" data-testid="favorite-team-error">{error}</p>{/if}
    {#if saved}<p class="success" role="status" data-testid="favorite-team-saved">Η προτίμησή σου αποθηκεύτηκε.</p>{/if}
    <button type="submit" class="primary" disabled={isBusy || !changed} data-testid="favorite-team-save">{isBusy ? 'Αποθήκευση…' : 'Αποθήκευση προτίμησης'}</button>
  </form>

  <div class="pending-feature" data-testid="profile-edit-under-development">
    <h2>Φωτογραφία και επεξεργασία στοιχείων</h2><p>Under Developments</p>
  </div>
  <button class="logout" type="button" disabled={isBusy} onclick={onLogout} data-testid="profile-logout">Αποσύνδεση</button>
</section>

<style>
  .profile { padding:20px; border:1px solid var(--lf-line); border-radius:8px; background:linear-gradient(135deg,#17191a,#101112); }
  .profile-heading { display:flex; align-items:center; gap:15px; margin-bottom:22px; }
  .profile-heading > div:last-child { min-width:0; }
  .profile-heading p { margin:0 0 4px; color:var(--lf-gold-light); font-size:12px; }
  h1 { margin:0; font-size:28px; line-height:1.15; overflow-wrap:anywhere; }
  .avatar { display:grid; place-items:center; flex:0 0 auto; width:62px; height:62px; overflow:hidden; border:1px solid #806127; border-radius:50%; background:#1c2022; color:var(--lf-gold-light); font-family:var(--lf-heading); font-size:29px; }
  .avatar img { width:100%; height:100%; object-fit:cover; }
  .identity-details { margin:0 0 24px; }
  .identity-details > div { padding:11px 0; border-bottom:1px solid var(--lf-line); }
  dt { color:var(--lf-muted); font-size:12px; }
  dd { margin:4px 0 0; color:#e5e8e9; overflow-wrap:anywhere; }
  h2 { margin:0 0 8px; font-size:22px; line-height:1.2; }
  form > p { margin:0 0 16px; color:var(--lf-muted); font-size:12px; line-height:1.5; }
  form .hint { color:#e0c58d; }
  label { display:block; margin:15px 0 7px; color:#c3c5c7; font-size:13px; }
  select { width:100%; min-height:48px; padding:11px 12px; border:1px solid #383c3e; border-radius:6px; background:#151718; color:#fff; }
  button { width:100%; min-height:46px; padding:11px 14px; border-radius:6px; cursor:pointer; }
  .primary { margin-top:16px; border:1px solid var(--lf-gold); background:linear-gradient(110deg,#ffcd47,#f5a623); color:#090909; font-weight:600; }
  .primary:hover:not(:disabled) { background:var(--lf-gold-light); }
  .primary:disabled { cursor:default; }
  form .error { margin:14px 0 0; padding:11px; border:1px solid #ef444455; border-radius:6px; background:#ef444411; color:#fca5a5; font-size:13px; overflow-wrap:anywhere; }
  form .success { margin:13px 0 0; color:#a4deb2; font-size:13px; }
  .pending-feature { margin:25px 0 20px; padding-top:20px; border-top:1px solid var(--lf-line); }
  .pending-feature h2 { color:#c8cdcf; font-size:19px; }
  .pending-feature p { margin:6px 0 0; color:var(--lf-muted); font-size:13px; }
  .logout { border:1px solid #383c3e; background:#151718; color:var(--lf-gold-light); }
</style>
