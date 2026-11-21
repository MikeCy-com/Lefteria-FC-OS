<script lang="ts">
  import {getContext,untrack} from 'svelte';
  import {browser} from '$app/env';
  import {goto} from '$app/navigation';
  import {page as routePage} from '$app/state';
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
  let showCreate=$state(false),creating=$state(false);
  let requestedCreateOpened=false;
  let username=$state(''),displayName=$state(''),password=$state(''),confirmPassword=$state('');
  let accountRole=$state('coach'),selectedOwners=$state<string[]>([]);
  let createError=$state(''),createNotice=$state('');
  let statusPending=$state(''),statusError=$state(''),statusNotice=$state('');
  let rolePending=$state(''),roleError=$state(''),roleNotice=$state('');
  let roleDrafts=$state<Record<string,string>>({});
  let provisionedAccountId=$state(''),provisionedOwner=$state(''),provisionedRole=$state('');
  const availableOwners=$derived($auth.session?.user.organizations.filter(o=>
    o.id==='lefteria_fc'||o.id==='lefteria_fc_academy')??[]);
  const roleChoices=[['manager','Manager'],['coach','Coach'],['editor','Editor'],
    ['marketing','Marketing'],['matchday_editor','Matchday editor'],['accountant','Accountant'],['cashier','Cashier · ένα ταμείο']] as const;
  function ownerToggle(id:string,checked:boolean){
    selectedOwners=checked?[...new Set([...selectedOwners,id])]:selectedOwners.filter(x=>x!==id);
    createError='';createNotice='';
  }
  async function createAccount(){
    if(!browser||!permitted||creating||!showCreate)return;
    const normalized=username.trim().toLowerCase(),name=displayName.trim();
    const bytes=new TextEncoder().encode(password).length;
    if(!/^[a-z0-9][a-z0-9._@+\-]{2,79}$/.test(normalized)||!name||name.length>200||
      password.length<12||password.length>72||bytes>72||password!==confirmPassword||
      selectedOwners.length<1||selectedOwners.length>2||
      (accountRole==='cashier'&&selectedOwners.length!==1)||
      !selectedOwners.every(id=>availableOwners.some(org=>org.id===id))){
      createError='Ελέγξτε χρήστη, όνομα, οργανισμό και δύο ίδιους ισχυρούς κωδικούς (12–72 χαρακτήρες, έως 72 bytes).';
      return;
    }
    const actor=$auth.session?.user.id;
    const body={username:normalized,name,role:accountRole,password,organizations:selectedOwners};
    creating=true;createError='';createNotice='';
    provisionedAccountId='';provisionedOwner='';provisionedRole='';
    try{
      const response=await fetch('/api/admin/access/users',{
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify(body)
      });
      if(actor!==$auth.session?.user.id)return;
      if(response.status===401){auth.invalidateSession();throw Error('Η σύνδεση έληξε.');}
      if(response.status===403){void auth.bootstrap();throw Error('Τα δικαιώματα άλλαξαν.');}
      if(response.status===409)throw Error('Το όνομα χρήστη χρησιμοποιείται ήδη.');
      if(response.status===503)throw Error('Η ασφαλής δημιουργία λογαριασμών δεν είναι ενεργή σε αυτό το περιβάλλον.');
      if(response.status!==201)throw Error('Η δημιουργία λογαριασμού απέτυχε.');
      const saved:unknown=await response.json();
      if(!saved||typeof saved!=='object'||Array.isArray(saved)||
        !('id' in saved)||typeof saved.id!=='string'||
        !('username' in saved)||saved.username!==normalized||
        !('role' in saved)||saved.role!==accountRole||
        !('organizations' in saved)||!Array.isArray(saved.organizations)||
        saved.organizations.length!==selectedOwners.length||
        !selectedOwners.every(owner=>(saved.organizations as string[]).includes(owner)))
        throw Error('Ο λογαριασμός δεν επιβεβαιώθηκε.');
      createNotice='Ο λογαριασμός δημιουργήθηκε με τον επιλεγμένο ρόλο. Μεταδώστε τον αρχικό κωδικό στον χρήστη μέσω ασφαλούς καναλιού.';
      // A role plus organization does not automatically grant access to a
      // particular city, location or team. Send the Super Admin to the
      // existing independently authorized scope assignment operation.
      provisionedAccountId=saved.id as string;
      provisionedOwner=selectedOwners[0];
      provisionedRole=accountRole;
      username='';displayName='';selectedOwners=[];showCreate=false;offset=0;revision++;
    }catch(e){createError=e instanceof Error?e.message:'Ο λογαριασμός δεν δημιουργήθηκε.';}
    finally{password='';confirmPassword='';creating=false;}
  }


  async function setAccountActive(account:Entry){
    if(!browser||!permitted||statusPending||rolePending||loading||!page ||
      account.id===$auth.session?.user.id||account.role==='super_admin'||
      account.invitation_pending||!page.accounts.some(item=>item.id===account.id))return;
    const actor=$auth.session?.user.id;
    const next=!account.active;
    if(!window.confirm(next
      ? 'Επανενεργοποίηση «'+account.name+'» με τους προηγούμενους ρόλους και οργανισμούς; Οι παλιές συνεδρίες παραμένουν ανακλημένες.'
      : 'Απενεργοποίηση «'+account.name+'»; Όλες οι τρέχουσες συνεδρίες θα ανακληθούν και η σύνδεση θα απαγορευτεί.'))return;
    statusPending=account.id;statusError='';statusNotice='';
    try{
      const response=await fetch('/api/admin/access/users/status',{
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({id:account.id,expected_role:account.role,
          expected_active:account.active,active:next})
      });
      if(actor!==$auth.session?.user.id)return;
      if(response.status===401){auth.invalidateSession();throw Error('Η σύνδεση έληξε.');}
      if(response.status===403){void auth.bootstrap();throw Error('Η πρόσβαση άλλαξε ή ο λογαριασμός δεν επιτρέπεται.');}
      if(response.status===409){revision++;throw Error('Η κατάσταση άλλαξε. Ανανεώστε τον κατάλογο.');}
      if(!response.ok)throw Error('Η αλλαγή κατάστασης δεν αποθηκεύτηκε.');
      const result:unknown=await response.json();
      if(!result||typeof result!=='object'||Array.isArray(result)||
        !('id' in result)||result.id!==account.id||
        !('role' in result)||result.role!==account.role||
        !('active' in result)||result.active!==next)
        throw Error('Η αλλαγή κατάστασης δεν επαληθεύτηκε.');
      statusNotice=next?'Ο λογαριασμός επανενεργοποιήθηκε.':'Ο λογαριασμός απενεργοποιήθηκε και οι συνεδρίες του ανακλήθηκαν.';
      revision++;
    }catch(e){statusError=e instanceof Error?e.message:'Η αλλαγή κατάστασης απέτυχε.';}
    finally{statusPending='';}
  }

  async function changeAccountRole(account:Entry){
    const nextRole=roleDrafts[account.id]??account.role;
    if(!browser||!permitted||rolePending||statusPending||loading||!page ||
      account.id===$auth.session?.user.id||account.role==='super_admin'||
      account.invitation_pending||nextRole===account.role||
      !roleChoices.some(([value])=>value===nextRole)||
      !page.accounts.some(item=>item.id===account.id&&item.role===account.role&&
        item.active===account.active))return;
    const actor=$auth.session?.user.id;
    if(!window.confirm('Αλλαγή ρόλου «'+account.name+'» από '+
      account.role+' σε '+nextRole+'; Οι ενεργές συνεδρίες θα ανακληθούν. '+
      'Εάν υπάρχουν αναθέσεις πόλης, τοποθεσίας, ομάδας, προσωπικού ή ενεργό POS, '+
      'η αλλαγή θα απορριφθεί μέχρι να αφαιρεθούν χωριστά.'))return;
    rolePending=account.id;roleError='';roleNotice='';
    try{
      const response=await fetch('/api/admin/access/users/role',{
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({id:account.id,expected_role:account.role,
          expected_active:account.active,next_role:nextRole})
      });
      if(actor!==$auth.session?.user.id)return;
      if(response.status===401){auth.invalidateSession();throw Error('Η σύνδεση έληξε.');}
      if(response.status===403){void auth.bootstrap();throw Error('Η πρόσβαση δεν επιτρέπεται για αυτόν τον λογαριασμό.');}
      if(response.status===409){
        const body:unknown=await response.json().catch(()=>null);
        if(body&&typeof body==='object'&&'code' in body&&
          body.code==='managed_admin_role_assignments_exist')
          throw Error('Αφαιρέστε τις αναθέσεις πόλης, τοποθεσίας, ομάδας και προσωπικού και ανακαλέστε τυχόν ενεργούς χειριστές POS.');
        revision++;throw Error('Ο λογαριασμός άλλαξε. Ανανεώστε και επιλέξτε ξανά ρόλο.');
      }
      if(response.status===503)throw Error('Η ασφαλής αλλαγή ρόλων δεν είναι διαθέσιμη.');
      if(!response.ok)throw Error('Δεν ήταν δυνατή η αλλαγή ρόλου.');
      const result:unknown=await response.json();
      if(!result||typeof result!=='object'||Array.isArray(result)||
        !('id' in result)||result.id!==account.id||
        !('role' in result)||result.role!==nextRole||
        !('active' in result)||result.active!==account.active)
        throw Error('Ο νέος ρόλος δεν επαληθεύτηκε.');
      roleDrafts={...roleDrafts,[account.id]:nextRole};
      roleNotice='Ο ρόλος άλλαξε. Οι παλιές συνεδρίες ανακλήθηκαν και απαιτείται νέα σύνδεση.';
      revision++;
    }catch(e){roleError=e instanceof Error?e.message:'Η αλλαγή ρόλου απέτυχε.';}
    finally{rolePending='';}
  }

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
    // The "new staff/account" action is available only to the actual
    // Super Admin; direct navigation does not change authorization.
    if(!browser||!permitted||requestedCreateOpened||
      routePage.url.searchParams.get('new')!=='1')return;
    requestedCreateOpened=true;
    showCreate=true;
  });

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
      <p class="admin-muted">Δημιουργία χρηστών με ισχυρό αρχικό κωδικό, ρόλο και οργανισμούς.
        Μόνο Super Admin μπορεί να απενεργοποιεί ή επανενεργοποιεί υπάρχοντες λογαριασμούς.
        Αλλαγή ρόλου επιτρέπεται αφού αφαιρεθούν οι παλιές αναθέσεις πόλης,
        τοποθεσίας, ομάδας και προσωπικού και ανακληθούν οι ενεργοί χειριστές POS. Οι συνεδρίες ανακαλούνται αυτόματα.
        Η πρόσκληση παραμένει εκτός λειτουργίας.
        Ο κατάλογος και τα σύνολα περιλαμβάνουν μόνο λογαριασμούς των οποίων
        όλοι οι οργανισμοί καλύπτονται από τις δικές σας εξουσιοδοτήσεις.</p>
      {#if !permitted}
        <div class="admin-panel" role="alert" data-testid="admin-users-forbidden">
          Απαιτείται επαληθευμένος ρόλος Super Admin.
        </div>
      {:else}
        <section class="admin-panel access-create-panel" data-testid="admin-managed-user-creation">
          <div class="access-create-head">
            <div><p class="admin-eyebrow">SUPER ADMIN · ΠΡΟΣΩΠΙΚΟ</p>
              <h2>Νέος λογαριασμός διαχείρισης</h2></div>
            <button type="button" class="admin-secondary" disabled={creating}
              onclick={()=>{showCreate=!showCreate;createError='';}}
              aria-expanded={showCreate} data-testid="admin-create-account-toggle">
              {showCreate?'Κλείσιμο':'Δημιουργία χρήστη'}
            </button>
          </div>
          {#if createNotice}<p class="access-create-success" role="status" data-testid="admin-created-account">{createNotice}</p>{/if}
          {#if provisionedAccountId &&
            ['manager','coach','editor','matchday_editor'].includes(provisionedRole)}
            <a class="admin-provision-follow-up"
              href={`/admin/territories?organization_id=${encodeURIComponent(provisionedOwner)}&account_id=${encodeURIComponent(provisionedAccountId)}#delegated-access`}
              data-testid="admin-new-account-scope-link">
              Επόμενο βήμα · ανάθεση πόλης, τοποθεσίας ή ομάδας ↗
            </a>
          {/if}
          {#if createError}<p role="alert" class="access-create-error">{createError}</p>{/if}
          {#if showCreate}
            <form class="access-create-form" data-testid="admin-create-account-form"
              onsubmit={e=>{e.preventDefault();void createAccount();}}>
              <label>Όνομα εμφάνισης
                <input type="text" bind:value={displayName} required maxlength="200" autocomplete="off" disabled={creating}/>
              </label>
              <label>Όνομα χρήστη
                <input type="text" bind:value={username} required minlength="3" maxlength="80"
                  autocomplete="off" autocapitalize="none" spellcheck="false" disabled={creating}/>
              </label>
              <label>Ρόλος πρόσβασης
                <select bind:value={accountRole} disabled={creating}>
                  {#each roleChoices as [id,label] (id)}<option value={id}>{label}</option>{/each}
                </select>
              </label>
              <fieldset disabled={creating} class="access-owner-options">
                <legend>Οργανισμοί · Επιλέξτε τουλάχιστον έναν</legend>
                {#each availableOwners as org (org.id)}
                  <label><input type="checkbox" checked={selectedOwners.includes(org.id)}
                    onchange={e=>ownerToggle(org.id,e.currentTarget.checked)}/>{org.name}</label>
                {/each}
              </fieldset>
              <label>Αρχικός κωδικός
                <input type="password" bind:value={password} required minlength="12" maxlength="72"
                  autocomplete="new-password" disabled={creating}/>
              </label>
              <label>Επιβεβαίωση κωδικού
                <input type="password" bind:value={confirmPassword} required minlength="12" maxlength="72"
                  autocomplete="new-password" disabled={creating}/>
              </label>
              <p class="admin-muted">Ο κωδικός αποθηκεύεται ως bcrypt hash και δεν εμφανίζεται ξανά.
                Δώστε τον στον χρήστη μέσω ασφαλούς καναλιού. Αναθέσεις πόλης/τοποθεσίας
                πραγματοποιούνται χωριστά από τις Ρυθμίσεις → Πόλεις & τοποθεσίες.</p>
              <button class="admin-secondary" type="submit" disabled={creating||selectedOwners.length===0}
                data-testid="admin-managed-user-submit">
                {creating?'Δημιουργία…':'Αποθήκευση λογαριασμού & ρόλου'}
              </button>
            </form>
          {/if}
        </section>
        {#if roleError}<p role="alert" class="access-create-error" data-testid="admin-account-role-error">{roleError}</p>{/if}
        {#if roleNotice}<p role="status" class="access-create-success" data-testid="admin-account-role-success">{roleNotice}</p>{/if}
        {#if statusError}<p role="alert" class="access-create-error" data-testid="admin-account-status-error">{statusError}</p>{/if}
        {#if statusNotice}<p role="status" class="access-create-success" data-testid="admin-account-status-success">{statusNotice}</p>{/if}
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
                      {#if account.role!=='super_admin' && !account.invitation_pending &&
                        account.id!==$auth.session.user.id}
                        <button type="button" class="admin-secondary access-status-button"
                          data-testid="admin-account-status-toggle"
                          disabled={!!statusPending||!!rolePending||loading}
                          onclick={()=>void setAccountActive(account)}>
                          {statusPending===account.id?'Αποθήκευση…':account.active?'Απενεργοποίηση':'Επανενεργοποίηση'}
                        </button>
                        <div class="access-role-controls">
                          <label for={'change-role-'+account.id}>Αλλαγή ρόλου</label>
                          <select id={'change-role-'+account.id}
                            data-testid="admin-account-role-select"
                            value={roleDrafts[account.id]??account.role}
                            disabled={!!rolePending||!!statusPending||loading}
                            onchange={e=>{
                              roleDrafts={...roleDrafts,[account.id]:e.currentTarget.value};
                              roleError='';roleNotice='';
                            }}>
                            {#each roleChoices as [value,label] (value)}
                              <option value={value}>{label}</option>
                            {/each}
                          </select>
                          <button type="button" class="admin-secondary access-status-button"
                            data-testid="admin-account-role-submit"
                            disabled={!!rolePending||!!statusPending||loading||
                              (roleDrafts[account.id]??account.role)===account.role}
                            onclick={()=>void changeAccountRole(account)}>
                            {rolePending===account.id?'Αποθήκευση…':'Αποθήκευση ρόλου'}
                          </button>
                        </div>
                      {/if}
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
  .admin-provision-follow-up{display:inline-flex;align-items:center;min-height:44px;padding:10px 15px;margin:5px 0 14px;border:1px solid #c69b41;background:#30240e;color:#ffd271;font-weight:800;text-decoration:none}
  .admin-provision-follow-up:focus-visible{outline:3px solid #ffd271;outline-offset:3px}
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
  .access-status-button{justify-self:start;margin-top:5px;min-height:44px;font-size:13px}
  .access-role-controls{display:grid;gap:6px;min-width:0;margin-top:9px}
  .access-role-controls label{color:#d0d6d7;font-size:13px;font-weight:700}
  .access-role-controls select{width:100%;max-width:250px;min-height:44px;background:#172024;color:#fff;border:1px solid #667477;border-radius:6px;padding:7px}
  .access-pages span{margin-right:auto}
  @media(max-width:690px){.access-list li{grid-template-columns:1fr 1fr}.access-organizations{grid-column:1/-1}}
  @media(max-width:390px){.access-list li{grid-template-columns:1fr}.access-organizations{grid-column:auto}}

  .access-create-panel{margin:20px 0 10px}
  .access-create-head{display:flex;align-items:center;justify-content:space-between;gap:16px;flex-wrap:wrap}
  .access-create-head h2{margin:4px 0 0}
  .access-create-form{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px;max-width:850px;margin-top:20px}
  .access-create-form>label{display:flex;flex-direction:column;gap:7px;color:#ddd;font-size:13px;font-weight:700}
  .access-create-form :is(input,select){min-height:46px;width:100%;background:#090b0c;color:#fff;
    border:1px solid #857a65;border-radius:4px;padding:9px 12px;font:inherit}
  .access-create-form .access-owner-options{display:flex;flex-wrap:wrap;gap:12px;
    margin:0;border:1px solid #77633e;align-items:center;padding:14px}
  .access-owner-options legend{font-size:13px;color:#ffcb62}
  .access-owner-options label{display:flex;align-items:center;gap:8px;font-size:13px}
  .access-owner-options input[type="checkbox"]{min-height:20px;width:20px;accent-color:#e6b641}
  .access-create-form>.admin-muted{grid-column:1/-1;font-size:12px;line-height:1.6}
  .access-create-form>button{grid-column:1/-1;justify-self:start;min-height:46px}
  .access-create-success{margin:16px 0;color:#d7ffc9;border-left:3px solid #a5d57c;padding:12px 15px}
  .access-create-error{margin:16px 0;color:#ffb5ad;border-left:3px solid #ff9186;padding:12px 15px}
  @media(max-width:730px){.access-create-form{grid-template-columns:1fr}}
</style>
