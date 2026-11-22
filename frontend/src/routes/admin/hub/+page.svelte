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
  import { scopedAdminOrganization } from '../../../lib/admin/admin-organization.ts';
  import AdminClassroomCurriculum from '../../../lib/admin/AdminClassroomCurriculum.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';

  const auth=getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  const permitted=$derived($auth.status==='authenticated'&&$auth.session&&
    ['manager','super_admin','editor'].includes($auth.session.user.role)&&
    hasAdminPermission($auth.session.user.permissions,'news:read')
    ? $auth.session.user.organizations.filter(o=>isHubOrganization(o.id)).map(o=>o.id as HubOrganization)
    : []);
  const manager=$derived($auth.status==='authenticated'&&
    ($auth.session?.user.role==='manager'||$auth.session?.user.role==='super_admin'));
  const canWrite=$derived(permitted.length>0&&
    hasAdminPermission($auth.session?.user.permissions??[],'news:write')&&
    (manager||$auth.session?.user.role==='editor'));
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
  let humanKind=$state<'article'|'drill'|'video'|'visual'|'course'>('article');
  let humanTitle=$state(''),humanBody=$state('');
  let humanParent=$state(true),humanPlayer=$state(false);
  let humanAgeRestricted=$state(false),humanAgeMin=$state(6),humanAgeMax=$state(18);
  let statusFilter=$state<'all'|HubState>('all');
  let revisionTitle=$state(''),revisionBody=$state('');
  let imageAlt=$state(''),imageFile=$state<File|null>(null),imageInputKey=$state(0);
  const filteredItems=$derived(items.filter(item=>statusFilter==='all'||item.status===statusFilter));
  const statusLabels:Record<HubState,string>={
    ai_draft:'Πρόχειρο',review_required:'Για έλεγχο',approved:'Εγκεκριμένο',
    published:'Δημοσιευμένο',rejected:'Απορρίφθηκε'
  };
  // Inspection proofs are bound to a single revision, not a reusable media ID.
  let inspected=$state<string[]>([]);
  const inspectionKey=(source:HubEditorialDetail,mediaId:string)=>
    [source.organization_id,source.id,source.revision_id,source.version,mediaId].join(':');
  let showPreview=$state<string|null>(null);
  const topics=$derived(audience==='parent'?
    ['support','communication','development','wellbeing']:
    ['confidence','teamwork','resilience','learning','wellbeing']);
  $effect(()=>{if(browser&&$auth.status==='anonymous')void goto('/admin/login',{replace:true});});
  $effect(()=>{
    const selected=scopedAdminOrganization($auth.session?.user??null,permitted);
    if(organization!==selected){
      organization=selected;
      // Never carry unsaved First Team text or audience settings into Academy,
      // or vice versa, even if both organizations are accessible to this admin.
      humanTitle='';humanBody='';humanKind='article';
      humanParent=true;humanPlayer=false;humanAgeRestricted=false;
      humanAgeMin=6;humanAgeMax=18;statusFilter='all';
    }
  });
  $effect(()=>{
    const owner=organization,actor=$auth.session?.user.id,generation=reload;
    void actor;void generation;
    items=[];detail=null;detailRequest++;inspected=[];showPreview=null;error=null;
    revisionTitle='';revisionBody='';imageFile=null;imageAlt='';imageInputKey++;
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
    revisionTitle='';revisionBody='';imageFile=null;imageAlt='';imageInputKey++;
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
      detail=data;revisionTitle=data.title;revisionBody=data.body;
    }catch{
      if(request===detailRequest)error='Δεν επαληθεύτηκαν τα στοιχεία αυτού του άρθρου.';
    }
  }
  async function runAction(action:'submit-review'|'text-review'|'visual-approve'|'final-approve'|'publish',
      resulting:HubState,decision?:'passed'|'rejected'){
    const source=detail,owner=organization,actor=$auth.session?.user.id;
    if(!source||!owner||source.organization_id!==owner||!manager||busy)return;
    if(action==='visual-approve'&&source.visuals.some(v=>!inspected.includes(inspectionKey(source,v.id)))){
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
        body:JSON.stringify({audience,age_min:ageMin,age_max:ageMax,topic,language})
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

  async function createHumanDraft(){
    const owner=organization,actor=$auth.session?.user.id;
    if(!owner||!canWrite||busy)return;
    const title=humanTitle.trim(),body=humanBody.trim();
    const unsafe=(value:string)=>Array.from(value).some(char=>/\p{Cc}/u.test(char));
    if(!title||title.length>180||unsafe(title)||!body||body.length>20000||unsafe(body)||
       !(humanParent||humanPlayer)||
       (humanAgeRestricted&&(!Number.isInteger(humanAgeMin)||!Number.isInteger(humanAgeMax)||
         humanAgeMin<4||humanAgeMax>99||humanAgeMin>humanAgeMax))){
      error='Συμπληρώστε τίτλο και κείμενο χωρίς χαρακτήρες ελέγχου ή αλλαγές γραμμής, επιλέξτε κοινό και ελέγξτε τυχόν όρια ηλικίας.';
      return;
    }
    busy=true;error=null;success=null;
    try{
      const response=await fetch('/api/admin/hub/editorial?organization_id='+owner,{
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({
          kind:humanKind,title,body,audience_parent:humanParent,audience_player:humanPlayer,
          age_min:humanAgeRestricted?humanAgeMin:null,age_max:humanAgeRestricted?humanAgeMax:null
        })
      });
      if(response.status===401){auth.invalidateSession();return;}
      if(response.status===403){void auth.bootstrap();return;}
      if(response.status!==201)throw new Error();
      const result:unknown=await response.json();
      if(!result||typeof result!=='object'||Array.isArray(result)||
         (result as Record<string,unknown>).organization_id!==owner||
         (result as Record<string,unknown>).status!=='ai_draft'||
         (result as Record<string,unknown>).kind!==humanKind)throw new Error();
      if(organization!==owner||$auth.session?.user.id!==actor)return;
      humanTitle='';humanBody='';success='Το ανθρώπινο πρόχειρο αποθηκεύτηκε ιδιωτικά. Απαιτείται ξεχωριστός έλεγχος και δημοσίευση.';
      statusFilter='ai_draft';reload++;
    }catch{error='Η αποθήκευση του προχείρου δεν επιβεβαιώθηκε. Ελέγξτε τα στοιχεία και ανανεώστε.';}
    finally{busy=false;}
  }

  function reviewInputValid(title:string,body:string):boolean{
    return title.length>=1&&title.length<=180&&body.length>=1&&body.length<=20000&&
      !Array.from(title+body).some(char=>/\p{Cc}/u.test(char));
  }
  async function reviseDraft(){
    const source=detail,owner=organization,actor=$auth.session?.user.id;
    if(!source||!owner||!canWrite||busy||source.status!=='ai_draft'||source.organization_id!==owner)return;
    const title=revisionTitle.trim(),body=revisionBody.trim();
    if(!reviewInputValid(title,body)){
      error='Ο τίτλος ή το κείμενο είναι άκυρο. Το υπάρχον API δεν υποστηρίζει αλλαγές γραμμής.';return;
    }
    if(title===source.title&&body===source.body){error='Δεν υπάρχουν αλλαγές προς αποθήκευση.';return;}
    busy=true;error=null;success=null;
    try{
      const response=await fetch('/api/admin/hub/editorial/'+encodeURIComponent(source.id)+
        '/revise?organization_id='+owner,{
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({version:source.version,title,body})
      });
      if(response.status===401){auth.invalidateSession();return;}
      if(response.status===403){void auth.bootstrap();return;}
      if(!response.ok)throw new Error();
      const result:unknown=await response.json();
      if(!validHubTransition(result,owner,source.id,source.version,'ai_draft'))throw new Error();
      if(organization!==owner||$auth.session?.user.id!==actor)return;
      success='Η νέα έκδοση του προχείρου αποθηκεύτηκε και απαιτεί νέο έλεγχο.';
      detail=null;inspected=[];showPreview=null;detailRequest++;reload++;
    }catch{error='Η αναθεώρηση δεν επιβεβαιώθηκε. Ανακτήστε πρώτα τη νεότερη έκδοση.';}
    finally{busy=false;}
  }
  async function quarantineImage(){
    const source=detail,owner=organization,actor=$auth.session?.user.id;
    if(!source||!owner||!canWrite||busy||source.status!=='ai_draft'||source.organization_id!==owner)return;
    const file=imageFile,alt=imageAlt.trim();
    if(!file||file.type!=='image/jpeg'||file.size<4||file.size>1048576||
       !alt||alt.length>500||Array.from(alt).some(char=>/\p{Cc}/u.test(char))){
      error='Απαιτείται εικόνα JPEG έως 1 MB και περιγραφή χωρίς χαρακτήρες ελέγχου.';return;
    }
    busy=true;error=null;success=null;
    try{
      const bytes=new Uint8Array(await file.arrayBuffer());
      if(bytes.length!==file.size||bytes[0]!==255||bytes[1]!==216||
         bytes[bytes.length-2]!==255||bytes[bytes.length-1]!==217)throw new Error();
      const chunks:string[]=[];
      for(let start=0;start<bytes.length;start+=8192)
        chunks.push(String.fromCharCode(...bytes.subarray(start,start+8192)));
      const response=await fetch('/api/admin/hub/editorial/'+encodeURIComponent(source.id)+
        '/images/quarantine?organization_id='+owner,{
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({version:source.version,base64:btoa(chunks.join('')),alt_text:alt})
      });
      if(response.status===401){auth.invalidateSession();return;}
      if(response.status===403){void auth.bootstrap();return;}
      if(response.status!==201)throw new Error();
      const result:unknown=await response.json();
      if(!result||typeof result!=='object'||Array.isArray(result)||
         (result as Record<string,unknown>).content_id!==source.id||
         (result as Record<string,unknown>).organization_id!==owner||
         (result as Record<string,unknown>).status!=='quarantined')throw new Error();
      if(organization!==owner||$auth.session?.user.id!==actor)return;
      imageFile=null;imageAlt='';imageInputKey++;
      success='Η εικόνα αποθηκεύτηκε σε καραντίνα. Απαιτείται ξεχωριστός πραγματικός έλεγχος.';
      detail=null;inspected=[];showPreview=null;detailRequest++;reload++;
    }catch{error='Η μεταφόρτωση δεν επιβεβαιώθηκε. Ελέγξτε το JPEG και την τρέχουσα έκδοση.';}
    finally{busy=false;}
  }
  async function returnRejectedForRevision(){
    const source=detail,owner=organization,actor=$auth.session?.user.id;
    if(!source||!owner||!manager||busy||source.organization_id!==owner||
       source.status!=='review_required'||
       (source.text_review!=='rejected'&&source.visual_review!=='rejected'))return;
    if(!confirm('Να επιστραφεί το απορριφθέν περιεχόμενο για διόρθωση;'))return;
    busy=true;error=null;success=null;
    try{
      const response=await fetch('/api/admin/hub/editorial/'+encodeURIComponent(source.id)+
        '/return-for-revision?organization_id='+owner,{
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({version:source.version})
      });
      if(response.status===401){auth.invalidateSession();return;}
      if(response.status===403){void auth.bootstrap();return;}
      if(!response.ok)throw new Error();
      const result:unknown=await response.json();
      if(!validHubTransition(result,owner,source.id,source.version,'ai_draft')||
         (result as Record<string,unknown>).requires_revision!==true)throw new Error();
      if(organization!==owner||$auth.session?.user.id!==actor)return;
      success='Το άρθρο επιστράφηκε ως πρόχειρο για νέα επεξεργασία.';
      detail=null;inspected=[];showPreview=null;detailRequest++;reload++;
    }catch{error='Η επιστροφή για διόρθωση δεν επιβεβαιώθηκε.';}
    finally{busy=false;}
  }
  async function rejectImage(mediaId:string){
    const source=detail,owner=organization,actor=$auth.session?.user.id;
    if(!source||!owner||!manager||busy||source.organization_id!==owner||
       source.status!=='review_required'||
       !source.visuals.some(media=>media.id===mediaId&&media.moderation==='quarantined'))return;
    if(!confirm('Να απορριφθεί αυτή η εικόνα και να απαιτηθεί διόρθωση;'))return;
    busy=true;error=null;success=null;
    try{
      const response=await fetch('/api/admin/hub/editorial/'+encodeURIComponent(source.id)+
        '/images/'+encodeURIComponent(mediaId)+'/reject?organization_id='+owner,{
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({version:source.version})
      });
      if(response.status===401){auth.invalidateSession();return;}
      if(response.status===403){void auth.bootstrap();return;}
      if(!response.ok)throw new Error();
      const result:unknown=await response.json();
      if(!validHubTransition(result,owner,source.id,source.version,'review_required')||
         (result as Record<string,unknown>).media_id!==mediaId||
         (result as Record<string,unknown>).visual_review!=='rejected')throw new Error();
      if(organization!==owner||$auth.session?.user.id!==actor)return;
      success='Η εικόνα απορρίφθηκε. Επιστρέψτε το άρθρο για διόρθωση.';
      detail=null;inspected=[];showPreview=null;detailRequest++;reload++;
    }catch{error='Δεν επιβεβαιώθηκε η απόρριψη εικόνας.';}
    finally{busy=false;}
  }
</script>
<svelte:head><title>HUB · Έλεγχος περιεχομένου | Lefteria FC</title></svelte:head>
{#if $auth.status==='authenticated'&&$auth.session}
  <AdminFrame active="hub" user={$auth.session.user} busy={$auth.busy||busy}
    onLogout={()=>void auth.logout()}>
    <div class="hub-editor" data-testid="hub-editorial-workspace">
      <header>
        
        <h1>Εκπαίδευση · Έλεγχος & δημοσίευση</h1>
        <p>Το AI δημιουργεί μόνο πρόχειρα. Τα επίσημα άρθρα απαιτούν ανθρώπινο έλεγχο κειμένου και εικαστικών, τελική έγκριση και ξεχωριστή δημοσίευση.</p>
        <p><a href="/admin/hub/player-quizzes">Εβδομαδιαία κουίζ ποδοσφαίρου · Δημιουργία, έλεγχος και έγκριση</a></p>
      </header>
      {#if permitted.length}
        <div class="toolbar">
          <span class="admin-organization-context-label" data-testid="admin-hub-organization">{organization==='lefteria_fc'?'Lefteria FC':organization==='lefteria_fc_academy'?'Lefteria FC Academy':'Δεν υπάρχει εξουσιοδοτημένος οργανισμός'}</span>
          <button type="button" disabled={busy||loading} onclick={()=>reload++}>Ανανέωση</button>
        </div>

        {#if canWrite}
          <section class="panel" aria-labelledby="human-draft-title">
            <h2 id="human-draft-title">Νέο ανθρώπινο πρόχειρο</h2>
            <p>Δημιουργία κειμένου στο επιλεγμένο τμήμα. Αποθηκεύεται ιδιωτικά και δεν φτάνει σε μέλη χωρίς έλεγχο, έγκριση και δημοσίευση.</p>
            <div class="fields">
              <label>Τύπος περιεχομένου
                <select bind:value={humanKind}>
                  <option value="article">Άρθρο</option>
                  <option value="drill">Άσκηση</option>
                  <option value="course">Μάθημα</option>
                  <option value="video">Βίντεο · κείμενο</option>
                  <option value="visual">Εικαστικό · περιγραφή</option>
                </select>
              </label>
              <label><span>Τίτλος (μέχρι 180 χαρακτήρες)</span>
                <input bind:value={humanTitle} maxlength="180" placeholder="Τίτλος μαθήματος"/>
              </label>
            </div>
            <label>Περιεχόμενο (μέχρι 20.000 χαρακτήρες)
              <textarea bind:value={humanBody} maxlength="20000" rows="5" placeholder="Κείμενο προς ανθρώπινο έλεγχο"></textarea>
            </label>
            <p class="hint">Προς το παρόν το API δέχεται μόνο απλό κείμενο χωρίς αλλαγές γραμμής. Η μορφοποίηση και τα πολυμέσα ελέγχονται ξεχωριστά.</p>
            <div class="fields">
              <label class="check"><input type="checkbox" bind:checked={humanParent}/> Θεματολογία γονέων</label>
              <label class="check"><input type="checkbox" bind:checked={humanPlayer}/> Θεματολογία αθλητών</label>
              <label class="check"><input type="checkbox" bind:checked={humanAgeRestricted}/> Ειδικό ηλικιακό εύρος</label>
              {#if humanAgeRestricted}
                <label>Από <input type="number" min="4" max="99" bind:value={humanAgeMin}/></label>
                <label>Έως <input type="number" min="4" max="99" bind:value={humanAgeMax}/></label>
              {/if}
            </div>
            <p class="hint">Χωρίς ηλικιακό περιορισμό το εγκεκριμένο άρθρο μπορεί να εξεταστεί για το αντίστοιχο HUB συνδρομητών. Η επιλογή κοινού δεν παρέχει κανέναν ρόλο ή πρόσβαση σε παιδιά.</p>
            <button type="button" disabled={busy||loading||!humanTitle.trim()||!humanBody.trim()||!(humanParent||humanPlayer)}
              onclick={()=>void createHumanDraft()}>Αποθήκευση ιδιωτικού προχείρου</button>
          </section>
        {/if}
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
            <p class="hint">Τελευταίες έως 100 εγγραφές του επιλεγμένου οργανισμού · ο έλεγχος εκτελείται στον εξυπηρετητή.</p>
            <label>Φίλτρο κατάστασης
              <select bind:value={statusFilter}>
                <option value="all">Όλες ({items.length})</option>
                {#each Object.entries(statusLabels) as [key,value]}
                  <option value={key}>{value} ({items.filter(item=>item.status===key).length})</option>
                {/each}
              </select>
            </label>
            {#if loading}<p role="status">Ανάκτηση εγγραφών…</p>
            {:else if items.length===0}<p>Δεν υπάρχουν καταχωρίσεις.</p>
            {:else if filteredItems.length===0}<p>Δεν υπάρχουν εγγραφές με αυτή την κατάσταση.</p>{/if}
            <ul class="drafts">
              {#each filteredItems as item (item.id)}
                <li>
                  <button type="button" disabled={busy} onclick={()=>void selectItem(item)}>
                    <strong>{item.title}</strong>
                    <span>{statusLabels[item.status]} · v{item.version} · {item.audience_parent?'Γονείς ':''}{item.audience_player?'Αθλητές':''}</span>
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
              {#if canWrite&&detail.status==='ai_draft'}
                <section class="revision" aria-labelledby="hub-revision-title">
                  <h3 id="hub-revision-title">Διόρθωση ιδιωτικού προχείρου</h3>
                  <label>Τίτλος <input maxlength="180" bind:value={revisionTitle}/></label>
                  <label>Κείμενο <textarea maxlength="20000" rows="6" bind:value={revisionBody}></textarea></label>
                  <p class="hint">Κάθε αλλαγή δημιουργεί νέα έκδοση. Οι προηγούμενες εγκρίσεις δεν μεταφέρονται.</p>
                  <button type="button" disabled={busy||!revisionTitle.trim()||!revisionBody.trim()}
                    onclick={()=>void reviseDraft()}>Αποθήκευση νέας έκδοσης</button>
                </section>
                <section class="revision" aria-labelledby="hub-image-title">
                  <h3 id="hub-image-title">Προσθήκη εικόνας για έλεγχο</h3>
                  <p class="hint">JPEG έως 1 MB. Ο διακομιστής αφαιρεί μη ασφαλή μεταδεδομένα και κρατά την εικόνα σε καραντίνα μέχρι ανθρώπινη έγκριση.</p>
                  {#key imageInputKey}
                    <label>Εικόνα JPEG
                      <input type="file" accept="image/jpeg" onchange={event=>imageFile=event.currentTarget.files?.[0]??null}/>
                    </label>
                  {/key}
                  <label>Εναλλακτικό κείμενο
                    <input maxlength="500" bind:value={imageAlt} placeholder="Περιγράψτε τι απεικονίζεται"/>
                  </label>
                  <button type="button" disabled={busy||!imageFile||!imageAlt.trim()}
                    onclick={()=>void quarantineImage()}>Αποστολή για έλεγχο εικόνας</button>
                </section>
              {/if}
              {#if detail.visuals.length}
                <h3>Εικαστικά προς έλεγχο</h3>
                {#each detail.visuals as media (media.id)}
                  {@const proof=inspectionKey(detail,media.id)}
                  <div class="image-review">
                    <p>{media.alt_text??'Χωρίς εναλλακτικό κείμενο'} · {media.moderation}</p>
                    {#if media.moderation==='quarantined'}
                      <button type="button" onclick={()=>{showPreview=proof;inspected=inspected.filter(id=>id!==proof);}}>Άνοιγμα πραγματικής εικόνας για έλεγχο</button>
                      {#if manager&&detail.status==='review_required'}
                        <button type="button" disabled={busy} onclick={()=>void rejectImage(media.id)}>Απόρριψη εικόνας</button>
                      {/if}
                      {#if showPreview===proof}
                        <img alt={media.alt_text??'Εικαστικό προς έλεγχο'} loading="lazy"
                          src={'/api/admin/hub/editorial/'+detail.id+'/images/'+media.id+'/preview?organization_id='+detail.organization_id+'&revision_id='+detail.revision_id+'&version='+detail.version}
                          onload={()=>{
                            if(detail&&organization===detail.organization_id&&
                               showPreview===proof&&inspectionKey(detail,media.id)===proof)
                              inspected=[...new Set([...inspected,proof])];
                          }}
                          onerror={()=>{inspected=inspected.filter(id=>id!==proof);}}/>
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
              {#if manager&&detail.status==='review_required'&&
                (detail.text_review==='rejected'||detail.visual_review==='rejected')}
                <button type="button" disabled={busy} onclick={()=>void returnRejectedForRevision()}>Επιστροφή για διόρθωση</button>
              {/if}
              {#if manager&&detail.status==='approved'}
                <button type="button" disabled={busy} onclick={()=>void runAction('publish','published')}>Δημοσίευση στην εφαρμογή</button>
              {/if}
            {:else}
              <p>Επιλέξτε άρθρο για να δείτε το πλήρες κείμενο, τις εικόνες και τις επόμενες επιτρεπόμενες ενέργειες.</p>
            {/if}
          </section>
        </div>
        {#if $auth.session.user.role==='super_admin' && organization}
          {#key $auth.session.user.id+':'+organization}
            <AdminClassroomCurriculum {organization} actorId={$auth.session.user.id}
              onExpired={()=>auth.invalidateSession()}
              onRevoked={()=>void auth.bootstrap()}/>
          {/key}
        {/if}
        <section class="panel fan-note" aria-labelledby="fan-title">
          <h2 id="fan-title">Συνδρομές HUB φιλάθλων</h2>
          {#if $auth.session.user.role==='super_admin'&&
            hasAdminPermission($auth.session.user.permissions,'finance:read')}
            <p><a href="/admin/hub/subscriptions">Συγκεντρωτική επισκόπηση συνδρομών & επαληθευμένων πληρωμών</a></p>
          {/if}
          <p>Η επιλογή θεματολογίας γονέα / αθλητή είναι συνδρομή περιεχομένου, <strong>όχι αλλαγή ρόλου ή εξουσιοδότηση κηδεμόνα.</strong>
            Υπάρχει μόνο δοκιμαστική ροή Payabl με επιβεβαίωση πληρωμής από τον πάροχο και ξεχωριστή πρόοδο μαθημάτων για κάθε νέο συνδρομητή.</p>
          <p class="hint">Η επισκόπηση συνδρομών είναι διαθέσιμη μόνο σε Super Admin, χωρίς στοιχεία συνδρομητών. Δεν υποστηρίζονται πραγματικές επαναλαμβανόμενες χρεώσεις από αυτή τη σελίδα. Μην θεωρείτε εκκρεμείς ή μη επαληθευμένες συναλλαγές ενεργές.</p>
        </section>
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
  select,input:not([type="checkbox"]),textarea{min-height:44px;min-width:145px;padding:9px;background:#232728;color:#fff;border:1px solid #565b5c;border-radius:8px}
  textarea{width:100%;max-width:100%;resize:vertical;font:inherit;line-height:1.55}
  .check{display:flex;align-items:center;gap:9px}
  .check input{width:18px;height:18px;accent-color:#dbaa4f}
  .hint{font-size:12px;color:#bfc5c4}
  .fan-note{margin-top:18px}
  .revision{border:1px solid #454d4d;padding:14px;border-radius:10px;margin:15px 0;display:grid;gap:10px}
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
