<script lang="ts">
  import { getContext, tick } from 'svelte';
  import { browser } from '$app/env';
  import { goto } from '$app/navigation';
  import { ADMIN_AUTH_CONTEXT, type AdminAuthController } from '../../../lib/admin/controller.ts';
  import { hasAdminPermission } from '../../../lib/admin/permissions.ts';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';

  type Organization='lefteria_fc'|'lefteria_fc_academy';
  type Group={id:string;name:string;kind:string;active_players:number};
  type Directory={organization_id:Organization;ready:true;groups:Group[]};
  type Media={id:string;kind:'banner'|'attachment';file_name:string;content_type:string;size:number};
  type Draft={id:string;organization_id:Organization;group_id:string;title:string;body:string;
    status:'draft';version:number;media?:Media[]};
  type Published={id:string;organization_id:Organization;group_id:string;title:string;body:string;
    published_at:string;status:'published';version:number;media?:Media[]};
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
  type Reader={id:string;name:string};
  let readerDialog:HTMLDialogElement;
  let readerItem=$state<Published|null>(null);
  let readerRows=$state<Reader[]>([]);
  let readerBusy=$state(false);


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
  function validMedia(raw:unknown):raw is Media[] {
    return Array.isArray(raw)&&raw.length<=4&&
      raw.filter((f:Media)=>f.kind==='banner').length<=1&&
      raw.filter((f:Media)=>f.kind==='attachment').length<=3&&
      raw.every((file:unknown)=>{
        if(!file||typeof file!=='object'||Array.isArray(file))return false;
        const f=file as Record<string,unknown>;
        return typeof f.id==='string'&&
          /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(f.id)&&
          typeof f.file_name==='string'&&f.file_name.length>0&&f.file_name.length<=96&&
          Number.isInteger(f.size)&&
          (f.kind==='banner'&&f.file_name==='banner.jpg'&&
            f.content_type==='image/jpeg'&&(f.size as number)>0&&(f.size as number)<=1048576||
           f.kind==='attachment'&&f.content_type==='application/pdf'&&
            /^[A-Za-z0-9][A-Za-z0-9 ._()-]{0,91}\.pdf$/i.test(f.file_name)&&
            (f.size as number)>0&&(f.size as number)<=2097152);
      })&&new Set(raw.map((f:Media)=>f.id)).size===raw.length;
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
          x.status==='draft' && Number.isSafeInteger(x.version) && (x.version as number)>=1 &&
          (x.media===undefined||validMedia(x.media));
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
          typeof x.published_at==='string'&&!Number.isNaN(Date.parse(x.published_at))&&
          (x.media===undefined||validMedia(x.media));
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
    readerItem=null;readerRows=[];
    if(readerDialog?.open)readerDialog.close();
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
      drafts=[{...(saved as Draft),media:[]},...drafts].slice(0,100);
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
      published=[{...(item as Published),media:draft.media??[]},...published].slice(0,100);
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
        // The web-admin mutation guard requires JSON even for versioned DELETE.
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:'{}'
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
  async function showReaders(item:Published){
    const owner=organization,actor=$auth.session?.user.id;
    if(!browser||!owner||!permitted.some(o=>o.id===owner)||
      item.organization_id!==owner||busy||loading||readerBusy)return;
    readerBusy=true;error=null;
    try{
      const query=new URLSearchParams({organization_id:owner});
      const response=await fetch('/api/admin/communications/app-announcements/'+
        encodeURIComponent(item.id)+'/readers?'+query,{
          credentials:'same-origin',cache:'no-store'
        });
      if(owner!==organization||actor!==$auth.session?.user.id)return;
      if(response.status===401){auth.invalidateSession();return;}
      if(response.status===403){void auth.bootstrap();return;}
      if(!response.ok)throw new Error('Δεν είναι διαθέσιμη η λίστα αναγνωστών.');
      const raw:unknown=await response.json();
      if(!raw||typeof raw!=='object'||Array.isArray(raw))
        throw new Error('Μη επαληθευμένη λίστα αναγνωστών.');
      const data=raw as Record<string,unknown>;
      if(data.announcement_id!==item.id||!Array.isArray(data.readers)||
         data.readers.length>150||!data.readers.every((v:unknown)=>{
           if(!v||typeof v!=='object'||Array.isArray(v))return false;
           const row=v as Record<string,unknown>;
           return typeof row.id==='string'&&
             /^[a-f0-9-]{36}$/i.test(row.id)&&
             typeof row.name==='string'&&row.name.length>0&&row.name.length<=200;
         })||new Set((data.readers as Reader[]).map(r=>r.id)).size!==data.readers.length)
        throw new Error('Μη επαληθευμένη λίστα αναγνωστών.');
      readerRows=data.readers as Reader[];
      readerItem=item;
      await tick();
      if(owner===organization&&actor===$auth.session?.user.id)readerDialog?.showModal();
    }catch(e){
      if(owner===organization&&actor===$auth.session?.user.id)
        error=e instanceof Error?e.message:'Η λίστα αναγνωστών δεν φορτώθηκε.';
    }finally{readerBusy=false;}
  }

  const bannerTypes=['image/jpeg','image/png','image/webp','image/gif'];
  async function uploadDraftMedia(draft:Draft,kind:'banner'|'attachment',file:File){
    const owner=organization,actor=$auth.session?.user.id;
    if(!browser||!owner||owner!==draft.organization_id||busy||loading||
      !permitted.some(o=>o.id===owner))return;
    if(file.size===0||file.size>(kind==='banner'?3145728:2097152)||
      (kind==='banner'&&!bannerTypes.includes(file.type))||
      (kind==='attachment'&&(file.type!=='application/pdf'||
        !/^[A-Za-z0-9][A-Za-z0-9 ._()-]{0,91}\.pdf$/i.test(file.name)))||
      (kind==='attachment'&&(draft.media??[]).filter(x=>x.kind==='attachment').length>=3)){
      error='Εικόνα έως 3 MB (JPEG/PNG/WebP/GIF) ή PDF έως 2 MB. Έως τρία PDF, με απλό λατινικό όνομα.';
      return;
    }
    busy=true;error=null;success=null;
    try{
      const bytes=new Uint8Array(await file.arrayBuffer());
      const chunks:string[]=[];
      for(let i=0;i<bytes.length;i+=0x4000)
        chunks.push(String.fromCharCode(...bytes.subarray(i,i+0x4000)));
      const base64=btoa(chunks.join(''));
      const query=new URLSearchParams({organization_id:owner});
      const response=await fetch('/api/admin/communications/announcement-drafts/'+
        encodeURIComponent(draft.id)+'/media?'+query,{
          method:'POST',credentials:'same-origin',cache:'no-store',
          headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
          body:JSON.stringify({version:draft.version,kind,
            file_name:kind==='banner'?'banner.jpg':file.name,
            content_type:file.type,base64})
        });
      if(owner!==organization||actor!==$auth.session?.user.id)return;
      if(response.status===401){auth.invalidateSession();return;}
      if(response.status===403){void auth.bootstrap();return;}
      if(!response.ok)throw new Error('Το αρχείο δεν αποθηκεύτηκε. Ελέγξτε τον τύπο και την έκδοση του προχείρου.');
      const data:unknown=await response.json();
      if(!data||typeof data!=='object'||Array.isArray(data))
        throw new Error('Μη επαληθευμένη αποθήκευση αρχείου.');
      const confirmed=data as Record<string,unknown>;
      if(confirmed.announcement_id!==draft.id||confirmed.version!==draft.version+1||
        !validMedia(confirmed.media)||!(confirmed.media as Media[]).some(m=>
          m.kind===kind&&(kind==='banner'||m.file_name===file.name)))
        throw new Error('Μη επαληθευμένη αποθήκευση αρχείου.');
      drafts=drafts.map(d=>d.id===draft.id
        ?{...d,version:confirmed.version as number,media:confirmed.media as Media[]}:d);
      success='Το αρχείο αποθηκεύτηκε μόνο στο πρόχειρο.';
    }catch(e){if(owner===organization&&actor===$auth.session?.user.id)
      error=e instanceof Error?e.message:'Αποτυχία αποθήκευσης.';}
    finally{busy=false;}
  }
  async function removeDraftMedia(draft:Draft,media:Media){
    const owner=organization,actor=$auth.session?.user.id;
    if(!browser||!owner||owner!==draft.organization_id||busy||loading||
      !window.confirm('Αφαίρεση συνημμένου από το πρόχειρο;'))return;
    busy=true;error=null;success=null;
    try{
      const query=new URLSearchParams({organization_id:owner});
      const response=await fetch('/api/admin/communications/announcement-drafts/'+
        encodeURIComponent(draft.id)+'/media/'+encodeURIComponent(media.id)+'?'+query,{
          method:'DELETE',credentials:'same-origin',cache:'no-store',
          headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
          body:JSON.stringify({version:draft.version})
        });
      if(owner!==organization||actor!==$auth.session?.user.id)return;
      if(response.status===401){auth.invalidateSession();return;}
      if(response.status===403){void auth.bootstrap();return;}
      if(!response.ok)throw new Error('Δεν ήταν δυνατή η αφαίρεση συνημμένου.');
      const result:unknown=await response.json();
      if(!result||typeof result!=='object'||Array.isArray(result))
        throw new Error('Μη επαληθευμένη αφαίρεση.');
      const x=result as Record<string,unknown>;
      if(x.announcement_id!==draft.id||x.version!==draft.version+1||
        !validMedia(x.media)||(x.media as Media[]).some(m=>m.id===media.id))
        throw new Error('Μη επαληθευμένη αφαίρεση.');
      drafts=drafts.map(d=>d.id===draft.id
        ?{...d,version:x.version as number,media:x.media as Media[]}:d);
      success='Το συνημμένο αφαιρέθηκε.';
    }catch(e){if(owner===organization&&actor===$auth.session?.user.id)
      error=e instanceof Error?e.message:'Αποτυχία αφαίρεσης.';}
    finally{busy=false;}
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
              <div class="media-editor" data-testid="announcement-media">
                <label>Εικόνα banner (έως 3 MB)
                  <input type="file" accept="image/jpeg,image/png,image/webp,image/gif"
                    disabled={busy||loading}
                    onchange={e=>{const f=e.currentTarget.files?.[0];e.currentTarget.value='';
                      if(f)void uploadDraftMedia(draft,'banner',f);}}/>
                </label>
                <label>Συνημμένο PDF (έως 2 MB, τρία συνολικά)
                  <input type="file" accept="application/pdf,.pdf"
                    disabled={busy||loading||(draft.media??[]).filter(f=>f.kind==='attachment').length>=3}
                    onchange={e=>{const f=e.currentTarget.files?.[0];e.currentTarget.value='';
                      if(f)void uploadDraftMedia(draft,'attachment',f);}}/>
                </label>
                {#each draft.media??[] as file (file.id)}
                  <div class="media-row">
                    <span>{file.kind==='banner'?'🖼️ Banner':'📎 '+file.file_name}
                      · {Math.ceil(file.size/1024)} KB</span>
                    <button type="button" class="admin-secondary" disabled={busy||loading}
                      onclick={()=>void removeDraftMedia(draft,file)}>Αφαίρεση</button>
                  </div>
                {/each}
              </div>
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
              {#if item.media?.length}
                <div class="media-row">Υλικό εφαρμογής:
                  {#each item.media as file (file.id)}
                    <span>{file.kind==='banner'?'🖼️ Banner':'📎 '+file.file_name}</span>
                  {/each}
                </div>
              {/if}
              <button type="button" class="admin-secondary" disabled={busy||loading||readerBusy}
                onclick={()=>void showReaders(item)} data-testid="announcement-readers">
                Διαβάστηκε από
              </button>
              <button type="button" class="admin-secondary" disabled={busy||loading}
                onclick={()=>void removeAnnouncement(item)}
                data-testid="announcement-remove">Απόσυρση ανακοίνωσης</button>
            </li>
          {/each}
        </ul>
      </section>
    {/if}
    <dialog bind:this={readerDialog} class="readers-dialog"
      onclose={()=>{readerItem=null;readerRows=[];}}>
      <h2>Διαβάστηκε από</h2>
      {#if readerItem}
        <p>{readerItem.title}</p>
      {/if}
      {#if readerRows.length}
        <ul>{#each readerRows as reader (reader.id)}<li>{reader.name}</li>{/each}</ul>
      {:else}
        <p>Δεν υπάρχουν επιβεβαιωμένες αναγνώσεις από ενεργά μέλη.</p>
      {/if}
      <form method="dialog"><button type="submit" class="admin-secondary">Κλείσιμο</button></form>
    </dialog>
  </AdminFrame>
{:else}
  <AdminSessionStatus unavailable={$auth.status==='unavailable'} busy={$auth.busy} error={$auth.error}
    onRetry={()=>void auth.bootstrap()} onLogout={()=>void auth.logout()}/>
{/if}
<style>
  .media-editor{display:grid;gap:9px;margin:14px 0;padding:12px;border:1px solid #444943;
    background:#131918;border-radius:11px}
  .media-editor label{display:grid;gap:6px;font-size:12px;color:#e2d9bd}
  .media-editor input{max-width:100%;font:inherit}
  .media-row{display:flex;flex-wrap:wrap;align-items:center;gap:10px;
    padding:6px 0;font-size:12px;color:#e3d5a9}
  .draft-heading{display:flex;gap:12px;align-items:center;justify-content:space-between}
  .draft-list{list-style:none;margin:0;padding:0}
  .draft-list li{padding:15px 0;border-top:1px solid #303335}
  .draft-list strong,.draft-list small{display:block}
  .draft-list small{color:#ffca46;margin-top:4px}
  .draft-list p{white-space:pre-wrap;overflow-wrap:anywhere;color:#d9dcdd}
  textarea{width:100%;min-height:140px;padding:12px;color:#fff;background:#090c0b;border:1px solid #5a6056;border-radius:8px;font:inherit}
  .readers-dialog{width:min(92vw,480px);max-height:78vh;overflow:auto;padding:24px;
    color:#fff;background:#161a19;border:1px solid #9c8344;border-radius:14px}
  .readers-dialog::backdrop{background:rgba(0,0,0,.72)}
  .readers-dialog ul{padding-left:20px;line-height:1.8}
  .readers-dialog h2{margin-top:0}
  @media(max-width:600px){.draft-heading{align-items:start;flex-direction:column}}
</style>
