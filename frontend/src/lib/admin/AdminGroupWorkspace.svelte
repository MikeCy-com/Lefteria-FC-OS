<script lang="ts">
  import {page} from '$app/state';
  import {hasAdminPermission} from './permissions.ts';
  import type {WebAdminSession} from './controller.ts';
  import AdminAcademyTrainingPlans from './AdminAcademyTrainingPlans.svelte';
  import AdminGroupAttendancePanel from './AdminGroupAttendancePanel.svelte';
  import AdminGroupPublishedFixtures from './AdminGroupPublishedFixtures.svelte';
  import AdminGroupMatchDraftPanel from './AdminGroupMatchDraftPanel.svelte';
  import AdminRosterPlayersPanel from './AdminRosterPlayersPanel.svelte';
  import AdminAcademyGroupProfilePanel from './AdminAcademyGroupProfilePanel.svelte';
  import AdminFirstTeamProfilePanel from './AdminFirstTeamProfilePanel.svelte';
  import AdminCoachDirectoryPanel from './AdminCoachDirectoryPanel.svelte';
  import {academyMonthWindow} from './academy-schedule.ts';

  type Owner='lefteria_fc'|'lefteria_fc_academy';
  type Group={id:string;name:string;kind:'team'|'academy_group';active_players:number};
  type Tab='players'|'training'|'attendance'|'matches'|'coaches'|'settings';
  let {group,groups,organization,user,canCreate,canReadAttendance,onExpired,onRevoked,onChanged}:{
    group:Group;groups:Group[];organization:Owner;user:WebAdminSession['user'];
    canCreate:boolean;canReadAttendance:boolean;
    onExpired:()=>void;onRevoked:()=>void;onChanged:()=>void;
  }=$props();
  let tab=$state<Tab>('players');
  let month=$state(new Intl.DateTimeFormat('en-CA',{
    timeZone:'Asia/Nicosia',year:'numeric',month:'2-digit'
  }).format(new Date()).slice(0,7));
  const range=$derived(academyMonthWindow(month));
  const canTrainingWrite=$derived(canReadAttendance&&hasAdminPermission(user.permissions,'training:write'));
  const canAttendanceWrite=$derived(canReadAttendance&&hasAdminPermission(user.permissions,'attendance:write'));
  const canEvaluate=$derived(['manager','super_admin'].includes(user.role)&&
    hasAdminPermission(user.permissions,organization==='lefteria_fc'?'teams:write':'academy:write'));
  const canPrivate=$derived(['manager','super_admin'].includes(user.role));
  const canAnnounce=$derived(canCreate&&canEvaluate);
  const canFinance=$derived(organization==='lefteria_fc_academy'&&
    hasAdminPermission(user.permissions,'finance:read'));
  $effect(()=>{
    const requested=page.url.searchParams.get('tab');
    tab=requested==='training'&&canReadAttendance?'training':
      requested==='attendance'&&canReadAttendance?'attendance':
      requested==='matches'?'matches':
      requested==='coaches'&&canCreate?'coaches':
      requested==='settings'&&canCreate?'settings':'players';
  });
</script>

<section class="group-operating-space" data-testid="admin-group-embedded-workspace">
  <div class="group-operation-tabs" role="group" aria-label={'Ενότητες '+group.name}
    data-testid="admin-group-tabs">
    <button type="button" aria-pressed={tab==='players'} onclick={()=>tab='players'}
      data-testid="admin-group-players-link">Αθλητές & ατομικοί φάκελοι</button>
    {#if canReadAttendance}
      <button type="button" aria-pressed={tab==='training'} onclick={()=>tab='training'}
        data-testid="admin-group-training-link">Προπονήσεις & πρόγραμμα</button>
      <button type="button" aria-pressed={tab==='attendance'} onclick={()=>tab='attendance'}
        data-testid="admin-group-attendance-link">Παρουσίες</button>
    {/if}
    <button type="button" aria-pressed={tab==='matches'} onclick={()=>tab='matches'}
      data-testid="admin-group-matches-link">Αγώνες & αποτελέσματα</button>
    {#if canCreate}
      <button type="button" aria-pressed={tab==='coaches'} onclick={()=>tab='coaches'}
        data-testid="admin-group-coaches-link">Προπονητές</button>
    {/if}
    {#if canAnnounce}
      <a class="group-notice-link" data-testid="admin-group-app-announcements-link"
        href={'/admin/announcements?organization_id='+encodeURIComponent(organization)+
          '&group_id='+encodeURIComponent(group.id)}>
        Ανακοινώσεις εφαρμογής <span aria-hidden="true">↗</span>
      </a>
    {/if}
    {#if canCreate}
      <button type="button" aria-pressed={tab==='settings'} onclick={()=>tab='settings'}
        data-testid="admin-group-settings-link">Ρυθμίσεις τμήματος</button>
    {/if}
  </div>

  {#key organization+':'+group.id+':'+user.id+':'+tab}
    <div class="group-operation-content" data-testid="admin-group-tab-content">
      {#if tab==='players'}
        <p class="admin-muted">Επιλέξτε αθλητή για τις ατομικές αξιολογήσεις και τα προστατευμένα
          έγγραφά του. Οι αναθέσεις νέων αθλητών γίνονται από τον επαληθευμένο
          κατάλογο του οργανισμού και όχι από υποθετικό ρόστερ.</p>
        {#if canCreate}
          <AdminRosterPlayersPanel owner={organization} actorId={user.id} {groups} focusGroupId={group.id}
            allowPrivate={canPrivate} allowEvaluationWrite={canEvaluate} allowFinance={canFinance}
            canReadAttendance={canReadAttendance} canWriteAttendance={canAttendanceWrite}
            allowGoalManagement={user.role==='super_admin'}
            allowGoalRead={user.role==='manager'}
            {onExpired} {onRevoked} {onChanged}/>
        {:else}
          <p role="status">Η διαχείριση ατομικών φακέλων απαιτεί επαληθευμένα δικαιώματα.</p>
        {/if}
      {:else if tab==='training' && canReadAttendance && range}
        <label class="group-month">Μήνας προπονήσεων
          <input type="month" bind:value={month} min="2024-01" max="2032-12"/>
        </label>
        <AdminAcademyTrainingPlans groupId={group.id}
          from={range.start} to={range.end} actor={user.id}
          allowed={canReadAttendance} canWrite={canTrainingWrite}/>
      {:else if tab==='attendance' && canReadAttendance}
        <AdminGroupAttendancePanel groupId={group.id} actorId={user.id}
          canRead={canReadAttendance} canWrite={canAttendanceWrite}/>
      {:else if tab==='matches'}
        <AdminGroupPublishedFixtures groupId={group.id} {organization} actorId={user.id}/>
        {#if canCreate}
          <AdminGroupMatchDraftPanel groupId={group.id} {organization}
            actorId={user.id} canWrite={canCreate}/>
        {/if}
      {:else if tab==='coaches' && canCreate}
        <AdminCoachDirectoryPanel owner={organization} actorId={user.id}
          groups={[group]} {onExpired} {onRevoked}/>
      {:else if tab==='settings' && canCreate}
        {#if organization==='lefteria_fc'}
          <AdminFirstTeamProfilePanel actorId={user.id} groups={[group]}
            {onExpired} {onRevoked}/>
        {:else}
          <AdminAcademyGroupProfilePanel actorId={user.id} groups={[group]}
            {onExpired} {onRevoked}/>
        {/if}
      {/if}
    </div>
  {/key}
</section>
<style>
  .group-operating-space{min-width:0;max-width:100%}
  .group-operation-tabs{display:flex;flex-wrap:wrap;gap:6px;margin:16px 0;padding-bottom:10px;
    border-bottom:1px solid #38332a;overflow-x:auto}
  .group-operation-tabs button,.group-operation-tabs a{display:inline-flex;align-items:center;justify-content:center;
    min-height:40px;padding:9px 12px;color:#d8dde0;background:#121516;border:1px solid #343637;
    border-radius:6px;cursor:pointer;font:700 13px/1.25 'Fira Sans',sans-serif}
  .group-operation-tabs button[aria-pressed='true']{background:#272012;color:#f8c65a;border-color:#d39722}
  .group-operation-tabs button:focus-visible,.group-operation-tabs a:focus-visible{outline:3px solid #ffd164;outline-offset:3px}
  .group-operation-tabs a{text-decoration:none;border-style:dashed;gap:8px}
  .group-operation-tabs a:hover{border-color:#ffcc65;background:#27271e}
  .group-operation-content{display:grid;gap:15px;min-width:0}
  .group-operation-content>.admin-muted{margin:0}
  .group-month{display:grid;gap:6px;justify-self:start;font-size:12px;color:#ead099}
  .group-month input{min-width:180px;min-height:43px}
  @media(max-width:650px){.group-operation-tabs button,.group-operation-tabs a{flex:1;font-size:13px}}
</style>
