<script lang="ts">
  import {getContext} from 'svelte';
  import {browser} from '$app/env';
  import {goto} from '$app/navigation';
  import {ADMIN_AUTH_CONTEXT,type AdminAuthController} from '../../../lib/admin/controller.ts';
  import {hasAdminPermission} from '../../../lib/admin/permissions.ts';
  import {isHubOrganization,validHubList,validHubDetail,validHubTransition,
    type HubOrganization,type HubEditorialItem,type HubEditorialDetail,type HubState
  } from '../../../lib/admin/hub-editorial.ts';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';

  const auth=getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  const permitted=$derived($auth.status==='authenticated'&&$auth.session&&
    ['manager','super_admin','editor'].includes($auth.session.user.role)&&
    hasAdminPermission($auth.session.user.permissions,'news:read')
    ? $auth.session.user.organizations.filter(o=>isHubOrganization(o.id)).map(o=>o.id as HubOrganization)
    : []);
  const manager=$derived($auth.status==='authenticated'&&
    ($auth.session?.user.role==='manager'||$auth.session?.user.role==='super_admin'));
  const canWrite=$derived(manager||$auth.session?.user.role==='editor');
  let organization=$state<HubOrganization|null>(null);
  let items=$state<HubEditorialItem[]>([]);
  let detail=$state<HubEditorialDetail|null>(null);
  let loading=$state(false),busy=$state(false);
  let error=$state<string|null>(null),success=$state<string|null>(null);
  let reload=$state(0),detailRequest=0;
  let audience=$state<'parent'|'player'>('parent');
  let topic=$state('support');
  let language=$state<'el'|'en'>('el');
  let ageMin=$state(6),ageMax=$state(12);
  let inspected=$state<string[]>([]);
  let showPreview=$state<string|null>(null);
  const topics=$derived(audience==='parent'?
    ['support','communication','development','wellbeing']:
    ['confidence','teamwork','resilience','learning','wellbeing']);
  $effect(()=>{if(browser&&$auth.status==='anonymous')void goto('/admin/login',{replace:true});});
  $effect(()=>{if(!organization||!permitted.includes(organization))organization=permitted[0]??null;});
  $effect(()=>{
    const owner=organization,actor=$auth.session?.user.id,generation=reload;
    void actor;void generation;
    items=[];detail=null;detailRequest++;inspected=[];showPreview=null;error=null;
    if(!browser||!owner||!permitted.includes(owner))return;
    const abort=new AbortController();
    loading=true;
    void(async()=>{
      try{
        const response=await fetch('/api/admin/hub/editorial?organization_id='+owner,{
          credentials:'same-origin',cache:'no-store',signal:abort.signal
        });
        if(abort.signal.aborted)return;
        if(response.status===401){auth.invalidateSession();return;}
        if(response.status===403){void auth.bootstrap();return;}
        if(!response.ok)throw new Error();
        const data:unknown=await response.json();
        if(!validHubList(data,owner))throw new Error();
        if(!abort.signal.aborted)items=data.items;
      }catch{
        if(!abort.signal.aborted)error='Δεν ήταν δυνατή η ασφαλής φόρτωση των προχείρων.';
      }finally{if(!abort.signal.aborted)loading=false;}
    })();
    return()=>abort.abort();
  });
  async function selectItem(item:HubEditorialItem){
    const owner=organization,actor=$auth.session?.user.id;
    if(!owner||item.organization_id!==owner||busy)return;
    const request=++detailRequest;detail=null;inspected=[];showPreview=null;
    error=null;success=null;
    try{
      const reply=await fetch('/api/admin/hub/editorial/'+encodeURIComponent(item.id)+
        '?organization_id='+owner,{credentials:'same-origin',cache:'no-store'});
      if(reply.status===401){auth.invalidateSession();return;}
      if(reply.status===403){void auth.bootstrap();return;}
      if(!reply.ok)throw new Error();
      const data:unknown=await reply.json();
      if(!validHubDetail(data,owner)||data.id!==item.id)throw new Error();
      if(request!==detailRequest||organization!==owner||$auth.session?.user.id!==actor)return;
      detail=data;
    }catch{
      if(request===detailRequest)error='Δεν επαληθεύτηκαν τα στοιχεία αυτού του άρθρου.';
    }
  }
  async function runAction(action:'submit-review'|'text-review'|'visual-approve'|'final-approve'|'publish',
      resulting:HubState,decision?:'passed'|'rejected'){
    const source=detail,owner=organization,actor=$auth.session?.user.id;
    if(!source||!owner||source.organization_id!==owner||!manager||busy)return;
    if(action==='visual-approve'&&source.visuals.some(v=>!inspected.includes(v.id))){
      error='Ελέγξτε πρώτα κάθε πραγματική εικόνα.';return;
    }
    if(action==='publish'&&!confirm('Να δημοσιευθεί αυτό το εγκεκριμένο άρθρο στην εφαρμογή;'))return;
    busy=true;error=null;success=null;
    try{
      const reply=await fetch('/api/admin/hub/editorial/'+encodeURIComponent(source.id)+
        '/'+action+'?organization_id='+owner,{
          method:'POST',credentials:'same-origin',cache:'no-store',
          headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
          body:JSON.stringify(decision?{version:source.version,decision}:{version:source.version})
        });
      if(reply.status===401){auth.invalidateSession();return;}
      if(reply.status===403){void auth.bootstrap();return;}
      if(!reply.ok)throw new Error();
      const result:unknown=await reply.json();
      if(!validHubTransition(result,owner,source.id,source.version,resulting))throw new Error();
      if(organization!==owner||$auth.session?.user.id!==actor)return;
      success='Η ενέργεια επιβεβαιώθηκε από το σύστημα.';
      detail=null;inspected=[];showPreview=null;detailRequest++;reload++;
    }catch{error='Η έγκριση δεν επιβεβαιώθηκε. Ανανεώστε τα στοιχεία πριν ξαναδοκιμάσετε.';}
    finally{busy=false;}
  }
  async function requestAiBrief(){
    const owner=organization,actor=$auth.session?.user.id;
    if(!owner||!canWrite||busy||!Number.isInteger(ageMin)||!Number.isInteger(ageMax)||
       ageMin<6||ageMax>18||ageMax<ageMin||!topics.includes(topic)){
      error='Ελέγξτε την ηλικία και το θέμα της εκπαιδευτικής εντολής.';return;
    }
    busy=true;error=null;success=null;
    try{
      const reply=await fetch('/api/admin/hub/ai/education-briefs?organization_id='+owner,{
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({audience,ageMin,ageMax,topic,language})
      });
      if(reply.status===401){auth.invalidateSession();return;}
      if(reply.status===403){void auth.bootstrap();return;}
      if(!reply.ok)throw new Error();
      const result:unknown=await reply.json();
      if(!result||typeof result!=='object'||Array.isArray(result)||
         (result as Record<string,unknown>).organization_id!==owner||
         (result as Record<string,unknown>).status!=='pending')throw new Error();
      if(organization===owner&&$auth.session?.user.id===actor)
        success='Η εντολή καταχωρίστηκε. Η παραγωγή AI είναι ανενεργή μέχρι να ενεργοποιηθεί με ασφάλεια στο δοκιμαστικό περιβάλλον.';
    }catch{error='Η εντολή δεν καταχωρίστηκε ή δεν επιβεβαιώθηκε.';}
    finally{busy=false;}
  }
</script>
<svelte:head><title>HUB · Έλεγχος περιεχομένου | Lefteria FC</title></svelte:head>
{#if $auth.status==='authenticated'&&$auth.session}
  <AdminFrame user={$auth.session.user} busy={$auth.busy||busy}
    onLogout={()=>void auth.logout()}>
    <div class="hub-editor" data-testid="hub-editorial-workspace">
      <header>
        <p class="eyebrow">ΛΕΥΤΕΡΙΑ FC · HUB</p>
        <h1>Εκπαίδευση · Έλεγχος & δημοσίευση</h1>
        <p>Το AI δημιουργεί μόνο πρόχειρα. Τα επίσημα άρθρα απαιτούν ανθρώπινο έλεγχο κειμένου και εικαστικών, τελική έγκριση και ξεχωριστή δημοσίευση.</p>
      </header>
      {#if permitted.length}
        <div class="toolbar">
          <label>Οργανισμός
            <select bind:value={organization}>
              {#each permitted as owner}<option value={owner}>{owner==='lefteria_fc'?'Lefteria FC':'Lefteria FC Academy'}</option>{/each}
            </select>
          </label>
          <button type="button" disabled={busy||loading} onclick={()=>reload++}>Ανανέωση</button>
        </div>
        {#if canWrite}
          <section class="panel">
            <h2>Δημιουργία εκπαιδευτικής εντολής AI</h2>
            <p>Η εντολή δεν δημοσιεύει άρθρο και δεν περιέχει στοιχεία παιδιών ή προπονητών.</p>
            <div class="fields">
              <label>Για ποιον
                <select bind:value={audience} onchange={()=>topic=audience==='parent'?'support':'confidence'}>
                  <option value="parent">Γονείς</option><option value="player">Αθλητές</option>
                </select>
              </label>
              <label>Θέμα
                <select bind:value={topic}>{#each topics as item}<option value={item}>{item}</option>{/each}</select>
              </label>
              <label>Γλώσσα
                <select bind:value={language}><option value="el">Ελληνικά</option><option value="en">English</option></select>
              </label>
              <label>Ηλικία από<input type="number" min="6" max="18" bind:value={ageMin}/></label>
              <label>Ηλικία έως<input type="number" min="6" max="18" bind:value={ageMax}/></label>
            </div>
            <button type="button" disabled={busy||loading} onclick={()=>void requestAiBrief()}>Καταχώριση πρόχειρης εντολής</button>
          </section>
        {/if}
        <div class="layout">
          <section class="panel" aria-labelledby="editor-list-title">
            <h2 id="editor-list-title">Πρόχειρα & δημοσιεύσεις</h2>
            {#if loading}<p role="status">Ανάκτηση εγγραφών…</p>
            {:else if items.length===0}<p>Δεν υπάρχουν καταχωρίσεις.</p>{/if}
            <ul class="drafts">
              {#each items as item (item.id)}
                <li>
                  <button type="button" disabled={busy} onclick={()=>void selectItem(item)}>
                    <strong>{item.title}</strong>
                    <span>{item.status} · v{item.version} · {item.audience_parent?'Γονείς ':''}{item.audience_player?'Αθλητές':''}</span>
                  </button>
                </li>
              {/each}
            </ul>
          </section>
          <section class="panel" aria-labelledby="editor-review-title">
            <h2 id="editor-review-title">Ανθρώπινος έλεγχος</h2>
            {#if detail}
              <p class="meta">{detail.kind} · {detail.status} · v{detail.version} · {detail.provenance}</p>
              <h3>{detail.title}</h3>
              <p class="article-body">{detail.body}</p>
              <p>Κείμενο: <strong>{detail.text_review}</strong> · Εικόνες: <strong>{detail.visual_review}</strong></p>
              {#if detail.visuals.length}
                <h3>Εικαστικά προς έλεγχο</h3>
                {#each detail.visuals as media (media.id)}
                  <div class="image-review">
                    <p>{media.alt_text??'Χωρίς εναλλακτικό κείμενο'} · {media.moderation}</p>
                    {#if media.moderation==='quarantined'}
                      <button type="button" onclick={()=>{showPreview=media.id;inspected=[...new Set([...inspected,media.id])];}}>Άνοιγμα πραγματικής εικόνας για έλεγχο</button>
                      {#if showPreview===media.id}
                        <img alt={media.alt_text??'Εικαστικό προς έλεγχο'} loading="lazy"
                          src={'/api/admin/hub/editorial/'+detail.id+'/images/'+media.id+'/preview?organization_id='+detail.organization_id}/>
                      {/if}
                    {/if}
                  </div>
                {/each}
              {/if}
              {#if manager&&detail.status==='ai_draft'}
                <button type="button" disabled={busy} onclick={()=>void runAction('submit-review','review_required')}>Υποβολή για έλεγχο</button>
              {/if}
              {#if manager&&detail.status==='review_required'}
                {#if detail.text_review==='pending'}
                  <button type="button" disabled={busy} onclick={()=>void runAction('text-review','review_required','passed')}>Έγκριση κειμένου</button>
                  <button type="button" disabled={busy} onclick={()=>void runAction('text-review','review_required','rejected')}>Απόρριψη κειμένου</button>
                {/if}
                {#if detail.visual_review==='pending'}
                  <button type="button" disabled={busy} onclick={()=>void runAction('visual-approve','review_required')}>Έγκριση οπτικού ελέγχου</button>
                {/if}
                {#if detail.visual_review==='passed'&&detail.text_review==='passed'}
                  <button type="button" disabled={busy} onclick={()=>void runAction('final-approve','approved')}>Τελική ανθρώπινη έγκριση</button>
                {/if}
              {/if}
              {#if manager&&detail.status==='approved'}
                <button type="button" disabled={busy} onclick={()=>void runAction('publish','published')}>Δημοσίευση στην εφαρμογή</button>
              {/if}
            {:else}
              <p>Επιλέξτε άρθρο για να δείτε το πλήρες κείμενο, τις εικόνες και τις επόμενες επιτρεπόμενες ενέργειες.</p>
            {/if}
          </section>
        </div>
        {#if success}<p class="success" role="status">{success}</p>{/if}
        {#if error}<p class="error" role="alert">{error}</p>{/if}
      {:else}
        <p>Δεν έχετε δικαιώματα διαχείρισης HUB σε κάποιον οργανισμό.</p>
      {/if}
    </div>
  </AdminFrame>
{:else}
  <AdminSessionStatus unavailable={$auth.status==='unavailable'} busy={$auth.busy}
    error={$auth.error} onRetry={()=>void auth.bootstrap()} onLogout={()=>void auth.logout()}/>
{/if}
<style>
  .hub-editor{max-width:1500px;margin:0 auto;padding:26px;color:var(--admin-text,#f4f4f4)}
  .hub-editor h1{font-size:clamp(27px,4vw,42px)}
  .hub-editor p{line-height:1.6}
  .eyebrow{font-size:11px;letter-spacing:.12em;color:#dbaa4f;font-weight:700}
  .toolbar,.fields{display:flex;gap:14px;flex-wrap:wrap;align-items:end;margin:16px 0}
  label{display:grid;gap:7px;font-size:13px}
  select,input{min-height:44px;min-width:145px;padding:9px;background:#232728;color:#fff;border:1px solid #565b5c;border-radius:8px}
  button{min-height:44px;padding:10px 14px;border:1px solid #83632d;border-radius:8px;background:#312817;color:#ffce74;font-weight:600;cursor:pointer;margin:3px}
  button:disabled{opacity:.45;cursor:default}
  button:focus-visible{outline:2px solid #ffd36d;outline-offset:3px}
  .layout{display:grid;grid-template-columns:minmax(260px,1fr) minmax(0,1.6fr);gap:18px;margin-top:18px}
  .panel{padding:18px;background:#1b1f20;border:1px solid #3e4344;border-radius:13px}
  .panel h2{margin-top:0;font-size:23px}
  .drafts{list-style:none;padding:0;display:grid;gap:10px;max-height:600px;overflow:auto}
  .drafts button{width:100%;text-align:left;display:grid;gap:7px;background:#242829;margin:0}
  .drafts span,.meta{font-size:12px;color:#bdbdbd}
  .article-body{white-space:pre-wrap;overflow-wrap:anywhere;font-size:14px;line-height:1.7}
  .image-review{border:1px solid #55462c;padding:12px;border-radius:10px;margin:12px 0}
  .image-review img{display:block;max-width:100%;max-height:450px;object-fit:contain;margin-top:12px}
  .error,.success{padding:14px;border-radius:10px;border:1px solid #68513c}
  .error{color:#ffd0c5}.success{color:#d5f3d2}
  @media(max-width:780px){.layout{grid-template-columns:1fr}.hub-editor{padding:16px}}
</style>
