<script lang="ts">
  import { getContext } from 'svelte';
  import { browser } from '$app/env';
  import { goto } from '$app/navigation';
  import { ADMIN_AUTH_CONTEXT, type AdminAuthController } from '../../../lib/admin/controller.ts';
  import { hasAdminPermission } from '../../../lib/admin/permissions.ts';
  import { adminOrganizationsFor } from '../../../lib/admin/admin-organization.ts';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';
  import AdminCoachDirectoryPanel from '../../../lib/admin/AdminCoachDirectoryPanel.svelte';

  type Owner = 'lefteria_fc' | 'lefteria_fc_academy';
  type Group = {id:string;name:string;kind:'team'|'academy_group';active_players:number};
  type Directory = {organization_id:Owner;ready:true;groups:Group[]};
  const auth = getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  // Only a local staff workflow selector. Never changes the admin-wide scope.
  const authorizedOwners = $derived($auth.session && ['manager','super_admin'].includes($auth.session.user.role)
    ? adminOrganizationsFor($auth.session.user).filter(id=>
        hasAdminPermission($auth.session!.user.permissions,id==='lefteria_fc'?'teams:write':'academy:write'))
    : []);
  let selectedOwner = $state<Owner|null>(null);
  const owner = $derived(selectedOwner && authorizedOwners.includes(selectedOwner)
    ? selectedOwner : authorizedOwners[0]??null);
  const permitted = $derived($auth.status==='authenticated' && !!$auth.session &&
    !!owner && authorizedOwners.includes(owner));
  let groups = $state<Group[]>([]);
  let loading = $state(false);
  let error = $state('');
  let retry = $state(0);

  function validDirectory(raw:unknown, expected:Owner): raw is Directory {
    if(!raw || typeof raw!=='object' || Array.isArray(raw))return false;
    const value = raw as Record<string,unknown>;
    const kind = expected==='lefteria_fc'?'team':'academy_group';
    const ids = new Set<string>();
    return value.ready===true && value.organization_id===expected &&
      Array.isArray(value.groups) && value.groups.length<=150 &&
      value.groups.every((entry:unknown)=>{
        if(!entry || typeof entry!=='object' || Array.isArray(entry))return false;
        const group=entry as Record<string,unknown>;
        if(typeof group.id!=='string'||group.id.length<1||group.id.length>128||ids.has(group.id))return false;
        ids.add(group.id);
        return typeof group.name==='string'&&group.name.length>0&&group.name.length<=200 &&
          group.kind===kind && Number.isSafeInteger(group.active_players) &&
          (group.active_players as number)>=0;
      });
  }

  $effect(()=>{
    if(browser && $auth.status==='anonymous')void goto('/admin/login',{replace:true});
  });
  $effect(()=>{
    const allowed=permitted,activeOwner=owner,actor=$auth.session?.user.id,generation=retry;
    void actor;void generation;
    groups=[];error='';loading=false;
    if(!browser||!allowed||(activeOwner!=='lefteria_fc'&&activeOwner!=='lefteria_fc_academy'))return;
    const request=new AbortController();
    loading=true;
    void(async()=>{
      try {
        const params=new URLSearchParams({organization_id:activeOwner});
        const response=await fetch('/api/admin/football/audiences?'+params,{
          credentials:'same-origin',cache:'no-store',signal:request.signal,
          headers:{Accept:'application/json'}
        });
        if(request.signal.aborted)return;
        if(response.status===401){auth.invalidateSession();return;}
        if(response.status===403){void auth.bootstrap();throw new Error('Τα δικαιώματα άλλαξαν.');}
        if(!response.ok)throw new Error('Τα επαληθευμένα τμήματα δεν είναι διαθέσιμα.');
        const raw:unknown=await response.json();
        if(!validDirectory(raw,activeOwner))throw new Error('Μη επαληθευμένη λίστα τμημάτων.');
        if(!request.signal.aborted)groups=raw.groups;
      }catch(e){
        if(!request.signal.aborted)error=e instanceof Error?e.message:'Σφάλμα φόρτωσης τμημάτων.';
      }finally{
        if(!request.signal.aborted)loading=false;
      }
    })();
    return()=>request.abort();
  });
</script>

<svelte:head>
  <title>Προσωπικό & προπονητές | Διαχείριση Λευτεριά FC</title>
  <meta name="robots" content="noindex,nofollow" />
</svelte:head>

{#if $auth.status==='authenticated' && $auth.session}
  <AdminFrame user={$auth.session.user} active="staff"
    busy={$auth.busy} onLogout={()=>void auth.logout()}>
    <section class="staff-workspace" data-testid="admin-staff-workspace">
      <p class="admin-eyebrow">ΔΙΑΧΕΙΡΙΣΗ · ΠΡΟΣΩΠΙΚΟ & ΠΡΟΠΟΝΗΤΕΣ</p>
      <h1>Προσωπικό & προπονητές</h1>
      <p class="admin-muted">Επαληθευμένος κατάλογος προπονητών και αναθέσεων στον επιλεγμένο οργανισμό.
        Οι αλλαγές τμημάτων αποθηκεύονται μόνο μέσω των προστατευμένων API με έλεγχο σύγκρουσης.
        Η δημιουργία προσωπικού, οι συμβάσεις και τα στοιχεία επικοινωνίας δεν υποστηρίζονται ακόμη.</p>
      {#if permitted && authorizedOwners.length > 1}
        <label class="staff-owner-selector">Οργανισμός αναθέσεων
          <select value={owner??''} data-testid="admin-staff-organization"
            onchange={event=>selectedOwner=event.currentTarget.value as Owner}>
            {#each authorizedOwners as option (option)}
              <option value={option}>{option==='lefteria_fc'?'Lefteria FC':'Lefteria FC Academy'}</option>
            {/each}
          </select>
        </label>
      {/if}
      {#if !permitted}
        <div class="admin-panel" role="alert" data-testid="admin-staff-denied">
          Δεν έχεις δικαίωμα διαχείρισης αναθέσεων για τον επιλεγμένο οργανισμό.
        </div>
      {:else if loading}
        <div class="admin-panel" role="status">Φόρτωση επαληθευμένων τμημάτων…</div>
      {:else if error}
        <div class="admin-panel" role="alert">
          <p>{error}</p>
          <button class="admin-secondary" type="button" onclick={()=>retry++}>Δοκίμασε ξανά</button>
        </div>
      {:else if owner==='lefteria_fc'||owner==='lefteria_fc_academy'}
        <AdminCoachDirectoryPanel {owner} actorId={$auth.session.user.id} {groups}
          onExpired={()=>auth.invalidateSession()} onRevoked={()=>void auth.bootstrap()} />
      {/if}
    </section>
  </AdminFrame>
{:else}
  <AdminSessionStatus unavailable={$auth.status==='unavailable'} busy={$auth.busy}
    error={$auth.error} onRetry={()=>void auth.bootstrap()}
    onLogout={()=>void auth.logout()} />
{/if}

<style>
  .staff-workspace{width:min(100%,1160px);margin:0 auto}
  .staff-workspace h1{margin:8px 0 14px}
  .staff-workspace>.admin-muted{max-width:85ch;line-height:1.65}
  .staff-owner-selector{display:grid;gap:8px;width:min(100%,360px);margin:20px 0;font-size:15px;color:#f7c653}
  .staff-owner-selector select{min-height:44px;padding:9px 12px;border:1px solid #6d552b;border-radius:8px;background:#181a1b;color:#fff}
</style>
