<script lang="ts">
  import {getContext,untrack} from 'svelte';
  import {browser} from '$app/env';
  import {goto} from '$app/navigation';
  import {ADMIN_AUTH_CONTEXT,type AdminAuthController} from '../../../lib/admin/controller.ts';
  import {hasAdminPermission} from '../../../lib/admin/permissions.ts';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';
  import {academyMonthWindow,isAcademyScheduleGroupList,isAcademyTrainingList,upcomingAcademySessions}
    from '../../../lib/admin/academy-schedule.ts';
  import type {AdminRosterGroup,AcademyTrainingList} from '../../../lib/admin/academy-schedule.ts';

  const auth=getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  $effect(()=>{if(browser&&$auth.status==='anonymous')void goto('/admin/login',{replace:true});});
  const permitted=$derived($auth.status==='authenticated'&&!!$auth.session&&
    ['manager','super_admin'].includes($auth.session.user.role)&&
    $auth.session.user.organizations.some(org=>org.id==='lefteria_fc_academy')&&
    hasAdminPermission($auth.session.user.permissions,'attendance:read'));
  let month=$state(new Intl.DateTimeFormat('en-CA',{timeZone:'Asia/Nicosia',year:'numeric',month:'2-digit'})
    .format(new Date()).slice(0,7));
  let groups=$state<AdminRosterGroup[]>([]);
  let groupId=$state('');
  let sessions=$state<AcademyTrainingList|null>(null);
  let loadingGroups=$state(false);
  let loadingSessions=$state(false);
  let error=$state('');
  let refresh=$state(0);
  const window=$derived(academyMonthWindow(month));
  const upcoming=$derived(sessions?upcomingAcademySessions(sessions,
    new Intl.DateTimeFormat('en-CA',{timeZone:'Asia/Nicosia',year:'numeric',month:'2-digit',day:'2-digit'}).format(new Date())):[]);

  $effect(()=>{
    const allowed=permitted,actor=$auth.session?.user.id;void actor;void refresh;
    groups=[];groupId='';sessions=null;
    if(!browser||!allowed)return;
    const ctrl=new AbortController();loadingGroups=true;error='';
    untrack(()=>{void(async()=>{
      try{
        const res=await fetch('/api/admin/football/audiences?organization_id=lefteria_fc_academy',{
          credentials:'same-origin',cache:'no-store',signal:ctrl.signal});
        if(ctrl.signal.aborted)return;
        if(res.status===401||res.status===403){void auth.bootstrap();throw Error('Η εξουσιοδότηση άλλαξε.');}
        if(!res.ok)throw Error('Δεν φορτώθηκαν τα τμήματα.');
        const data:unknown=await res.json();
        if(!isAcademyScheduleGroupList(data))throw Error('Μη επαληθευμένη λίστα τμημάτων.');
        if(!ctrl.signal.aborted){groups=data.groups;groupId=data.groups[0]?.id??'';}
      }catch(e){if(!ctrl.signal.aborted)error=e instanceof Error?e.message:'Πρόβλημα σύνδεσης.';}
      finally{if(!ctrl.signal.aborted)loadingGroups=false;}
    })();});
    return()=>ctrl.abort();
  });
  $effect(()=>{
    const allowed=permitted,actor=$auth.session?.user.id,group=groupId,dates=window;
    void actor;void refresh;sessions=null;
    if(!browser||!allowed||!group||!groups.some(g=>g.id===group)||!dates)return;
    const ctrl=new AbortController();loadingSessions=true;error='';
    untrack(()=>{void(async()=>{
      try{
        const params=new URLSearchParams({organization_id:'lefteria_fc_academy',
          group_id:group,start:dates.start,end:dates.end});
        const res=await fetch('/api/admin/academy/training/?'+params,{
          credentials:'same-origin',cache:'no-store',signal:ctrl.signal});
        if(ctrl.signal.aborted)return;
        if(res.status===401||res.status===403){void auth.bootstrap();throw Error('Τα δικαιώματα άλλαξαν.');}
        if(!res.ok)throw Error('Το επιλεγμένο πρόγραμμα δεν είναι διαθέσιμο.');
        const data:unknown=await res.json();
        if(!isAcademyTrainingList(data,group,dates.start,dates.end))
          throw Error('Μη επαληθευμένο πρόγραμμα προπονήσεων.');
        if(!ctrl.signal.aborted)sessions=data;
      }catch(e){if(!ctrl.signal.aborted)error=e instanceof Error?e.message:'Σφάλμα προπονήσεων.';}
      finally{if(!ctrl.signal.aborted)loadingSessions=false;}
    })();});
    return()=>ctrl.abort();
  });
</script>

<svelte:head><title>Πρόγραμμα προπονήσεων Ακαδημίας | Λευτεριά FC OS</title><meta name="robots" content="noindex"/></svelte:head>
{#if $auth.status==='authenticated'&&$auth.session}
  <AdminFrame user={$auth.session.user} active="groups" busy={$auth.busy} onLogout={()=>void auth.logout()}>
    <section class="academy-schedule" aria-labelledby="academy-schedule-title" data-testid="admin-academy-schedule">
      <p class="eyebrow">ΛΕΥΤΕΡΙΑ FC · ΑΚΑΔΗΜΙΑ</p>
      <h1 id="academy-schedule-title">Πρόγραμμα προπονήσεων</h1>
      <p class="intro">Πραγματικές προπονήσεις Ακαδημίας από το επαληθευμένο ημερολόγιο. Η δημιουργία και η αλλαγή προπονήσεων δεν έχουν ενεργοποιηθεί ακόμη.</p>
      {#if !permitted}
        <p role="alert">Η προβολή επιτρέπεται μόνο στην εξουσιοδοτημένη διαχείριση της Ακαδημίας.</p>
      {:else}
        <div class="filters">
          <label>Τμήμα
            <select bind:value={groupId} disabled={loadingGroups||loadingSessions}>
              {#if !groups.length}<option value="">Χωρίς διαθέσιμα τμήματα</option>{/if}
              {#each groups as group (group.id)}
                <option value={group.id}>{group.name}</option>
              {/each}
            </select>
          </label>
          <label>Μήνας
            <input type="month" bind:value={month} min="2024-01" max="2032-12"/>
          </label>
          <button type="button" onclick={()=>refresh++} disabled={loadingGroups||loadingSessions}>Ανανέωση</button>
        </div>
        {#if error}<p class="notice error" role="alert">{error}</p>{/if}
        {#if loadingGroups||loadingSessions}
          <p role="status">Φόρτωση εγκεκριμένου προγράμματος…</p>
        {:else if sessions}
          <p class="notice" role="status">{sessions.total} συνολικές καταχωρίσεις · {upcoming.length} επόμενες ενεργές</p>
          {#if sessions.sessions.length===0}
            <p class="notice">Δεν υπάρχουν καταχωρημένες προπονήσεις για αυτό το τμήμα και τον μήνα.</p>
          {:else}
            <ol class="schedule-list">
              {#each [...sessions.sessions].sort((a,b)=>a.day.localeCompare(b.day)||a.id.localeCompare(b.id)) as session (session.id)}
                <li class:cancelled={session.is_cancelled||session.is_archived}>
                  <time datetime={session.day}>{session.day}</time>
                  <strong>{session.title}</strong>
                  <span>{session.is_archived?'Αρχειοθετημένη':session.is_cancelled?'Ακυρωμένη':'Καταχωρισμένη'}</span>
                </li>
              {/each}
            </ol>
          {/if}
          <a class="go-attendance" href="/admin/attendance">Άνοιγμα παρουσιολογίου προπονήσεων ›</a>
        {:else if !loadingGroups&&!loadingSessions&&groups.length===0}
          <p class="notice">Δεν υπάρχουν διαθέσιμα τμήματα για τον συγκεκριμένο οργανισμό.</p>
        {/if}
      {/if}
    </section>
  </AdminFrame>
{:else}
  <AdminSessionStatus unavailable={$auth.status==='unavailable'} busy={$auth.busy}
    error={$auth.error} onRetry={()=>void auth.bootstrap()} onLogout={()=>void auth.logout()}/>
{/if}
<style>
  .academy-schedule{max-width:1100px;margin:0 auto;padding:22px 0 60px}
  .eyebrow{font-size:11px;color:#f5a623;letter-spacing:.1em}
  h1{font-family:'Fira Sans Extra Condensed',sans-serif}
  .intro,.notice{color:#b3b8bb;font-size:13px;line-height:1.7}
  .filters{display:flex;flex-wrap:wrap;align-items:end;gap:12px;margin:24px 0}
  .filters label{display:grid;gap:7px;color:#ffca46;font-size:12px;min-width:210px}
  .filters select,.filters input,.filters button{min-height:44px;background:#1d2021;color:#fff;border:1px solid #50422a;border-radius:7px;padding:9px}
  .filters button{color:#ffca46}
  .schedule-list{list-style:none;padding:0;display:grid;gap:9px}
  .schedule-list li{display:grid;grid-template-columns:minmax(95px,.5fr) minmax(140px,2fr) minmax(95px,1fr);gap:10px;align-items:center;background:#181a1b;border:1px solid #35383a;border-radius:9px;padding:13px}
  .schedule-list time{font-size:12px;color:#ffca46}
  .schedule-list strong{overflow-wrap:anywhere;font-size:14px}
  .schedule-list span{font-size:11px;color:#b7c0c2}
  .cancelled{opacity:.65}
  .go-attendance{display:inline-flex;align-items:center;color:#ffca46;min-height:44px;margin-top:18px}
  .error{color:#ffb1aa}
  @media(max-width:640px){.schedule-list li{grid-template-columns:1fr}.filters label{width:100%}}
</style>
