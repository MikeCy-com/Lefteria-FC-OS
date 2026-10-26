<script lang="ts">
  import { tick } from 'svelte';
  import type { components } from '../../api/schema';
  import type { FavoriteTeam, MobilePlayerOption, MobilePlayerOptionsStatus, MobileProfileUpdate } from '../controller.ts';
  import { AVATAR_FILE_TYPES, avatarFileError } from '../controller.ts';
  import './mobile-theme.css';

  interface Props {
    user: components['schemas']['MobileUserDto'];
    busy?: boolean;
    error?: string | null;
    playerOptions?: readonly MobilePlayerOption[];
    playerOptionsStatus?: MobilePlayerOptionsStatus;
    playerOptionsError?: string | null;
    onSaveFavoriteTeam: (value: FavoriteTeam) => boolean | Promise<boolean>;
    onSaveProfile: (changes: MobileProfileUpdate) => boolean | Promise<boolean>;
    onUploadAvatar: (file: File) => boolean | Promise<boolean>;
    onLoadPlayerOptions: () => boolean | Promise<boolean>;
    onLogout: () => void;
  }

  let { user, busy = false, error = null, playerOptions = [], playerOptionsStatus = 'idle', playerOptionsError = null,
    onSaveFavoriteTeam, onSaveProfile, onUploadAvatar, onLoadPlayerOptions, onLogout }: Props = $props();
  const id = $props.id();
  let draft = $state('');
  let submitting = $state(false);
  let saved = $state(false);
  let avatarFailed = $state(false);
  let editor = $state<'details' | 'notifications' | 'avatar' | 'player' | null>(null);
  let profileSaved = $state(false);
  let details = $state({ firstName: '', lastName: '', birthday: '', email: '' });
  let notificationDraft = $state<Record<string, boolean>>({});
  let firstNameInput = $state<HTMLInputElement>();
  let notificationHeading = $state<HTMLHeadingElement>();
  let profileHeading = $state<HTMLHeadingElement>();
  let detailsTrigger = $state<HTMLButtonElement>();
  let notificationsTrigger = $state<HTMLButtonElement>();
  let avatarTrigger = $state<HTMLButtonElement>();
  let avatarInput = $state<HTMLInputElement>();
  let selectedAvatar = $state<File | null>(null);
  let avatarPreview = $state<string | null>(null);
  let avatarSelectionError = $state<string | null>(null);
  let avatarPreviewFailed = $state(false);
  let avatarSaved = $state(false);
  let playerTrigger = $state<HTMLButtonElement>();
  let playerSelect = $state<HTMLSelectElement>();
  let playerDraft = $state('');
  let playerSaved = $state(false);
  let playerImageFailed = $state(false);
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
  const savedPlayerId = $derived(user.favourite_player_id ?? '');
  const storedPlayerUnlisted = $derived(savedPlayerId !== '' && !playerOptions.some((player) => player.id === savedPlayerId));
  const selectedPlayer = $derived(playerOptions.find((player) => player.id === playerDraft));
  const playerChanged = $derived(playerDraft !== savedPlayerId);
  const playerChoiceValid = $derived(playerDraft === '' || playerDraft === savedPlayerId ||
    (playerOptionsStatus === 'ready' && playerOptions.some((player) => player.id === playerDraft)));
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
  $effect(() => { void identityId; void user.role; editor = null; profileSaved = false; selectedAvatar = null; avatarSaved = false; avatarSelectionError = null; playerSaved = false; });
  $effect(() => { void selectedPlayer?.image_url; playerImageFailed = false; });
  $effect(() => {
    if (editor === 'player' && playerOptionsStatus !== 'loading') void tick().then(() => playerSelect?.focus());
  });
  $effect(() => {
    const file = selectedAvatar;
    if (!file) { avatarPreview = null; return; }
    const preview = URL.createObjectURL(file);
    avatarPreview = preview;
    return () => URL.revokeObjectURL(preview);
  });

  function openAvatar() {
    selectedAvatar = null;
    avatarSelectionError = null;
    avatarPreviewFailed = false;
    avatarSaved = false;
    editor = 'avatar';
    void tick().then(() => avatarInput?.focus());
  }

  function chooseAvatar(event: Event) {
    const input = event.currentTarget as HTMLInputElement;
    const file = input.files?.[0];
    if (isBusy || !file) return;
    const validation = avatarFileError(file);
    selectedAvatar = validation ? null : file;
    avatarSelectionError = validation;
    avatarPreviewFailed = false;
    avatarSaved = false;
    input.value = '';
  }

  async function uploadAvatar(event: SubmitEvent) {
    event.preventDefault();
    if (isBusy || !selectedAvatar || avatarPreviewFailed) return;
    submitting = true;
    avatarSaved = false;
    profileSaved = false;
    saved = false;
    try {
      if (await onUploadAvatar(selectedAvatar)) {
        selectedAvatar = null;
        editor = null;
        avatarSaved = true;
        void tick().then(() => profileHeading?.focus());
      }
    } finally { submitting = false; }
  }

  function openDetails() {
    details = { firstName: user.first_name ?? '', lastName: user.last_name ?? '', birthday: user.birthday ?? '', email: user.email ?? '' };
    profileSaved = false;
    editor = 'details';
    void tick().then(() => firstNameInput?.focus());
  }

  function openPlayer() {
    playerDraft = savedPlayerId;
    playerSaved = false;
    editor = 'player';
    void onLoadPlayerOptions();
  }

  async function savePlayer(event: SubmitEvent) {
    event.preventDefault();
    if (isBusy || !playerChanged || !playerChoiceValid || playerOptionsStatus === 'loading') return;
    submitting = true;
    playerSaved = false;
    profileSaved = false;
    saved = false;
    try {
      if (await onSaveProfile({ favourite_player_id: playerDraft })) {
        editor = null;
        playerSaved = true;
        void tick().then(() => profileHeading?.focus());
      }
    } finally { submitting = false; }
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
    selectedAvatar = null;
    avatarSelectionError = null;
    avatarPreviewFailed = false;
    editor = null;
    void tick().then(() => (previous === 'details' ? detailsTrigger : previous === 'avatar' ? avatarTrigger : previous === 'player' ? playerTrigger : notificationsTrigger)?.focus());
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
  {#if avatarSaved}<p class="feedback success" role="status" data-testid="avatar-saved">Η φωτογραφία προφίλ αποθηκεύτηκε.</p>{/if}
  {#if playerSaved}<p class="feedback success" role="status" data-testid="favorite-player-saved">Η προτίμηση παίκτη αποθηκεύτηκε.</p>{/if}

  {#if editor === 'player'}
    <form class="profile-editor" onsubmit={savePlayer} aria-labelledby={`${id}-player-title`} aria-busy={playerOptionsStatus === 'loading' || isBusy} data-testid="favorite-player-form">
      <h2 id={`${id}-player-title`}>Αγαπημένος παίκτης</h2>
      <p class="hint" id={`${id}-player-help`}>Επίλεξε παίκτη από την πρώτη ομάδα ή αφαίρεσε την προτίμησή σου.</p>
      {#if playerOptionsStatus === 'idle'}<button type="button" class="secondary" disabled={isBusy} onclick={() => void onLoadPlayerOptions()} data-testid="favorite-player-load">Φόρτωση παικτών</button>{/if}
      {#if playerOptionsStatus === 'loading'}<p role="status" data-testid="favorite-player-loading">Φόρτωση παικτών…</p>{/if}
      {#if playerOptionsStatus === 'error'}
        <p class="error" role="alert" data-testid="favorite-player-error">{playerOptionsError}</p>
        <button type="button" class="secondary" disabled={isBusy} onclick={() => void onLoadPlayerOptions()} data-testid="favorite-player-retry">Δοκιμή ξανά</button>
      {/if}
      {#if playerOptionsStatus === 'ready' && playerOptions.length === 0}<p role="status" data-testid="favorite-player-empty">Δεν υπάρχουν διαθέσιμοι παίκτες στην πρώτη ομάδα.</p>{/if}
      {#if storedPlayerUnlisted && playerOptionsStatus === 'ready'}<p class="hint" data-testid="favorite-player-unlisted">Η αποθηκευμένη επιλογή δεν υπάρχει στη διαθέσιμη λίστα. Διατηρείται μέχρι να επιλέξεις άλλον παίκτη ή «Κανένας».</p>{/if}
      <label for={`${id}-favorite-player`}>Προτίμηση παίκτη</label>
      <select bind:this={playerSelect} id={`${id}-favorite-player`} bind:value={playerDraft} disabled={isBusy || playerOptionsStatus === 'loading'}
        aria-describedby={`${id}-player-help`} data-testid="favorite-player-select">
        <option value="">Κανένας</option>
        {#if storedPlayerUnlisted}<option value={savedPlayerId}>{playerOptionsStatus === 'ready' ? 'Αποθηκευμένη επιλογή (εκτός λίστας)' : 'Αποθηκευμένη επιλογή'}</option>{/if}
        {#if playerOptionsStatus === 'ready'}
          {#each playerOptions as player (player.id)}<option value={player.id}>#{player.number} · {player.name}{player.position ? ` · ${player.position}` : ''}</option>{/each}
        {/if}
      </select>
      {#if selectedPlayer && playerOptionsStatus === 'ready'}
        <div class="player-preview" data-testid="favorite-player-preview">
          {#if selectedPlayer.image_url && !playerImageFailed}<img src={selectedPlayer.image_url} alt="" onerror={() => { playerImageFailed = true; }} />{/if}
          <p><strong>{selectedPlayer.name}</strong><span>#{selectedPlayer.number}{selectedPlayer.position ? ` · ${selectedPlayer.position}` : ''}</span></p>
        </div>
      {/if}
      <div class="form-actions">
        <button type="button" class="secondary" disabled={isBusy} onclick={cancelEditor} data-testid="favorite-player-cancel">Ακύρωση</button>
        <button type="submit" class="primary" disabled={isBusy || !playerChanged || !playerChoiceValid || playerOptionsStatus === 'loading'} data-testid="favorite-player-save">{isBusy ? 'Αποθήκευση…' : 'Αποθήκευση'}</button>
      </div>
    </form>
  {:else if editor === 'avatar'}
    <form class="profile-editor" onsubmit={uploadAvatar} aria-labelledby={`${id}-avatar-title`} data-testid="profile-avatar-form">
      <h2 id={`${id}-avatar-title`}>Αλλαγή φωτογραφίας</h2>
      <label for={`${id}-avatar-file`}>Νέα φωτογραφία προφίλ</label>
      <input bind:this={avatarInput} id={`${id}-avatar-file`} type="file" accept={AVATAR_FILE_TYPES.join(',')}
        disabled={isBusy} onchange={chooseAvatar} aria-describedby={`${id}-avatar-help`} data-testid="avatar-file-input" />
      <p class="hint" id={`${id}-avatar-help`}>JPEG, PNG, WebP ή GIF, έως 5 MB. Η φωτογραφία προφίλ έχει δημόσιο σύνδεσμο. Τα κινούμενα αρχεία αποθηκεύονται ως στατική φωτογραφία.</p>
      {#if avatarSelectionError}<p class="error" role="alert" data-testid="avatar-selection-error">{avatarSelectionError}</p>{/if}
      {#if selectedAvatar && avatarPreview}
        <figure class="avatar-preview" data-testid="avatar-preview">
          <img src={avatarPreview} alt="Προεπισκόπηση της νέας φωτογραφίας" onerror={() => { avatarPreviewFailed = true; }} />
          <figcaption>{selectedAvatar.name}</figcaption>
        </figure>
      {/if}
      {#if avatarPreviewFailed}<p class="error" role="alert">Δεν ήταν δυνατή η προεπισκόπηση. Επίλεξε άλλη φωτογραφία.</p>{/if}
      <div class="form-actions">
        <button type="button" class="secondary" disabled={isBusy} onclick={cancelEditor} data-testid="avatar-cancel">Ακύρωση</button>
        <button type="submit" class="primary" disabled={isBusy || !selectedAvatar || avatarPreviewFailed} data-testid="avatar-upload">{isBusy ? 'Μεταφόρτωση…' : 'Αποθήκευση φωτογραφίας'}</button>
      </div>
    </form>
  {:else if editor === 'details'}
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
      <button bind:this={avatarTrigger} type="button" class="secondary" disabled={isBusy} onclick={openAvatar} data-testid="profile-edit-avatar">Αλλαγή φωτογραφίας</button>
      <button bind:this={detailsTrigger} type="button" class="secondary" disabled={isBusy} onclick={openDetails} data-testid="profile-edit-details">Επεξεργασία στοιχείων</button>
      <button bind:this={notificationsTrigger} type="button" class="secondary" disabled={isBusy} onclick={openNotifications} data-testid="profile-edit-notifications">Προτιμήσεις ειδοποιήσεων</button>
      <button bind:this={playerTrigger} type="button" class="secondary" disabled={isBusy} onclick={openPlayer} data-testid="profile-edit-favorite-player">Αγαπημένος παίκτης</button>
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
  .avatar-preview { margin:18px 0 0; }
  .avatar-preview img { display:block; width:160px; height:160px; max-width:100%; margin:0 auto; border:1px solid #806127; border-radius:8px; object-fit:contain; background:#080808; }
  .avatar-preview figcaption { margin-top:8px; color:var(--lf-muted); font-size:12px; text-align:center; overflow-wrap:anywhere; }
  .player-preview { display:flex; align-items:center; gap:12px; margin-top:16px; }
  .player-preview img { width:54px; height:54px; border-radius:6px; object-fit:cover; }
  .player-preview p { margin:0; overflow-wrap:anywhere; }
  .player-preview strong,.player-preview span { display:block; }
  .player-preview span { margin-top:4px; color:var(--lf-muted); font-size:12px; }
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
  .logout { margin-top:20px; border:1px solid #383c3e; background:#151718; color:var(--lf-gold-light); }
</style>
