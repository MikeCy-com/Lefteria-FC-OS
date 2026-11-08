<script lang="ts">
  import { getContext } from 'svelte';
  import { browser } from '$app/env';
  import { goto } from '$app/navigation';
  import { ADMIN_AUTH_CONTEXT, type AdminAuthController } from '../../../lib/admin/controller.ts';
  import { hasAdminPermission } from '../../../lib/admin/permissions.ts';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';

  type Organization='lefteria_fc'|'lefteria_fc_academy';
  type Group={id:string;name:string;kind:string;active_players:number};
  type Directory={organization_id:Organization;ready:true;groups:Group[]};
  type Draft={id:string;organization_id:Organization;group_id:string;title:string;body:string;status:'draft';version:number};
  type Published={id:string;organization_id:Organization;group_id:string;title:string;body:string;
    published_at:string;status:'published';version:number};
  const auth=getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  const permitted=$derived($auth.status==='authenticated' && $auth.session &&
    ['manager','super_admin'].includes($auth.session.user.role)
    ? $auth.session.user.organizations.filter((org):org is typeof org & {id:Organization} =>
        (org.id==='lefteria_fc' && hasAdminPermission($auth.session!.user.permissions,'teams:write')) ||
        (org.id==='lefteria_fc_academy' && hasAdminPermission($auth.session!.user.permissions,'academy:write')))
    : []);
  let organization=$state<Organization|null>(null);
  let groups=$state<Group[]>([]);
  let drafts=$state<Draft[]>([]);
  let published=$state<Published[]>([]);
  let groupId=$state('');
  let title=$state('');
  let body=$state('');
  let busy=$state(false);
  let loading=$state(false);
  let error=$state<string|null>(null);
  let success=$state<string|null>(null);
  let reload=$state(0);

  function validGroups(raw:unknown,owner:Organization):raw is Directory{
    if(!raw || typeof raw!=='object' || Array.isArray(raw))return false;
    const d=raw as Record<string,unknown>;
    return d.organization_id===owner && d.ready===true &&
      Array.isArray(d.groups) && d.groups.length<=150 &&
      d.groups.every((item:unknown)=>{
        if(!item || typeof item!=='object' || Array.isArray(item))return false;
        const g=item as Record<string,unknown>;
        return typeof g.id==='string' && g.id.length>0 && g.id.length<=128 &&
          typeof g.name==='string' && g.name.length>0 && g.name.length<=200 &&
          g.kind===(owner==='lefteria_fc'?'team':'academy_group') &&
          Number.isSafeInteger(g.active_players) && (g.active_players as number)>=0;
      });
  }
  function validDrafts(raw:unknown,owner:Organization):raw is {organization_id:Organization;drafts:Draft[]}{
    if(!raw || typeof raw!=='object' || Array.isArray(raw))return false;
    const d=raw as Record<string,unknown>;
    return d.organization_id===owner && Array.isArray(d.drafts) && d.drafts.length<=100 &&
      d.drafts.every((item:unknown)=>{
        if(!item || typeof item!=='object' || Array.isArray(item))return false;
        const x=item as Record<string,unknown>;
        return typeof x.id==='string' && /^ann_[0-9a-f]{32}$/.test(x.id) &&
          x.organization_id===owner && typeof x.group_id==='string' && x.group_id.length<=128 &&
          typeof x.title==='string' && x.title.length<=140 &&
          typeof x.body==='string' && x.body.length<=4000 &&
          x.status==='draft' && Number.isSafeInteger(x.version) && (x.version as number)>=1;
      });
  }
  function validPublished(raw:unknown,owner:Organization):raw is {organization_id:Organization;announcements:Published[]} {
    if(!raw||typeof raw!=='object'||Array.isArray(raw))return false;
    const d=raw as Record<string,unknown>;
    return d.organization_id===owner&&Array.isArray(d.announcements)&&
      d.announcements.length<=100&&d.announcements.every((item:unknown)=>{
        if(!item||typeof item!=='object'||Array.isArray(item))return false;
        const x=item as Record<string,unknown>;
        return typeof x.id==='string'&&/^ann_[a-f0-9]{32}$/.test(x.id)&&
          x.organization_id===owner&&typeof x.group_id==='string'&&x.group_id.length<=128&&
          typeof x.title==='string'&&x.title.length>0&&x.title.length<=140&&
          typeof x.body==='string'&&x.body.length>0&&x.body.length<=4000&&
          x.status==='published'&&Number.isSafeInteger(x.version)&&(x.version as number)>=1&&
          typeof x.published_at==='string'&&!Number.isNaN(Date.parse(x.published_at));
      });
  }
  $effect(()=>{
    if(browser && $auth.status==='anonymous')void goto('/admin/login',{replace:true});
  });
  $effect(()=>{
    if(!permitted.some(o=>o.id===organization))organization=permitted[0]?.id??null;
  });
  $effect(()=>{
    const owner=organization;
    const actor=$auth.session?.user.id;
    const grants=permitted.map(o=>o.id).join('|');
    const generation=reload;
    void actor;void grants;void generation;
    groups=[];drafts=[];published=[];groupId='';title='';body='';error=null;success=null;
    loading=false;
    if(!browser || !owner || !permitted.some(o=>o.id===owner))return;
    const controller=new AbortController();
    loading=true;
    void(async()=>{
      try{
        const query=new URLSearchParams({organization_id:owner});
        const [audiences,stored,live]=await Promise.all([
          fetch('/api/admin/football/audiences?'+query,{credentials:'same-origin',cache:'no-store',signal:controller.signal}),
          fetch('/api/admin/communications/announcement-drafts?'+query,{credentials:'same-origin',cache:'no-store',signal:controller.signal}),
          fetch('/api/admin/communications/app-announcements?'+query,{credentials:'same-origin',cache:'no-store',signal:controller.signal})
        ]);
        if(controller.signal.aborted)return;
        if(audiences.status===401 || stored.status===401 || live.status===401){auth.invalidateSession();return;}
        if(audiences.status===403 || stored.status===403 || live.status===403){void auth.bootstrap();return;}
        if(!audiences.ok || !stored.ok || !live.ok)throw new Error('Οι πρόχειρες ανακοινώσεις δεν είναι διαθέσιμες.');
        const groupRaw:unknown=await audiences.json();
        const draftRaw:unknown=await stored.json();
        const liveRaw:unknown=await live.json();
        if(!validGroups(groupRaw,owner) || !validDrafts(draftRaw,owner) ||
          !validPublished(liveRaw,owner))
          throw new Error('Μη επαληθευμένη λίστα.');
        if(!controller.signal.aborted){
          groups=groupRaw.groups;drafts=draftRaw.drafts;published=liveRaw.announcements;
        }
      }catch{
        if(!controller.signal.aborted)error='Δεν ήταν δυνατή η φόρτωση. Ελέγξτε τη σύνδεση.';
      }finally{if(!controller.signal.aborted)loading=false;}
    })();
    return ()=>controller.abort();
  });

  async function saveDraft(){
    if(!browser || !organization || !permitted.some(o=>o.id===organization) ||
      busy || loading || !groups.some(g=>g.id===groupId) ||
      !title.trim() || title.trim()!==title || title.length>140 ||
      !body.trim() || body.trim()!==body || body.length>4000){
      error='Επιλέξτε τμήμα και συμπληρώστε έγκυρο τίτλο και κείμενο.';
      return;
    }
    const owner=organization;
    const actor=$auth.session?.user.id;
    const target=groupId,headline=title,message=body;
    busy=true;error=null;success=null;
    try{
      const query=new URLSearchParams({organization_id:owner});
      const response=await fetch('/api/admin/communications/announcement-drafts?'+query,{
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({group_id:target,title:headline,body:message})
      });
      if(organization!==owner || actor!==$auth.session?.user.id)return;
      if(response.status===401){auth.invalidateSession();return;}
      if(response.status===403){void auth.bootstrap();return;}
      if(!response.ok)throw new Error('Δεν αποθηκεύτηκε το πρόχειρο.');
      const raw:unknown=await response.json();
      if(!raw || typeof raw!=='object' || Array.isArray(raw))throw new Error('Μη επαληθευμένη αποθήκευση.');
      const saved=raw as Record<string,unknown>;
      if(saved.organization_id!==owner || saved.group_id!==target ||
        saved.title!==headline || saved.body!==message ||
        saved.status!=='draft' || saved.version!==1 ||
        typeof saved.id!=='string' || !/^ann_[0-9a-f]{32}$/.test(saved.id))
        throw new Error('Μη επαληθευμένη αποθήκευση.');
      drafts=[saved as Draft,...drafts].slice(0,100);
      title='';body='';
      success='Το πρόχειρο αποθηκεύτηκε. Δεν στάλθηκε καμία ειδοποίηση.';
    }catch(e){
      if(organization===owner && actor===$auth.session?.user.id)
        error=e instanceof Error?e.message:'Η αποθήκευση απέτυχε.';
    }finally{busy=false;}
  }
  async function publishDraft(draft:Draft){
    const owner=organization,actor=$auth.session?.user.id;
    if(!browser||!owner||draft.organization_id!==owner||busy||loading||
      !permitted.some(o=>o.id===owner)||!groups.some(g=>g.id===draft.group_id))return;
    if(!window.confirm('Δημοσίευση αυτής της ανακοίνωσης στην επιλεγμένη ομάδα της εφαρμογής;'))return;
    busy=true;error=null;success=null;
    try{
      const query=new URLSearchParams({organization_id:owner});
      const reply=await fetch('/api/admin/communications/announcement-drafts/'+encodeURIComponent(draft.id)+'/publish?'+query,{
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({version:draft.version})
      });
      if(organization!==owner||actor!==$auth.session?.user.id)return;
      if(reply.status===401){auth.invalidateSession();return;}
      if(reply.status===403){void auth.bootstrap();return;}
      if(!reply.ok)throw new Error('Η δημοσίευση δεν ολοκληρώθηκε. Ανανέωσε τα πρόχειρα.');
      const raw:unknown=await reply.json();
      if(!raw||typeof raw!=='object'||Array.isArray(raw))throw new Error('Μη επαληθευμένη δημοσίευση.');
      const item=raw as Record<string,unknown>;
      if(item.id!==draft.id||item.organization_id!==owner||item.group_id!==draft.group_id||
        item.title!==draft.title||item.body!==draft.body||
        item.status!=='published'||item.version!==1||
        typeof item.published_at!=='string'||Number.isNaN(Date.parse(item.published_at)))
        throw new Error('Μη επαληθευμένη δημοσίευση.');
      drafts=drafts.filter(row=>row.id!==draft.id);
      published=[item as Published,...published].slice(0,100);
      success='Η ανακοίνωση δημοσιεύτηκε μόνο στην επιλεγμένη ομάδα της εφαρμογής.';
    }catch(e){
      if(organization===owner&&actor===$auth.session?.user.id)
        error=e instanceof Error?e.message:'Η δημοσίευση απέτυχε.';
    }finally{busy=false;}
  }
  async function removeAnnouncement(item:Published){
    const owner=organization,actor=$auth.session?.user.id;
    if(!browser||!owner||item.organization_id!==owner||busy||loading||
      !permitted.some(o=>o.id===owner))return;
    if(!window.confirm('Απόσυρση ανακοίνωσης από την εφαρμογή;'))return;
    busy=true;error=null;success=null;
    try{
      const query=new URLSearchParams({organization_id:owner,version:String(item.version)});
      const reply=await fetch('/api/admin/communications/app-announcements/'+encodeURIComponent(item.id)+'?'+query,{
        method:'DELETE',credentials:'same-origin',cache:'no-store',
        headers:{'X-Lefteria-Admin':'1'}
      });
      if(organization!==owner||actor!==$auth.session?.user.id)return;
      if(reply.status===401){auth.invalidateSession();return;}
      if(reply.status===403){void auth.bootstrap();return;}
      if(!reply.ok)throw new Error('Η απόσυρση δεν ολοκληρώθηκε. Ανανέωσε τη λίστα.');
      const raw:unknown=await reply.json();
      if(!raw||typeof raw!=='object'||Array.isArray(raw))throw new Error('Μη επαληθευμένη απόσυρση.');
      const removed=raw as Record<string,unknown>;
      if(removed.id!==item.id||removed.removed!==true||removed.version!==item.version+1)
        throw new Error('Μη επαληθευμένη απόσυρση.');
      published=published.filter(row=>row.id!==item.id);
      success='Η ανακοίνωση αποσύρθηκε από την εφαρμογή.';
    }catch(e){
      if(organization===owner&&actor===$auth.session?.user.id)
        error=e instanceof Error?e.message:'Η απόσυρση απέτυχε.';
    }finally{busy=false;}
  }
</script>

<svelte:head><title>Πρόχειρες ανακοινώσεις | Λευτεριά FC</title></svelte:head>
{#if $auth.status==='authenticated' && $auth.session}
  <AdminFrame user={$auth.session.user} active="announcements" busy={$auth.busy} onLogout={()=>void auth.logout()}>
    <p class="admin-eyebrow">CRM & ΕΠΙΚΟΙΝΩΝΙΑ · ΑΝΑΚΟΙΝΩΣΕΙΣ</p>
    <h1>Ανακοινώσεις</h1>
    <p class="admin-muted">Οι ανακοινώσεις δημιουργούνται ως πρόχειρα και δημοσιεύονται μόνο στην εφαρμογή της επιλεγμένης ομάδας. Η δημοσίευση απαιτεί ρητή επιβεβαίωση διαχειριστή. Δεν στέλνει SMS ή email.</p>
    {#if !permitted.length}
      <section class="admin-panel" role="alert">Δεν υπάρχει δικαίωμα δημιουργίας πρόχειρων ανακοινώσεων.</section>
    {:else}
      <section class="admin-panel">
        <label for="announcement-owner">Οργανισμός</label>
        <select id="announcement-owner" data-testid="announcement-owner" value={organization??''}
          onchange={e=>{if(permitted.some(o=>o.id===e.currentTarget.value))organization=e.currentTarget.value as Organization;}}>
          {#each permitted as org (org.id)}<option value={org.id}>{org.name}</option>{/each}
        </select>
        {#if loading}
          <p role="status">Φόρτωση…</p>
        {:else}
          <form onsubmit={e=>{e.preventDefault();void saveDraft();}} data-testid="announcement-draft-form">
            <label for="announcement-group">Τμήμα παραληπτών (δυναμικό)</label>
            <select id="announcement-group" bind:value={groupId} required disabled={busy}>
              <option value="">Επιλέξτε τμήμα</option>
              {#each groups as g (g.id)}<option value={g.id}>{g.name} · {g.active_players} ενεργοί παίκτες</option>{/each}
            </select>
            <label for="announcement-title">Τίτλος (έως 140 χαρακτήρες)</label>
            <input id="announcement-title" bind:value={title} maxlength="140" required disabled={busy}/>
            <label for="announcement-body">Κείμενο (έως 4.000 χαρακτήρες)</label>
            <textarea id="announcement-body" bind:value={body} maxlength="4000" rows="6" required disabled={busy}></textarea>
            <button type="submit" class="admin-primary" disabled={busy || !groups.length}>Αποθήκευση ως πρόχειρο</button>
          </form>
        {/if}
        {#if error}<p class="admin-error" role="alert">{error}</p>{/if}
        {#if success}<p role="status">{success}</p>{/if}
      </section>
      <section class="admin-panel" data-testid="announcement-draft-list">
        <div class="draft-heading"><h2>Αποθηκευμένα πρόχειρα</h2><button type="button" class="admin-secondary" disabled={loading || busy} onclick={()=>reload++}>Ανανέωση</button></div>
        {#if !drafts.length}<p class="admin-muted">Δεν υπάρχουν πρόχειρα σε αυτόν τον οργανισμό.</p>{/if}
        <ul class="draft-list">
          {#each drafts as draft (draft.id)}
            <li><strong>{draft.title}</strong>
              <small>{groups.find(g=>g.id===draft.group_id)?.name??'Τμήμα μη διαθέσιμο'} · Πρόχειρο</small>
              <p>{draft.body}</p>
              <button type="button" class="admin-primary" disabled={busy||loading||
                !groups.some(g=>g.id===draft.group_id)} onclick={()=>void publishDraft(draft)}
                data-testid="announcement-publish">Δημοσίευση στην εφαρμογή</button>
            </li>
          {/each}
        </ul>
      </section>
      <section class="admin-panel" data-testid="app-announcement-published-list">
        <div class="draft-heading"><h2>Δημοσιευμένες ανακοινώσεις εφαρμογής</h2></div>
        {#if !published.length}<p class="admin-muted">Δεν υπάρχουν δημοσιευμένες ανακοινώσεις στον οργανισμό.</p>{/if}
        <ul class="draft-list">
          {#each published as item (item.id)}
            <li><strong>{item.title}</strong>
              <small>{groups.find(g=>g.id===item.group_id)?.name??'Τμήμα μη διαθέσιμο'} · Δημοσιεύτηκε
                {new Date(item.published_at).toLocaleString('el-CY')}</small>
              <p>{item.body}</p>
              <button type="button" class="admin-secondary" disabled={busy||loading}
                onclick={()=>void removeAnnouncement(item)}
                data-testid="announcement-remove">Απόσυρση ανακοίνωσης</button>
            </li>
          {/each}
        </ul>
      </section>
    {/if}
  </AdminFrame>
{:else}
  <AdminSessionStatus unavailable={$auth.status==='unavailable'} busy={$auth.busy} error={$auth.error}
    onRetry={()=>void auth.bootstrap()} onLogout={()=>void auth.logout()}/>
{/if}
<style>
  .draft-heading{display:flex;gap:12px;align-items:center;justify-content:space-between}
  .draft-list{list-style:none;margin:0;padding:0}
  .draft-list li{padding:15px 0;border-top:1px solid #303335}
  .draft-list strong,.draft-list small{display:block}
  .draft-list small{color:#ffca46;margin-top:4px}
  .draft-list p{white-space:pre-wrap;overflow-wrap:anywhere;color:#d9dcdd}
  textarea{width:100%;min-height:140px;padding:12px;color:#fff;background:#090c0b;border:1px solid #5a6056;border-radius:8px;font:inherit}
  @media(max-width:600px){.draft-heading{align-items:start;flex-direction:column}}
</style>
