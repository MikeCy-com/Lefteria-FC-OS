<script lang="ts">
  import { tick } from 'svelte';
  import type { components } from '../../api/schema';
  import type { FavoriteTeam, MobileProfileUpdate } from '../controller.ts';
  import './mobile-theme.css';

  interface Props {
    user: components['schemas']['MobileUserDto'];
    busy?: boolean;
    error?: string | null;
    onSaveFavoriteTeam: (value: FavoriteTeam) => boolean | Promise<boolean>;
    onSaveProfile: (changes: MobileProfileUpdate) => boolean | Promise<boolean>;
    onLogout: () => void;
  }

  let { user, busy = false, error = null, onSaveFavoriteTeam, onSaveProfile, onLogout }: Props = $props();
  const id = $props.id();
  let draft = $state('');
  let submitting = $state(false);
  let saved = $state(false);
  let avatarFailed = $state(false);
  let editor = $state<'details' | 'notifications' | null>(null);
  let profileSaved = $state(false);
  let details = $state({ firstName: '', lastName: '', birthday: '', email: '' });
  let notificationDraft = $state<Record<string, boolean>>({});
  let firstNameInput = $state<HTMLInputElement>();
  let notificationHeading = $state<HTMLHeadingElement>();
  let profileHeading = $state<HTMLHeadingElement>();
  let detailsTrigger = $state<HTMLButtonElement>();
  let notificationsTrigger = $state<HTMLButtonElement>();
  // Keys/defaults come from pinned notification_center.py and GameHub.jsx.
  const notificationGroups = [
    { label: 'Λευτεριά FC', options: [
      { key: 'events', label: 'Εκδηλώσεις' }, { key: 'training', label: 'Προπονήσεις' },
      { key: 'match_reminders', label: 'Υπενθυμίσεις αγώνων' }, { key: 'messages', label: 'Μηνύματα' },
      { key: 'news', label: 'Νέα' }, { key: 'administrative', label: 'Ενημερώσεις συλλόγου' },
      { key: 'goals_live', label: 'Ζωντανές ενημερώσεις γκολ' }, { key: 'reels', label: 'Στιγμές' },
      { key: 'shop_offers', label: 'Προσφορές καταστήματος' }
    ] },
    { label: 'Κοινότητα', options: [
      { key: 'social', label: 'Ενημερώσεις κοινότητας' }, { key: 'social_comments', label: 'Σχόλια' },
      { key: 'social_reactions', label: 'Αντιδράσεις' }, { key: 'social_mentions', label: 'Αναφορές στο όνομά μου' },
      { key: 'social_follows', label: 'Ακολουθήσεις' }, { key: 'social_friendships', label: 'Αιτήματα φιλίας' },
      { key: 'social_messages', label: 'Μηνύματα κοινότητας' }, { key: 'social_moderation', label: 'Ενημερώσεις εποπτείας' },
      { key: 'social_communities', label: 'Ομάδες κοινότητας' }
    ] },
    { label: 'Παιχνίδια', options: [{ key: 'game_hub_giveaways', label: 'Διαγωνισμοί Game Hub' }] }
  ] as const;
  const identityId = $derived(user.id);
  const savedPreference = $derived(user.favorite_team ?? null);
  const avatar = $derived(user.avatar_url);
  const initial = $derived(user.name.trim().slice(0, 1).toLocaleUpperCase('el-GR') || '?');
  const isBusy = $derived(busy || submitting);
  const unknownPreference = $derived(savedPreference !== null && savedPreference !== 'first_team' && savedPreference !== 'academy' && savedPreference !== 'both');
  const changed = $derived((draft === '' ? null : draft) !== savedPreference);
  const updatedAt = $derived(user.updated_at && !Number.isNaN(Date.parse(user.updated_at))
    ? new Intl.DateTimeFormat('el-GR', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(user.updated_at)) : null);
  const notificationChanged = $derived(notificationGroups.some((group) => group.options.some((option) =>
    notificationDraft[option.key] !== (user.notification_prefs?.[option.key] ?? option.key !== 'game_hub_giveaways'))));

  $effect(() => {
    void identityId;
    draft = savedPreference === 'first_team' || savedPreference === 'academy' || savedPreference === 'both' ? savedPreference : '';
    saved = false;
  });
  $effect(() => { void avatar; avatarFailed = false; });
  $effect(() => { void identityId; editor = null; profileSaved = false; });

  function openDetails() {
    details = { firstName: user.first_name ?? '', lastName: user.last_name ?? '', birthday: user.birthday ?? '', email: user.email ?? '' };
    profileSaved = false;
    editor = 'details';
    void tick().then(() => firstNameInput?.focus());
  }

  function openNotifications() {
    notificationDraft = Object.fromEntries(Object.entries(user.notification_prefs ?? {}).filter(([, value]) => typeof value === 'boolean'));
    for (const group of notificationGroups) for (const option of group.options) {
      notificationDraft[option.key] = user.notification_prefs?.[option.key] ?? option.key !== 'game_hub_giveaways';
    }
    profileSaved = false;
    editor = 'notifications';
    void tick().then(() => notificationHeading?.focus());
  }

  function cancelEditor() {
    if (isBusy) return;
    const previous = editor;
    editor = null;
    void tick().then(() => (previous === 'details' ? detailsTrigger : notificationsTrigger)?.focus());
  }

  async function saveProfile(event: SubmitEvent) {
    event.preventDefault();
    if (isBusy) return;
    const changes: MobileProfileUpdate = editor === 'details' ? {
      first_name: details.firstName.trim(), last_name: details.lastName.trim(), birthday: details.birthday,
      email: details.email.trim()
    } : { notification_prefs: { ...notificationDraft } };
    submitting = true;
    profileSaved = false;
    saved = false;
    try {
      if (await onSaveProfile(changes)) {
        editor = null;
        profileSaved = true;
        void tick().then(() => profileHeading?.focus());
      }
    } finally { submitting = false; }
  }

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
    <div><p>Το προφίλ μου</p><h1 bind:this={profileHeading} tabindex="-1" id={`${id}-title`}>{user.name}</h1></div>
  </header>

  <dl class="identity-details">
    <div><dt>Τηλέφωνο</dt><dd>{user.phone}</dd></div>
    {#if user.birthday}<div><dt>Ημερομηνία γέννησης</dt><dd>{user.birthday}</dd></div>{/if}
    {#if user.email}<div><dt>Email</dt><dd>{user.email}</dd></div>{/if}
    {#if updatedAt && user.updated_at}<div><dt>Τελευταία ενημέρωση</dt><dd><time datetime={user.updated_at}>{updatedAt}</time></dd></div>{/if}
  </dl>

  {#if error}<p class="feedback error" role="alert" data-testid="profile-error">{error}</p>{/if}
  {#if profileSaved}<p class="feedback success" role="status" data-testid="profile-saved">Οι αλλαγές σου αποθηκεύτηκαν.</p>{/if}

  {#if editor === 'details'}
    <form class="profile-editor" onsubmit={saveProfile} aria-labelledby={`${id}-edit-title`} data-testid="profile-details-form">
      <h2 id={`${id}-edit-title`}>Επεξεργασία προφίλ</h2>
      <fieldset disabled={isBusy}>
        <label for={`${id}-first-name`}>Όνομα</label>
        <input bind:this={firstNameInput} id={`${id}-first-name`} bind:value={details.firstName} autocomplete="given-name" maxlength="100" required data-testid="profile-first-name" />
        <label for={`${id}-last-name`}>Επώνυμο</label>
        <input id={`${id}-last-name`} bind:value={details.lastName} autocomplete="family-name" maxlength="100" required data-testid="profile-last-name" />
        <label for={`${id}-birthday`}>Ημερομηνία γέννησης</label>
        <input id={`${id}-birthday`} type="date" bind:value={details.birthday} autocomplete="bday" required data-testid="profile-birthday" />
        <label for={`${id}-email`}>Email <span class="optional">(προαιρετικό)</span></label>
        <input id={`${id}-email`} type="email" bind:value={details.email} autocomplete="email" maxlength="254" data-testid="profile-email" />
        <label for={`${id}-phone`}>Τηλέφωνο</label>
        <input id={`${id}-phone`} type="tel" value={user.phone} readonly aria-describedby={`${id}-phone-help`} data-testid="profile-phone-locked" />
        <p class="hint" id={`${id}-phone-help`}>Το τηλέφωνο σύνδεσης δεν αλλάζει από το προφίλ.</p>
        <div class="form-actions">
          <button type="button" class="secondary" onclick={cancelEditor} data-testid="profile-details-cancel">Ακύρωση</button>
          <button type="submit" class="primary" data-testid="profile-details-save">{isBusy ? 'Αποθήκευση…' : 'Αποθήκευση'}</button>
        </div>
      </fieldset>
    </form>
  {:else if editor === 'notifications'}
    <form class="profile-editor" onsubmit={saveProfile} aria-labelledby={`${id}-notifications-title`} data-testid="profile-notifications-form">
      <h2 bind:this={notificationHeading} tabindex="-1" id={`${id}-notifications-title`}>Προτιμήσεις ειδοποιήσεων</h2>
      <fieldset disabled={isBusy}>
        {#each notificationGroups as group (group.label)}
          <fieldset class="notification-group"><legend>{group.label}</legend>
            {#each group.options as option (option.key)}
              <label class="checkbox-row" for={`${id}-${option.key}`}>
                <span>{option.label}</span><input id={`${id}-${option.key}`} type="checkbox" bind:checked={notificationDraft[option.key]} data-testid={`notification-pref-${option.key}`} />
              </label>
            {/each}
          </fieldset>
        {/each}
        <div class="form-actions">
          <button type="button" class="secondary" onclick={cancelEditor} data-testid="profile-notifications-cancel">Ακύρωση</button>
          <button type="submit" class="primary" disabled={!notificationChanged} data-testid="profile-notifications-save">{isBusy ? 'Αποθήκευση…' : 'Αποθήκευση'}</button>
        </div>
      </fieldset>
    </form>
  {:else}
    <div class="profile-actions">
      <button bind:this={detailsTrigger} type="button" class="secondary" disabled={isBusy} onclick={openDetails} data-testid="profile-edit-details">Επεξεργασία στοιχείων</button>
      <button bind:this={notificationsTrigger} type="button" class="secondary" disabled={isBusy} onclick={openNotifications} data-testid="profile-edit-notifications">Προτιμήσεις ειδοποιήσεων</button>
    </div>
  {/if}

  <form class="preference-form" onsubmit={save} aria-labelledby={`${id}-preference-title`} data-testid="favorite-team-form">
    <h2 id={`${id}-preference-title`}>Η ομάδα που υποστηρίζω</h2>
    <p id={`${id}-preference-help`}>Διάλεξε την προτίμησή σου για την πρώτη ομάδα και την Ακαδημία.</p>
    {#if unknownPreference}<p class="hint">Η αποθηκευμένη επιλογή δεν είναι διαθέσιμη. Μπορείς να επιλέξεις νέα προτίμηση.</p>{/if}
    <label for={`${id}-favorite-team`}>Προτίμηση ομάδας</label>
    <select id={`${id}-favorite-team`} bind:value={draft} disabled={isBusy || editor !== null}
      onchange={() => { saved = false; }} aria-describedby={`${id}-preference-help`} data-testid="favorite-team-select">
      <option value="">Χωρίς προτίμηση</option>
      <option value="first_team">Πρώτη ομάδα</option>
      <option value="academy">Ακαδημία</option>
      <option value="both">Όλη η Λευτεριά</option>
    </select>
    {#if saved}<p class="success" role="status" data-testid="favorite-team-saved">Η προτίμησή σου αποθηκεύτηκε.</p>{/if}
    <button type="submit" class="primary" disabled={isBusy || !changed || editor !== null} data-testid="favorite-team-save">{isBusy ? 'Αποθήκευση…' : 'Αποθήκευση προτίμησης'}</button>
  </form>

  <div class="pending-feature" data-testid="profile-edit-under-development">
    <h2>Φωτογραφία και αγαπημένος παίκτης</h2><p>Under Developments</p>
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
  input,select { width:100%; min-height:48px; padding:11px 12px; border:1px solid #383c3e; border-radius:6px; background:#151718; color:#fff; }
  input[readonly] { color:var(--lf-muted); }
  fieldset { min-width:0; padding:0; margin:0; border:0; }
  .profile-actions { display:grid; gap:10px; margin-bottom:25px; }
  .profile-editor { margin-bottom:25px; padding:18px 0; border-top:1px solid var(--lf-line); border-bottom:1px solid var(--lf-line); }
  .form-actions { display:grid; grid-template-columns:1fr 1fr; gap:10px; margin-top:20px; }
  .form-actions .primary { margin-top:0; }
  .secondary { border:1px solid #383c3e; background:#151718; color:var(--lf-gold-light); }
  .optional,.hint { color:var(--lf-muted); font-size:11px; }
  .hint { margin:8px 0 0; }
  .notification-group { margin-top:19px; }
  legend { padding:0 0 6px; color:var(--lf-gold-light); font-family:var(--lf-heading); font-size:18px; }
  .checkbox-row { display:flex; align-items:center; justify-content:space-between; gap:15px; min-height:46px; margin:0; padding:10px 0; border-bottom:1px solid var(--lf-line); cursor:pointer; }
  .checkbox-row input { flex:0 0 auto; width:19px; height:19px; min-height:0; margin:0; accent-color:var(--lf-gold); }
  .feedback { margin:0 0 16px; font-size:13px; }
  button { width:100%; min-height:46px; padding:11px 14px; border-radius:6px; cursor:pointer; }
  .primary { margin-top:16px; border:1px solid var(--lf-gold); background:linear-gradient(110deg,#ffcd47,#f5a623); color:#090909; font-weight:600; }
  .primary:hover:not(:disabled) { background:var(--lf-gold-light); }
  .primary:disabled { cursor:default; }
  .error { padding:11px; border:1px solid #ef444455; border-radius:6px; background:#ef444411; color:#fca5a5; font-size:13px; overflow-wrap:anywhere; }
  .success { color:#a4deb2; font-size:13px; }
  form .success { margin:13px 0 0; }
  .pending-feature { margin:25px 0 20px; padding-top:20px; border-top:1px solid var(--lf-line); }
  .pending-feature h2 { color:#c8cdcf; font-size:19px; }
  .pending-feature p { margin:6px 0 0; color:var(--lf-muted); font-size:13px; }
  .logout { border:1px solid #383c3e; background:#151718; color:var(--lf-gold-light); }
</style>
