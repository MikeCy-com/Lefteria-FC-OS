<script lang="ts">
  import {getContext} from 'svelte';
  import {browser} from '$app/env';
  import {goto} from '$app/navigation';
  import {ADMIN_AUTH_CONTEXT,type AdminAuthController} from '../../../lib/admin/controller.ts';
  import {hasAdminPermission} from '../../../lib/admin/permissions.ts';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';

  type Owner='lefteria_fc'|'lefteria_fc_academy';
  type Assigned={id:string;name:string;kind:'team'|'academy_group'};
  type Directory={organization_id:Owner;ready:true;groups:Assigned[]};
  const auth=getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  const assignedOwners=$derived($auth.status==='authenticated' &&
    $auth.session?.user.role==='coach'
      ? $auth.session.user.organizations.filter((org):org is typeof org & {id:Owner} =>
          (org.id==='lefteria_fc' &&
            hasAdminPermission($auth.session!.user.permissions,'teams:read_assigned')) ||
          (org.id==='lefteria_fc_academy' &&
            hasAdminPermission($auth.session!.user.permissions,'academy:read_assigned')))
      : []);
  let owner=$state<Owner|null>(null);
  let directory=$state<Directory|null>(null);
  let loading=$state(false);
  let error=$state<string|null>(null);
  let refresh=$state(0);
  $effect(()=>{
    if(browser && $auth.status==='anonymous')void goto('/admin/login',{replace:true});
  });
  $effect(()=>{
    const choices=assignedOwners;
    if(!choices.some(item=>item.id===owner))owner=choices[0]?.id??null;
  });
  function validGroups(raw:unknown,expected:Owner):raw is Directory {
    if(!raw||typeof raw!=='object'||Array.isArray(raw))return false;
    const x=raw as Record<string,unknown>;
    if(x.organization_id!==expected||x.ready!==true ||
      !Array.isArray(x.groups)||x.groups.length>150)return false;
    const ids=new Set<string>();
    return x.groups.every(item=>{
      if(!item||typeof item!=='object'||Array.isArray(item))return false;
      const g=item as Record<string,unknown>;
      if(typeof g.id!=='string'||!g.id.length||g.id.length>128 ||
        ids.has(g.id)||typeof g.name!=='string'||!g.name.trim()||g.name.length>200 ||
        g.kind!==(expected==='lefteria_fc'?'team':'academy_group'))return false;
      ids.add(g.id);
      return true;
    });
  }
  $effect(()=>{
    const selected=owner,actor=$auth.session?.user.id??'';
    const permitted=assignedOwners.some(org=>org.id===selected);
    const generation=refresh;void generation;
    directory=null;loading=false;error=null;
    if(!browser||$auth.status!=='authenticated'||!actor||!selected||!permitted)return;
    const abort=new AbortController();
    loading=true;
    void (async()=>{
      try{
        const response=await fetch('/api/admin/football/assigned-groups?organization_id='+
          encodeURIComponent(selected),{
            credentials:'same-origin',cache:'no-store',signal:abort.signal
          });
        if(abort.signal.aborted)return;
        if(response.status===401){auth.invalidateSession();return;}
        if(response.status===403){void auth.bootstrap();throw new Error('Η ανάθεση ή τα δικαιώματά σας άλλαξαν.');}
        if(!response.ok)throw new Error('Οι ανατεθειμένες ομάδες δεν είναι διαθέσιμες.');
        const data:unknown=await response.json();
        if(!validGroups(data,selected))throw new Error('Μη επαληθευμένες αναθέσεις.');
        if(!abort.signal.aborted&&owner===selected &&
          $auth.session?.user.id===actor&&$auth.status==='authenticated')
          directory=data;
      }catch(e){
        if(!abort.signal.aborted&&owner===selected)
          error=e instanceof Error?e.message:'Αποτυχία ανάκτησης ομάδων.';
      }finally{if(!abort.signal.aborted&&owner===selected)loading=false;}
    })();
    return ()=>abort.abort();
  });
</script>

<svelte:head><title>Τα τμήματά μου | Διαχείριση Λευτεριά FC</title></svelte:head>

{#if $auth.status==='authenticated' && $auth.session}
  <AdminFrame user={$auth.session.user} active="assigned-groups" busy={$auth.busy}
    onLogout={()=>void auth.logout()}>
    <div class="coach-assigned-directory" data-testid="admin-coach-assigned-directory">
      <p class="admin-eyebrow">ΑΓΩΝΙΣΤΙΚΟ · ΑΝΑΘΕΣΕΙΣ ΠΡΟΠΟΝΗΤΗ</p>
      <h1>Τα τμήματά μου</h1>
      <p class="admin-muted">Μόνο οι ομάδες που είναι ρητά συνδεδεμένες με τον ενεργό
        λογαριασμό προπονητή σας. Δεν εμφανίζονται παίκτες, οικογένειες,
        αριθμοί συμμετοχών ή εργαλεία διαχείρισης άλλων ομάδων.</p>
      {#if assignedOwners.length===0}
        <section class="admin-panel" role="status" data-testid="coach-assigned-denied">
          <p>Δεν υπάρχει ενεργή εξουσιοδότηση προπονητή σε οργανισμό.</p>
          <a href="/admin/profile#access">Οι αναθέσεις & τα δικαιώματά μου</a>
        </section>
      {:else}
        <section class="admin-panel coach-assigned-controls" aria-label="Επιλογή εξουσιοδοτημένου οργανισμού">
          {#if assignedOwners.length>1}
            <label for="coach-assigned-owner">Οργανισμός</label>
            <select id="coach-assigned-owner" value={owner??''} disabled={loading}
              data-testid="coach-assigned-owner"
              onchange={event=>{
                const next=event.currentTarget.value;
                if(assignedOwners.some(org=>org.id===next))owner=next as Owner;
              }}>
              {#each assignedOwners as org(org.id)}
                <option value={org.id}>{org.name}</option>
              {/each}
            </select>
          {:else}
            <strong data-testid="coach-assigned-organization">{assignedOwners[0].name}</strong>
          {/if}
          <button type="button" class="admin-secondary" disabled={loading}
            onclick={()=>refresh++}>Ανανέωση αναθέσεων</button>
        </section>
        {#if loading || directory?.organization_id!==owner && directory!==null}
          <section class="admin-panel" role="status" data-testid="coach-assigned-loading">Επαλήθευση αναθέσεων…</section>
        {:else if error || !directory}
          <section class="admin-panel" role="alert" data-testid="coach-assigned-unavailable">
            {error??'Δεν ήταν δυνατή η ανάκτηση των αναθέσεων.'}
          </section>
        {:else if directory.groups.length===0}
          <section class="admin-panel" role="status" data-testid="coach-assigned-empty">
            Δεν υπάρχει ενεργή και επαληθευμένη σύνδεση λογαριασμού προπονητή με ομάδα
            στον επιλεγμένο οργανισμό. Ζητήστε από τον Super Admin να ελέγξει τη σύνδεση προσωπικού.
          </section>
        {:else}
          <section class="admin-panel" data-testid="coach-assigned-list">
            <h2>Επαληθευμένες ομάδες ({directory.groups.length})</h2>
            <ul class="coach-assigned-list">
              {#each directory.groups as group (group.id)}
                <li data-testid="coach-assigned-group">
                  <span>{group.kind==='team'?'ΠΡΩΤΗ ΟΜΑΔΑ':'ΑΚΑΔΗΜΙΑ'}</span>
                  <strong>{group.name}</strong>
                </li>
              {/each}
            </ul>
            <p class="admin-muted">Πρόκειται για ασφαλή προβολή αναθέσεων. Οι ενέργειες
              παρουσιών, προπονήσεων και αξιολογήσεων απαιτούν ξεχωριστή
              εξουσιοδότηση API ανά τμήμα και δεν ενεργοποιούνται από αυτή τη σελίδα.</p>
          </section>
        {/if}
      {/if}
    </div>
  </AdminFrame>
{:else}
  <AdminSessionStatus unavailable={$auth.status==='unavailable'} busy={$auth.busy}
    error={$auth.error} onRetry={()=>void auth.bootstrap()}
    onLogout={()=>void auth.logout()}/>
{/if}

<style>
  .coach-assigned-directory{display:grid;gap:17px;max-width:1100px}
  .coach-assigned-directory h1{margin:0}
  .coach-assigned-directory>p{margin:0}
  .coach-assigned-controls{display:flex;flex-wrap:wrap;align-items:center;gap:12px}
  .coach-assigned-controls select{min-height:44px;padding:9px 12px;max-width:100%;
    color:#fff;background:#111;border:1px solid #a17d3b;border-radius:5px}
  .coach-assigned-list{list-style:none;margin:15px 0;display:grid;gap:9px;padding:0}
  .coach-assigned-list li{display:grid;gap:5px;min-width:0;padding:14px 16px;
    border:1px solid #574520;background:#151515;border-radius:6px}
  .coach-assigned-list span{font-size:11px;font-weight:800;letter-spacing:.08em;color:#f6bb38}
  .coach-assigned-list strong{font-size:16px;overflow-wrap:anywhere}
  .coach-assigned-controls button:focus-visible,.coach-assigned-controls select:focus-visible{
    outline:3px solid #f6bb38;outline-offset:3px}
</style>
