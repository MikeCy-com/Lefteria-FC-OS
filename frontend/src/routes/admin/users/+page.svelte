<script lang="ts">
  import {getContext,untrack} from 'svelte';
  import {browser} from '$app/env';
  import {goto} from '$app/navigation';
  import {ADMIN_AUTH_CONTEXT,type AdminAuthController} from '../../../lib/admin/controller.ts';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';
  import {ADMIN_ROLE_NAMES,isAdminRole} from '../../../lib/admin/permissions.ts';

  type Entry={id:string;name:string;role:string;active:boolean;
    invitation_pending:boolean;organizations:string[]};
  type UserPage={offset:number;limit:number;total:number;accounts:Entry[]};
  const auth=getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  const permitted=$derived($auth.status==='authenticated'&&
    $auth.session?.user.role==='super_admin');
  const pageSize=20;
  let offset=$state(0);
  let revision=$state(0);
  let page=$state<UserPage|null>(null);
  let loading=$state(false);
  let error=$state('');

  const organizationName=(id:string)=>id==='lefteria_fc'?'Lefteria FC':
    id==='lefteria_fc_academy'?'Lefteria FC Academy':'Μη επαληθευμένος οργανισμός';
  function isValid(value:unknown,wantedOffset:number):value is UserPage {
    if(!value||typeof value!=='object'||Array.isArray(value))return false;
    const p=value as Record<string,unknown>;
    if(p.offset!==wantedOffset||p.limit!==pageSize||
      !Number.isSafeInteger(p.total)||(p.total as number)<0||
      !Array.isArray(p.accounts)||p.accounts.length>pageSize)return false;
    const ids=new Set<string>();
    return p.accounts.every((entry:unknown)=>{
      if(!entry||typeof entry!=='object'||Array.isArray(entry))return false;
      const item=entry as Record<string,unknown>;
      if(typeof item.id!=='string'||!/^[0-9a-f]{8}-[0-9a-f-]{27,}$/.test(item.id)||
         ids.has(item.id)||typeof item.name!=='string'||item.name.length>200||
         !isAdminRole(item.role)||typeof item.active!=='boolean'||
         typeof item.invitation_pending!=='boolean'||
         !Array.isArray(item.organizations)||item.organizations.length>2||
         !item.organizations.every((owner:unknown)=>owner==='lefteria_fc'||owner==='lefteria_fc_academy'))return false;
      ids.add(item.id);
      return true;
    });
  }
  $effect(()=>{if(browser&&$auth.status==='anonymous')void goto('/admin/login',{replace:true});});
  $effect(()=>{
    const authorized=permitted,actor=$auth.session?.user.id,wantedOffset=offset,changed=revision;
    void actor;void changed;
    page=null;loading=false;error='';
    if(!browser||!authorized)return;
    const controller=new AbortController();
    loading=true;
    untrack(()=>void(async()=>{
      try {
        const query=new URLSearchParams({offset:String(wantedOffset),limit:String(pageSize)});
        const response=await fetch('/api/admin/access/users?'+query,{
          credentials:'same-origin',cache:'no-store',signal:controller.signal,
          headers:{Accept:'application/json'}
        });
        if(controller.signal.aborted)return;
        if(response.status===401){auth.invalidateSession();return;}
        if(response.status===403){void auth.bootstrap();throw new Error('Τα δικαιώματα πρόσβασης άλλαξαν.');}
        if(!response.ok)throw new Error('Ο κατάλογος λογαριασμών δεν είναι διαθέσιμος.');
        const raw:unknown=await response.json();
        if(!isValid(raw,wantedOffset))throw new Error('Μη επαληθευμένη απάντηση λογαριασμών.');
        if(!controller.signal.aborted)page=raw;
      }catch(e){
        if(!controller.signal.aborted)error=e instanceof Error?e.message:'Σφάλμα καταλόγου.';
      }finally{if(!controller.signal.aborted)loading=false;}
    })());
    return()=>controller.abort();
  });
</script>

<svelte:head>
  <title>Χρήστες & δικαιώματα | Διαχείριση Λευτεριά FC</title>
  <meta name="robots" content="noindex,nofollow"/>
</svelte:head>
{#if $auth.status==='authenticated'&&$auth.session}
  <AdminFrame user={$auth.session.user} active="users"
    busy={$auth.busy} onLogout={()=>void auth.logout()}>
    <section class="access-directory" data-testid="admin-users-directory">
      <p class="admin-eyebrow">ΟΡΓΑΝΩΣΗ · ΛΟΓΑΡΙΑΣΜΟΙ ΔΙΑΧΕΙΡΙΣΗΣ</p>
      <h1>Χρήστες & δικαιώματα</h1>
      <p class="admin-muted">Ασφαλής, μόνο για ανάγνωση, κατάλογος λογαριασμών προσωπικού.
        Οι ρόλοι, οι ενεργοί λογαριασμοί και οι οργανισμοί προέρχονται από τον διακομιστή.
        Προσθήκη, αλλαγή ρόλου, πρόσκληση και ανάκληση πρόσβασης δεν είναι ακόμη διαθέσιμες.</p>
      {#if !permitted}
        <div class="admin-panel" role="alert" data-testid="admin-users-forbidden">
          Απαιτείται επαληθευμένος ρόλος Super Admin.
        </div>
      {:else}
        <div class="access-toolbar">
          <button type="button" class="admin-secondary" disabled={loading}
            onclick={()=>revision++}>Ανανέωση καταλόγου</button>
          {#if page}<span role="status">{page.total} λογαριασμοί</span>{/if}
        </div>
        {#if loading}
          <div class="admin-panel" role="status">Φόρτωση επαληθευμένων χρηστών…</div>
        {:else if error}
          <div class="admin-panel" role="alert">
            <p>{error}</p>
            <button type="button" class="admin-secondary" onclick={()=>revision++}>Δοκίμασε ξανά</button>
          </div>
        {:else if page}
          <div class="admin-panel">
            {#if page.accounts.length===0}
              <p>Δεν βρέθηκαν λογαριασμοί σε αυτή τη σελίδα.</p>
            {:else}
              <ul class="access-list" data-testid="admin-users-list">
                {#each page.accounts as account (account.id)}
                  <li>
                    <div><strong>{account.name || 'Χωρίς εμφανιζόμενο όνομα'}</strong>
                      <span>{ADMIN_ROLE_NAMES[account.role as keyof typeof ADMIN_ROLE_NAMES]}</span></div>
                    <div class="access-status">
                      <span>{account.active?'Ενεργός':'Ανενεργός'}</span>
                      {#if account.invitation_pending}<span>Εκκρεμεί πρόσκληση</span>{/if}
                    </div>
                    <div class="access-organizations">
                      {#each account.organizations as org (org)}
                        <span>{organizationName(org)}</span>
                      {/each}
                      {#if !account.organizations.length}<span>Χωρίς οργανισμό</span>{/if}
                    </div>
                  </li>
                {/each}
              </ul>
            {/if}
            <nav class="access-pages" aria-label="Σελίδες λογαριασμών">
              <span>{page.total===0?0:offset+1}–{Math.min(offset+page.accounts.length,page.total)} από {page.total}</span>
              <button type="button" class="admin-secondary" disabled={loading||offset===0}
                onclick={()=>offset=Math.max(0,offset-pageSize)}>Προηγούμενη</button>
              <button type="button" class="admin-secondary" disabled={loading||offset+pageSize>=page.total}
                onclick={()=>offset+=pageSize}>Επόμενη</button>
            </nav>
          </div>
        {/if}
      {/if}
    </section>
  </AdminFrame>
{:else}
  <AdminSessionStatus unavailable={$auth.status==='unavailable'} busy={$auth.busy}
    error={$auth.error} onRetry={()=>void auth.bootstrap()} onLogout={()=>void auth.logout()}/>
{/if}
<style>
  .access-directory{width:min(100%,1100px);margin:0 auto}
  .access-directory h1{margin:8px 0 13px}
  .access-directory>.admin-muted{max-width:85ch;line-height:1.65}
  .access-toolbar,.access-pages{display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:12px;margin:18px 0}
  .access-list{list-style:none;margin:0;padding:0}
  .access-list li{display:grid;grid-template-columns:1.2fr 1fr 1.5fr;gap:14px;align-items:start;border-bottom:1px solid #35383a;padding:17px 0}
  .access-list li>div{display:grid;gap:5px;min-width:0}
  .access-list strong{font-size:16px;overflow-wrap:anywhere}
  .access-list span{font-size:14px;color:#c4c9c9}
  .access-status span:first-child{color:#f1c65d}
  .access-pages span{margin-right:auto}
  @media(max-width:690px){.access-list li{grid-template-columns:1fr 1fr}.access-organizations{grid-column:1/-1}}
  @media(max-width:390px){.access-list li{grid-template-columns:1fr}.access-organizations{grid-column:auto}}
</style>
