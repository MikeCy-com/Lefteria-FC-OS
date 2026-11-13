<script lang="ts">
  import { getContext } from 'svelte';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import { browser } from '$app/env';
  import MobileShell from '../../../lib/mobile/components/MobileShell.svelte';
  import MobileProfile from '../../../lib/mobile/components/MobileProfile.svelte';
  import MobileRecipientInvitations from '../../../lib/mobile/components/MobileRecipientInvitations.svelte';
  import MobileCalendar from '../../../lib/mobile/components/MobileCalendar.svelte';
  import MobileGroupChat from '../../../lib/mobile/components/MobileGroupChat.svelte';
  import MobilePrivateChat from '../../../lib/mobile/components/MobilePrivateChat.svelte';
  import MobileAppAnnouncements from '../../../lib/mobile/components/MobileAppAnnouncements.svelte';
  import MobileAppNotificationInbox from '../../../lib/mobile/components/MobileAppNotificationInbox.svelte';
  import type {AppNotification} from '../../../lib/mobile/notification-inbox.ts';
  import MobileFamily from '../../../lib/mobile/components/MobileFamily.svelte';
  import MobileParentHome from '../../../lib/mobile/components/MobileParentHome.svelte';
  import MobileRoleHome from '../../../lib/mobile/components/MobileRoleHome.svelte';
  import MobileTrainingFees from '../../../lib/mobile/components/MobileTrainingFees.svelte';
  import MobileAcademyEducation from '../../../lib/mobile/components/MobileAcademyEducation.svelte';
  import MobilePublishedHubEducation from '../../../lib/mobile/components/MobilePublishedHubEducation.svelte';
  import MobilePersonalLearningJourney from '../../../lib/mobile/components/MobilePersonalLearningJourney.svelte';
  import MobilePlayerCoachFeedback from '../../../lib/mobile/components/MobilePlayerCoachFeedback.svelte';
  import MobileCoachFeedbackComposer from '../../../lib/mobile/components/MobileCoachFeedbackComposer.svelte';
  import MobileCoachFeedbackTargets from '../../../lib/mobile/components/MobileCoachFeedbackTargets.svelte';
  import OrganizationSwitcher from '../../../lib/mobile/components/OrganizationSwitcher.svelte';
  import GuidedSetup from '../../../lib/onboarding/GuidedSetup.svelte';
  import {mobileGuide} from '../../../lib/onboarding/guides.ts';
  import { MOBILE_AUTH_CONTEXT, type MobileAuthController } from '../../../lib/mobile/controller.ts';
  import { ROLE_NAMES, TAB_NAMES, allowedTabs, isMobileRole, tabFromLocation, type MobileRole, type MobileTab } from '../../../lib/mobile/navigation.ts';

  const controller = getContext<MobileAuthController>(MOBILE_AUTH_CONTEXT);
  let composing = $state(false);
  let chatMode = $state<'groups'|'private'>('groups');
  const user = $derived($controller.user);
  const role = $derived(user && isMobileRole(user.role) ? user.role : null);
  const activeTab = $derived(role ? tabFromLocation(page.url, role) : 'home');
  let notificationCount=$state(0);
  $effect(()=>{
    const id=user?.id,currentRole=role,currentTab=activeTab,
      managementOwner=$controller.organization?.organization_id,
      hasSelection=$controller.organization?.requires_organization_selection;
    void currentTab;void managementOwner;
    if(!browser||!id||!currentRole||currentRole==='fan'||
      !user?.current_role_available||(currentRole==='management'&&hasSelection)){
      notificationCount=0;return;
    }
    let valid=true;
    void controller.getAppNotificationInbox().then(result=>{
      if(valid&&user?.id===id&&role===currentRole)
        notificationCount=result.unread_count;
    }).catch(()=>{if(valid)notificationCount=0;});
    return()=>{valid=false;};
  });
  async function openNotification(notice:AppNotification){
    const query=new URLSearchParams({tab:'announcements',
      organization_id:notice.organization_id,
      group:notice.group_id,notice:notice.announcement_id});
    await goto('/app?'+query.toString(),{reset:true});
  }
  $effect(() => { if (browser && $controller.status === 'anonymous') void goto('/app/login', { replace: true }); });
  $effect(() => { void role; composing = false; });
  $effect(() => { void user?.id; void role; chatMode='groups'; });

  async function openTab(tab: MobileTab, childId?: string) {
    composing = false;
    // Caller-supplied child selection never replaces the server's current
    // guardian authorization check.
    const query = new URLSearchParams({ tab });
    if (role === 'parent' && tab === 'calendar' && childId) query.set('player_id', childId);
    await goto('/app?' + query.toString(), { reset: true });
  }
  async function changeRole(nextRole: MobileRole) {
    if (await controller.switchRole(nextRole)) await openTab('home');
  }
  async function openProfile() {
    // Own-profile updates are supported for the current mobile identity.
    // Never switch a parent, player or coach into the fan identity to edit it.
    await openTab('profile');
  }
  function logout() { controller.logout(); }
</script>

<svelte:head><title>{TAB_NAMES[activeTab]} | Λευτεριά FC</title><meta name="robots" content="noindex" /></svelte:head>

{#if $controller.status === 'authenticated' && user && role}
  <MobileShell {role} {activeTab} availableRoles={user.current_role_available ? $controller.availableRoles : $controller.availableRoles.filter((option) => option === 'fan')} userName={user.name}
    avatarUrl={user.avatar_url} notificationCount={notificationCount} busy={$controller.busy}
    onTabChange={openTab} onRoleChange={changeRole}
    onNotifications={async () => { await openTab('notifications'); }} onProfile={openProfile}
    onCreatePost={() => { composing = true; }}>
    {#if $controller.error && $controller.error !== $controller.organizationError && !(activeTab === 'profile' && role === 'fan' && user.current_role_available && !composing)}<p class="error" role="alert">{$controller.error}</p>{/if}
    {#if !user.current_role_available}
      <section class="content-card" aria-labelledby="revoked-title" data-testid="revoked-role">
        <h1 id="revoked-title">Ο ρόλος δεν είναι πλέον διαθέσιμος</h1>
        <p>Η πρόσβαση με αυτόν τον ρόλο έχει αφαιρεθεί.</p>
        {#if $controller.availableRoles.includes('fan')}
          <button class="primary" type="button" disabled={$controller.busy} onclick={() => void changeRole('fan')} data-testid="recover-fan">Συνέχεια ως φίλαθλος</button>
        {/if}
        <button class="secondary" type="button" disabled={$controller.busy} onclick={logout}>Αποσύνδεση</button>
      </section>
    {:else}
      {#if role === 'management'}
        {#if $controller.organization}
          <OrganizationSwitcher organizations={$controller.organization.available_organizations}
            selectedId={$controller.organization.organization_id} requiresSelection={$controller.organization.requires_organization_selection}
            busy={$controller.busy} error={$controller.organizationError}
            onSelect={async (id) => { await controller.selectOrganization(id); }} />
        {:else if $controller.organizationError}
          <section class="content-card"><p class="error" role="alert">{$controller.organizationError}</p>
            <button class="secondary" type="button" disabled={$controller.busy} onclick={() => void controller.refreshOrganization()}>Δοκιμή ξανά</button>
          </section>
        {/if}
      {/if}
      {#if activeTab === 'home' && !composing}
        {#key user.id+':'+role}
          <GuidedSetup
            steps={mobileGuide({...user,role})}
            scope={'mobile:'+user.id+':'+role}
            title="Πρώτα βήματα στον ρόλο μου"
            onNavigate={async href=>{await goto(href);}}/>
        {/key}
      {/if}
      {#if activeTab === 'home' && role === 'parent' && !composing}
        {#key `${user.id}:${role}`}
          <MobileParentHome identityId={user.id} parentName={user.name}
            family={$controller.family} familyStatus={$controller.familyStatus} familyError={$controller.familyError}
            calendar={$controller.calendar} calendarStatus={$controller.calendarStatus}
            calendarError={$controller.calendarError} busy={$controller.busy}
            onLoadFamily={() => controller.loadFamily()}
            onLoadCalendar={(month, childId) => controller.loadCalendar(month, childId)}
            onChildren={() => openTab('children')}
            onEducation={() => openTab('hub')}
            onMessages={() => openTab('chat')}
            onFinancials={() => openTab('financials')}
            onRespond={(item,status)=>controller.submitAvailability(item,status)}
            onCalendar={(childId) => openTab('calendar', childId)} />
        {/key}
      {:else if activeTab === 'home' && role !== 'parent' && !composing}
        {#key user.id+':'+role+':'+(role==='management'?$controller.organization?.organization_id??'unselected':'')}
          <MobileRoleHome {role} identityId={user.id} userName={user.name}
            organizationId={role==='management' && !$controller.organization?.requires_organization_selection
              ? $controller.organization?.organization_id : null}
            calendar={$controller.calendar} calendarStatus={$controller.calendarStatus}
            calendarError={$controller.calendarError} busy={$controller.busy}
            onLoadCalendar={month=>controller.loadCalendar(month)}
            onNavigate={openTab}
            onRespond={role==='player'?(item,status)=>controller.submitAvailability(item,status):undefined}/>
          {#if role==='player'}
            <MobilePlayerCoachFeedback {controller}/>
          {:else if role==='coach'}
            <MobileCoachFeedbackTargets {controller} onOpenFeedback={()=>openTab('squad')}/>
          {/if}
        {/key}
      {:else if activeTab === 'profile' && !composing}
        <MobileProfile {user} busy={$controller.busy} error={$controller.error}
          playerOptions={$controller.playerOptions} playerOptionsStatus={$controller.playerOptionsStatus} playerOptionsError={$controller.playerOptionsError}
          onLoadPlayerOptions={() => controller.loadPlayerOptions()}
          onSaveFavoriteTeam={(value) => controller.updateFavoriteTeam(value)}
          onSaveProfile={(changes) => controller.updateProfile(changes)}
          onUploadAvatar={(file) => controller.uploadAvatar(file)} onLogout={logout} />
        {#if role === 'fan'}
        {#key `${user.id}:${user.phone}`}
          <MobileRecipientInvitations
            inbox={$controller.recipientInvitations}
            status={$controller.recipientStatus}
            error={$controller.recipientError} busy={$controller.busy}
            decliningId={$controller.recipientDecliningId}
            declineError={$controller.recipientDeclineError}
            acceptingId={$controller.recipientAcceptingId}
            acceptError={$controller.recipientAcceptError}
            acceptSuccess={$controller.recipientAcceptSuccess}
            onAccept={(id) => controller.acceptRecipientInvitation(id)}
            onDecline={(id) => controller.declineRecipientInvitation(id)}
            delegationAvailability={$controller.delegationAvailability}
            delegationStatus={$controller.delegationStatus}
            delegationError={$controller.delegationError}
            delegatedCalendar={$controller.delegatedCalendar}
            delegatedCalendarStatus={$controller.delegatedCalendarStatus}
            delegatedCalendarError={$controller.delegatedCalendarError}
            delegatedCalendarLoadingMore={$controller.delegatedCalendarLoadingMore}
            onLoadCalendar={(month, append) => controller.loadDelegatedCalendar(month, append)}
            onRespondCalendar={(item, status) => controller.submitDelegatedAvailability(item, status)}
            calendarRespondingKey={$controller.delegatedRespondingKey}
            calendarRespondError={$controller.delegatedRespondError}
            calendarRespondSuccess={$controller.delegatedRespondSuccess}
            onLoadDelegation={() => controller.loadDelegationAvailability()}
            onLoad={() => controller.loadRecipientInvitations()} />
        {/key}
        {/if}
      {:else if activeTab === 'squad' && role === 'coach' && !composing}
        {#key user.id+':'+role}
          <MobileCoachFeedbackComposer {controller}/>
        {/key}
      {:else if activeTab === 'financials' && role === 'parent' && !composing}
        {#key user.id+':'+role}
          <MobileTrainingFees {controller}/>
        {/key}
      {:else if (activeTab === 'hub' || (activeTab === 'education' && role === 'parent')) && (role === 'parent' || role === 'player' || role === 'fan') && !composing}
        {#key user.id+':'+role}
          {#if role === 'parent' || role === 'player'}
            <MobilePersonalLearningJourney {controller} viewerRole={role}/>
            <MobilePublishedHubEducation {controller} viewerRole={role}/>
          {/if}
          <MobileAcademyEducation viewerRole={role} />
          {#if role === 'player'}
            <MobilePlayerCoachFeedback {controller} />
          {/if}
        {/key}
      {:else if activeTab === 'children' && role === 'parent' && !composing}
        {#key `${user.id}:${role}`}
          <MobileFamily family={$controller.family} status={$controller.familyStatus}
            error={$controller.familyError} busy={$controller.busy}
            outgoingInvitations={$controller.outgoingInvitations}
            outgoingStatus={$controller.outgoingStatus}
            outgoingError={$controller.outgoingError}
            outgoingCancellingId={$controller.outgoingCancellingId}
            outgoingCancelError={$controller.outgoingCancelError}
            activeDelegations={$controller.activeFamilyDelegations}
            activeStatus={$controller.activeFamilyStatus}
            activeError={$controller.activeFamilyError}
            guardianPermissions={$controller.guardianPermissions}
            guardianStatus={$controller.guardianStatus}
            guardianError={$controller.guardianError}
            guardianPending={$controller.guardianPending}
            guardianRevokingId={$controller.guardianRevokingId}
            onLoadGuardianPermissions={() => controller.loadGuardianPermissions()}
            onCreatePermission={(playerId,kind,phone,requestId) =>
              controller.createFamilyPermission(playerId,kind,phone,requestId)}
            onCreateManyGuardian={(children,phone) =>
              controller.createManyGuardianPermissions(children,phone)}
            onRevokeGuardianPermission={(id) => controller.revokeGuardianPermission(id)}
            onLoadActive={() => controller.loadActiveFamilyDelegations()}
            onRevokeDelegation={(id) => controller.revokeFamilyDelegation(id)}
            revokingPlayerId={$controller.familyRevokingPlayerId}
            revokeError={$controller.familyRevokeError}
            revokeSuccess={$controller.familyRevokeSuccess}
            onCancelOutgoing={(id) => controller.cancelOutgoingInvitation(id)}
            onLoadOutgoing={() => controller.loadOutgoingInvitations()}
            onLoad={() => controller.loadFamily()} onCalendar={(childId) => openTab('calendar', childId)} />
        {/key}
      {:else if activeTab === 'announcements' && role !== 'fan' && !composing}
        {#if role==='management' && (!$controller.organization?.organization_id || $controller.organization.requires_organization_selection)}
          <section class="content-card" data-testid="announcements-organization-required">
            <h1>Ανακοινώσεις</h1><p>Επίλεξε πρώτα οργανισμό.</p>
          </section>
        {:else}
          {#key user.id+':'+role+':'+(role==='management'?$controller.organization?.organization_id:'')}
            <MobileAppAnnouncements {controller}
              initialOrganization={page.url.searchParams.get('organization_id')}
              initialGroup={page.url.searchParams.get('group')}
              initialAnnouncementId={page.url.searchParams.get('notice')}/>
          {/key}
        {/if}
      {:else if activeTab === 'notifications' && !composing}
        {#if role==='fan'}
          <section class="content-card" aria-labelledby="fan-notification-title">
            <h1 id="fan-notification-title">Ειδοποιήσεις</h1>
            <p>Οι ομαδικές ανακοινώσεις είναι διαθέσιμες μόνο στα ενεργά
              μέλη ομάδων και Ακαδημίας.</p>
          </section>
        {:else if role==='management' &&
            (!$controller.organization?.organization_id ||
             $controller.organization.requires_organization_selection)}
          <section class="content-card"><h1>Ειδοποιήσεις</h1>
            <p>Επίλεξε πρώτα οργανισμό.</p></section>
        {:else}
          {#key user.id+':'+role+':'+(role==='management'?$controller.organization?.organization_id:'')}
            <MobileAppNotificationInbox {controller}
              onUnreadChange={count=>notificationCount=count}
              onOpen={openNotification}/>
          {/key}
        {/if}
      {:else if activeTab === 'chat' && role !== 'fan' && !composing}
        {#if role === 'management' && (!$controller.organization?.organization_id || $controller.organization.requires_organization_selection)}
          <section class="content-card" data-testid="chat-organization-required"><h1>Μηνύματα</h1><p>Επίλεξε οργανισμό για να δεις τις συνομιλίες.</p></section>
        {:else}
          {#if role==='parent'||role==='coach'||role==='management'}
            <div class="chat-mode-tabs" role="group" aria-label="Τύπος συνομιλίας">
              <button type="button" class:current={chatMode==='groups'}
                aria-pressed={chatMode==='groups'} onclick={()=>{chatMode='groups';}}>Ομάδες</button>
              <button type="button" class:current={chatMode==='private'}
                aria-pressed={chatMode==='private'} onclick={()=>{chatMode='private';}}>Ιδιωτικά</button>
            </div>
          {/if}
          {#key user.id+':'+role+':'+(role==='management'?$controller.organization?.organization_id:'')+':'+chatMode}
            {#if chatMode==='private' && (role==='parent'||role==='coach'||role==='management')}
              <MobilePrivateChat {controller} actorId={user.id} {role}/>
            {:else}
              <MobileGroupChat {controller} actorId={user.id} {role} />
            {/if}
          {/key}
        {/if}
      {:else if activeTab === 'calendar' && role !== 'fan' && !composing}
        {#if role === 'management' && (!$controller.organization?.organization_id || $controller.organization.requires_organization_selection)}
          <section class="content-card" data-testid="calendar-organization-required"><h1>Πρόγραμμα</h1><p>Επίλεξε οργανισμό για να δεις το πρόγραμμά του.</p></section>
        {:else}
          {#key `${user.id}:${role}:${role === 'management' ? $controller.organization?.organization_id : ''}`}
            <MobileCalendar {role} initialPlayerId={role === 'parent' ? page.url.searchParams.get('player_id') : null}
              calendar={$controller.calendar} status={$controller.calendarStatus} error={$controller.calendarError}
              loadingMore={$controller.calendarLoadingMore} busy={$controller.busy}
              responses={$controller.calendarResponses} responsesKey={$controller.calendarResponsesKey}
              responsesStatus={$controller.calendarResponsesStatus} responsesError={$controller.calendarResponsesError}
              responsesLoadingMore={$controller.calendarResponsesLoadingMore}
              onLoad={(month, playerId) => controller.loadCalendar(month, playerId)}
              onLoadMore={() => controller.loadMoreCalendar()}
              onRespond={(item, status) => controller.submitAvailability(item, status)}
              onLoadResponses={(item, append) => controller.loadCalendarResponses(item, append)}
              onCloseResponses={() => controller.clearCalendarResponses()} />
          {/key}
        {/if}
      {:else if activeTab === 'more' && !composing}
        {#key user.id+':'+role}
          <GuidedSetup
            steps={mobileGuide({...user,role})}
            scope={'mobile:'+user.id+':'+role}
            title="Οδηγός χρήσης εφαρμογής"
            onNavigate={async href=>{await goto(href);}}/>
        {/key}
        <section class="content-card" aria-labelledby="more-title"><h1 id="more-title">{TAB_NAMES.more}</h1>
          <div class="more-links">
            {#each allowedTabs(role).filter((tab) => !['home', 'more', 'profile'].includes(tab)) as tab (tab)}
              <button class="secondary" type="button" disabled={$controller.busy} onclick={() => void openTab(tab)}>{TAB_NAMES[tab]}</button>
            {/each}
            <button class="secondary" type="button" disabled={$controller.busy} onclick={() => void openProfile()}>Το προφίλ μου</button>
            <button class="secondary" type="button" disabled={$controller.busy} onclick={logout}>Αποσύνδεση</button>
          </div>
        </section>
      {:else}
        <section class="content-card development" aria-labelledby="development-title" data-testid="under-developments">
          <p class="context">{ROLE_NAMES[role] + ($controller.organization?.organization_name ? ` · ${$controller.organization.organization_name}` : '')}</p>
          <h1 id="development-title">{composing ? 'Δημιουργία ανάρτησης' : TAB_NAMES[activeTab]}</h1>
          <p class="development-label">Under Developments</p>
          {#if composing}<button class="secondary" type="button" onclick={() => { composing = false; }}>Πίσω</button>{/if}
        </section>
      {/if}
    {/if}
  </MobileShell>
{:else}
  <div class="app-status">
    {#if $controller.status === 'unavailable'}
      <p role="alert">{$controller.error}</p>
      <button class="secondary" type="button" disabled={$controller.busy} onclick={() => void controller.bootstrap()}>Δοκιμή ξανά</button>
      <button class="secondary" type="button" disabled={$controller.busy} onclick={logout}>Αποσύνδεση</button>
    {:else}<p role="status">Έλεγχος σύνδεσης…</p>{/if}
  </div>
{/if}

<style>
  .content-card { margin:0 0 16px; padding:20px; border:1px solid #292b2c; border-radius:8px; background:linear-gradient(135deg,#17191a,#101112); }
  .content-card h1 { max-width:none; margin:0 0 14px; font-family:'Fira Sans Extra Condensed',sans-serif; font-size:28px; line-height:1.15; }
  .content-card p { color:#a6aaad; }
  .content-card .context { margin:0 0 8px; color:#ffca46; font-size:12px; }
  .development { margin-top:16px; min-height:180px; }
  .development-label { font-size:16px; }
  .error { margin:0 0 16px; padding:12px; border:1px solid #ef444455; border-radius:6px; background:#ef444411; color:#fca5a5!important; overflow-wrap:anywhere; }
  button { min-height:44px; padding:10px 16px; border:1px solid #383c3e; border-radius:6px; font:inherit; cursor:pointer; }
  .primary { margin:8px 8px 0 0; border-color:#f5a623; background:#f5a623; color:#080808; }
  .secondary { background:#151718; color:#ffca46; }
  button:disabled { opacity:.55; cursor:wait; }
  button:focus-visible { outline:2px solid #ffca46; outline-offset:3px; }
  .more-links { display:grid; gap:9px; }
  .more-links button { text-align:left; }
  .app-status { display:grid; align-content:center; justify-items:center; gap:12px; min-height:100dvh; padding:24px; background:#080808; color:#fff; font-family:'Fira Sans',sans-serif; }
  .app-status p { max-width:32rem; text-align:center; }
  .chat-mode-tabs{display:grid;grid-template-columns:1fr 1fr;gap:8px;margin:12px 14px 0}
  .chat-mode-tabs button{min-height:44px;background:#202325;border:1px solid #525252;
    color:#f0e0bd;border-radius:10px;font-family:var(--lf-body);font-weight:700;cursor:pointer}
  .chat-mode-tabs button.current{background:#3b301e;border-color:var(--lf-gold);color:var(--lf-gold-light)}
  .chat-mode-tabs button:focus-visible{outline:2px solid var(--lf-gold);outline-offset:2px}
</style>
