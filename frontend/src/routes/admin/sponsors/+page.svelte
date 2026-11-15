<script lang="ts">
  import { getContext } from 'svelte';
  import { browser } from '$app/env';
  import { goto } from '$app/navigation';
  import { ADMIN_AUTH_CONTEXT, type AdminAuthController } from '../../../lib/admin/controller.ts';
  import { hasAdminPermission } from '../../../lib/admin/permissions.ts';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';
  import PublishedMediaBrowser from '../../../lib/admin/PublishedMediaBrowser.svelte';
  import { publishedMediaUrl, type PublishedMediaAsset } from '../../../lib/admin/published-media.ts';
  import PublicImage from '../../../lib/public/PublicImage.svelte';
  import { catalogHref } from '../../../lib/public/PublicCatalog.ts';
  import type { PublicOrganization } from '../../../lib/public/types.ts';

  type Kind = 'sponsor'|'partner';
  type Copy = {name:string;description:string|null;image_url:string|null;
    website_url:string|null;display_order:number};
  type Item = {organization_id:PublicOrganization;kind:Kind;item_id:string;version:number;
    draft:Copy;published:Copy|null;published_at:string|null;updated_at:string|null};
  const auth=getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  const allowed=$derived($auth.status==='authenticated'&&$auth.session &&
    hasAdminPermission($auth.session.user.permissions,'sponsors:read')?
    $auth.session.user.organizations.filter(o=>o.id==='lefteria_fc'||o.id==='lefteria_fc_academy'):[]);
  let organization=$state<PublicOrganization|null>(null);
  let items=$state<Item[]>([]);
  let selectedKey=$state<string|null>(null);
  let draft=$state<Copy|null>(null);
  let loading=$state(false);
  let saving=$state(false);
  let error=$state('');
  let notice=$state('');
  let reload=$state(0);
  let query=$state('');
  let tab=$state<'all'|Kind>('all');
  let mediaOpen=$state(false);
  const selected=$derived(items.find(item=>item.kind+':'+item.item_id===selectedKey)??null);
  const canWrite=$derived(!!organization&&$auth.status==='authenticated'&&!!$auth.session&&
    hasAdminPermission($auth.session.user.permissions,'sponsors:write')&&
    $auth.session.user.organizations.some(o=>o.id===organization));
  const canPublish=$derived(canWrite&&['manager','super_admin'].includes($auth.session?.user.role??''));
  const dirty=$derived(!!draft&&!!selected&&(selected.version===0||
    JSON.stringify(draft)!==JSON.stringify(selected.draft)));
  const filtered=$derived(items.filter(item=>(tab==='all'||item.kind===tab)&&
    (!query.trim()||item.draft.name.toLocaleLowerCase('el-GR').includes(query.trim().toLocaleLowerCase('el-GR')))));
  const title=$derived(organization==='lefteria_fc_academy'?'Ακαδημία':'Πρώτη ομάδα');
  function resetEditor(){selectedKey=null;draft=null;mediaOpen=false;notice='';error='';}
  $effect(()=>{if(browser&&$auth.status==='anonymous')void goto('/admin/login',{replace:true});});
  $effect(()=>{
    if(!allowed.some(org=>org.id===organization)){
      organization=(allowed[0]?.id as PublicOrganization|undefined)??null;
      resetEditor();items=[];
    }
  });
  $effect(()=>{
    const owner=organization,ticket=reload,actor=$auth.session?.user.id;
    void ticket;void actor;
    const controller=new AbortController();
    items=[];loading=false;resetEditor();
    if(!browser||!owner||$auth.status!=='authenticated'||!allowed.some(o=>o.id===owner))return;
    loading=true;
    void(async()=>{
      try{
        const response=await fetch('/api/admin/website/sponsorships?organization_id='+owner,{
          credentials:'same-origin',cache:'no-store',signal:controller.signal,
          headers:{Accept:'application/json'}
        });
        if(response.status===401){auth.invalidateSession();return;}
        if(response.status===403){void auth.bootstrap();throw Error('Τα δικαιώματα προβολής άλλαξαν.');}
        if(!response.ok)throw Error('Δεν είναι διαθέσιμη η εγκεκριμένη συλλογή. Ελέγξτε τη βάση και τα δικαιώματα.');
        const payload:unknown=await response.json();
        if(!Array.isArray(payload)||payload.length>400||
          !payload.every((row:unknown)=>validRow(row,owner)))
          throw Error('Η απόκριση χορηγών/συνεργατών δεν επαληθεύτηκε.');
        if(!controller.signal.aborted){items=payload;error='';}
      }catch(cause){
        if(!controller.signal.aborted)error=cause instanceof Error?cause.message:'Αδυναμία φόρτωσης.';
      }finally{if(!controller.signal.aborted)loading=false;}
    })();
    return()=>controller.abort();
  });
  function validCopy(value:unknown):value is Copy{
    if(!value||typeof value!=='object'||Array.isArray(value))return false;
    const p=value as Partial<Copy>;
    return typeof p.name==='string'&&p.name.length<=160&&
      (p.description===null||typeof p.description==='string')&&
      (p.image_url===null||typeof p.image_url==='string')&&
      (p.website_url===null||typeof p.website_url==='string')&&
      Number.isSafeInteger(p.display_order);
  }
  function validRow(value:unknown,owner:PublicOrganization):value is Item{
    if(!value||typeof value!=='object'||Array.isArray(value))return false;
    const row=value as Partial<Item>;
    return row.organization_id===owner&&(row.kind==='sponsor'||row.kind==='partner')&&
      typeof row.item_id==='string'&&row.item_id.length>0&&row.item_id.length<=128&&
      Number.isSafeInteger(row.version)&&Number(row.version)>=0&&validCopy(row.draft)&&
      (row.published===null||validCopy(row.published))&&
      (row.published_at===null||typeof row.published_at==='string')&&
      (row.updated_at===null||typeof row.updated_at==='string');
  }
  function setOrganization(value:PublicOrganization){
    if(value===organization||!allowed.some(o=>o.id===value))return;
    if(dirty&&!window.confirm('Υπάρχουν μη αποθηκευμένες αλλαγές. Αλλαγή οργανισμού;'))return;
    organization=value;resetEditor();items=[];
  }
  function selectItem(item:Item){
    if(item.organization_id!==organization)return;
    if(dirty&&!window.confirm('Απόρριψη μη αποθηκευμένων αλλαγών;'))return;
    selectedKey=item.kind+':'+item.item_id;
    draft=structuredClone(item.draft);
    notice='';error='';mediaOpen=false;
  }
  function validateDraft(value:Copy|null):boolean{
    if(!value)return false;
    return value.name.trim().length>0&&value.name.length<=160&&
      (!value.description||value.description.length<=800)&&
      (!value.image_url||publishedMediaUrl(value.image_url)!==null)&&
      (!value.website_url||(/^https:\/\//i.test(value.website_url)&&
        publishedMediaUrl(value.website_url)!==null))&&
      Number.isSafeInteger(value.display_order)&&value.display_order>=0&&value.display_order<=10000;
  }
  async function mutate(path:'/draft'|'/publication',payload:unknown):Promise<Item>{
    if(!organization||!selected)throw Error('Επιλέξτε πρώτα εγκεκριμένο οργανισμό.');
    const response=await fetch('/api/admin/website/sponsorships'+path+'?organization_id='+organization,{
      method:'POST',credentials:'same-origin',cache:'no-store',
      headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1',Accept:'application/json'},
      body:JSON.stringify(payload)
    });
    if(response.status===401){auth.invalidateSession();throw Error('Η σύνδεση έληξε.');}
    if(response.status===403){void auth.bootstrap();throw Error('Η πρόσβαση απορρίφθηκε.');}
    if(response.status===409)throw Error('Η εγγραφή άλλαξε από άλλο διαχειριστή. Απαιτείται ανανέωση.');
    if(response.status===404)throw Error('Η χορηγία δεν υπάρχει πια στον ενεργό δημοσιευμένο κατάλογο.');
    if(!response.ok)throw Error('Η ενέργεια δεν έγινε αποδεκτή από τον διακομιστή.');
    const item:unknown=await response.json();
    if(!validRow(item,organization)||item.item_id!==selected.item_id||item.kind!==selected.kind||
       item.version<=selected.version)throw Error('Η απάντηση δεν επαληθεύτηκε.');
    return item;
  }
  function accept(item:Item){
    items=items.map(existing=>existing.kind===item.kind&&existing.item_id===item.item_id?item:existing);
    selectedKey=item.kind+':'+item.item_id;
    draft=structuredClone(item.draft);
    mediaOpen=false;
  }
  async function save(){
    if(!canWrite||!selected||!organization||!draft||saving)return;
    if(!validateDraft(draft)){error='Ελέγξτε όνομα, κείμενο, HTTPS εικόνα/ιστοσελίδα και σειρά εμφάνισης.';return;}
    saving=true;error='';notice='';
    try{
      const row=await mutate('/draft',{kind:selected.kind,item_id:selected.item_id,
        expected_version:selected.version,draft});
      accept(row);notice='Το ιδιωτικό πρόχειρο αποθηκεύτηκε. Η δημόσια ιστοσελίδα δεν άλλαξε.';
    }catch(cause){error=cause instanceof Error?cause.message:'Αποτυχία αποθήκευσης.';}
    finally{saving=false;}
  }
  async function publish(publishNext:boolean){
    if(!canPublish||!selected||selected.version<1||dirty||saving)return;
    if(!window.confirm(publishNext?'Δημοσίευση εγκεκριμένου περιεχομένου στην ιστοσελίδα;':
      'Απόσυρση αλλαγών και επιστροφή στα αρχικά δημοσιευμένα στοιχεία;'))return;
    saving=true;error='';notice='';
    try{
      const row=await mutate('/publication',{kind:selected.kind,item_id:selected.item_id,
        expected_version:selected.version,publish:publishNext});
      accept(row);
      notice=publishNext?'Το εγκεκριμένο περιεχόμενο δημοσιεύτηκε.':
        'Οι αλλαγές αποσύρθηκαν. Εμφανίζονται τα αρχικά εισαγμένα στοιχεία.';
    }catch(cause){error=cause instanceof Error?cause.message:'Η δημοσίευση απέτυχε.';}
    finally{saving=false;}
  }
  function useImage(asset:PublishedMediaAsset){
    if(!canWrite||saving||!draft||!organization||asset.organization_id!==organization)return;
    const url=publishedMediaUrl(asset.url);
    if(!url)return;
    draft.image_url=url;mediaOpen=false;
    notice='Η εικόνα επιλέχθηκε. Αποθηκεύστε το πρόχειρο για να διατηρηθεί.';
  }
  const href=(item:Item)=>catalogHref(item.kind==='sponsor'?'sponsors':'partners',
    {id:item.item_id,organization_id:item.organization_id},item.published?.name??item.draft.name);
</script>

<svelte:head><title>Χορηγοί & συνεργάτες | Διαχείριση Λευτεριά FC</title></svelte:head>
{#if $auth.status==='authenticated'&&$auth.session}
  <AdminFrame user={$auth.session.user} active="sponsors" busy={$auth.busy||saving} onLogout={()=>void auth.logout()}>
    <div class="sponsorship-admin" data-testid="admin-sponsorship-editor">
      <header class="sponsorship-header">
        <p class="admin-eyebrow">ΙΣΤΟΣΕΛΙΔΑ · ΣΥΝΕΡΓΑΣΙΕΣ</p>
        <h1>Χορηγοί & συνεργάτες</h1>
        <p class="admin-muted">Διαχείριση εγκεκριμένων συνεργασιών Πρώτης Ομάδας και Ακαδημίας με χωριστές άδειες, ιδιωτικά πρόχειρα και ελεγχόμενη δημοσίευση.</p>
        <p class="sponsorship-safety">Πρώτη λειτουργική έκδοση: επεξεργασία ήδη εισαγμένων εγγραφών. Δημιουργία, λήξη σύμβασης, διαγραφή και πληρωμές δεν έχουν ενεργοποιηθεί.</p>
      </header>
      {#if allowed.length&&organization}
        <section class="admin-panel sponsorship-target">
          <label>Οργανισμός <select value={organization} onchange={e=>setOrganization(e.currentTarget.value as PublicOrganization)}
            aria-label="Οργανισμός χορηγιών">
            {#each allowed as item (item.id)}
              <option value={item.id}>{item.id==='lefteria_fc'?'Πρώτη ομάδα':'Ακαδημία'}</option>
            {/each}
          </select></label>
          <p class="admin-muted">Ανοιχτός οργανισμός: {title}. Το API επιβεβαιώνει τα δικαιώματα ξανά για κάθε μεταβολή.</p>
        </section>
        <div class="sponsorship-grid">
          <aside class="admin-panel sponsorship-list" aria-label="Υφιστάμενοι χορηγοί και συνεργάτες">
            <h2>Εγκεκριμένος κατάλογος</h2>
            <label>Αναζήτηση <input type="search" bind:value={query} placeholder="Όνομα χορηγού…"/></label>
            <label>Είδος <select bind:value={tab}>
              <option value="all">Όλα</option>
              <option value="sponsor">Χορηγοί</option>
              <option value="partner">Συνεργάτες</option>
            </select></label>
            <button type="button" class="admin-secondary" disabled={loading||saving||dirty}
              onclick={()=>reload++}>Ανανέωση καταλόγου</button>
            {#if loading}<p role="status">Ανάκτηση πραγματικών συνεργασιών…</p>{/if}
            {#if error&&!selected}<p role="alert" class="sponsorship-error">{error}</p>{/if}
            {#if !loading&&!error&&filtered.length===0}
              <p class="admin-muted">Δεν υπάρχουν καταχωρίσεις για αυτά τα φίλτρα.</p>
            {/if}
            <nav class="sponsorship-records" aria-label="Επιλογή χορηγού ή συνεργάτη">
              {#each filtered as item (item.kind+':'+item.item_id)}
                <button type="button" class:selected={selectedKey===item.kind+':'+item.item_id}
                  aria-current={selectedKey===item.kind+':'+item.item_id?'true':undefined}
                  onclick={()=>selectItem(item)}>
                  <span>{item.kind==='sponsor'?'ΧΟΡΗΓΟΣ':'ΣΥΝΕΡΓΑΤΗΣ'}</span>
                  <strong>{item.draft.name}</strong>
                  <small>{item.published?'Δημοσιευμένη αλλαγή':item.version>0?'Ιδιωτικό πρόχειρο':'Αρχική καταχώριση'}</small>
                </button>
              {/each}
            </nav>
          </aside>
          <main class="sponsorship-workspace">
            {#if selected&&draft}
              <section class="admin-panel sponsorship-editor">
                <div class="sponsorship-editor-head">
                  <div>
                    <p class="admin-eyebrow">{selected.kind==='sponsor'?'ΧΟΡΗΓΟΣ':'ΣΥΝΕΡΓΑΤΗΣ'} · {title}</p>
                    <h2>{draft.name}</h2>
                    <p class="admin-muted">Έκδοση {selected.version} · {selected.published_at?'Δημοσιευμένη ενημέρωση':'Χωρίς δημοσιευμένη ενημέρωση'}</p>
                  </div>
                  {#if selected.published}<a class="admin-secondary" href={href(selected)}
                    target="_blank" rel="noopener noreferrer">Δημόσια προβολή</a>{/if}
                </div>
                <form onsubmit={e=>{e.preventDefault();void save();}}>
                  <label>Εμφανιζόμενο όνομα <input required maxlength="160" value={draft.name}
                    oninput={e=>{if(draft)draft.name=e.currentTarget.value;}} disabled={!canWrite||saving}/></label>
                  <label>{selected.kind==='sponsor'?'Περιγραφή':'Σύντομο κείμενο συνεργασίας'}
                    <textarea rows="4" maxlength="800" value={draft.description??''}
                      oninput={e=>{if(draft)draft.description=e.currentTarget.value||null;}}
                      disabled={!canWrite||saving}></textarea></label>
                  <label>{selected.kind==='sponsor'?'URL λογότυπου':'URL εικόνας εξωφύλλου'}
                    <input type="url" maxlength="2048" placeholder="https://… ή /api/uploads/…"
                      value={draft.image_url??''} oninput={e=>{if(draft)draft.image_url=e.currentTarget.value||null;}}
                      disabled={!canWrite||saving}/></label>
                  {#if canWrite}
                    <button class="admin-secondary" type="button" aria-expanded={mediaOpen}
                      disabled={saving} onclick={()=>mediaOpen=!mediaOpen}>
                      {mediaOpen?'Κλείσιμο βιβλιοθήκης':'Επιλογή δημοσιευμένης εικόνας'}
                    </button>
                  {/if}
                  {#if mediaOpen&&canWrite}
                    <div class="sponsorship-media">
                      <PublishedMediaBrowser {organization} onPick={useImage} embedded/>
                    </div>
                  {/if}
                  <label>Επίσημη ιστοσελίδα συνεργάτη (HTTPS)
                    <input type="url" maxlength="2048" placeholder="https://…"
                      value={draft.website_url??''} oninput={e=>{if(draft)draft.website_url=e.currentTarget.value||null;}}
                      disabled={!canWrite||saving}/></label>
                  <label>Σειρά εμφάνισης <input type="number" min="0" max="10000" step="1"
                    value={draft.display_order} oninput={e=>{if(draft)draft.display_order=e.currentTarget.valueAsNumber;}}
                    disabled={!canWrite||saving}/></label>
                  <p class="admin-muted">Η ταυτότητα της εγγραφής, τα επιπλέον στοιχεία συνεργασίας και η αρχική εισαγωγή παραμένουν ανέπαφα.</p>
                  {#if error}<p role="alert" class="sponsorship-error">{error}</p>{/if}
                  {#if notice}<p role="status" class="sponsorship-notice">{notice}</p>{/if}
                  <div class="sponsorship-actions">
                    {#if canWrite}<button type="submit" class="admin-primary" disabled={saving||!dirty||!validateDraft(draft)}>
                      {saving?'Αποθήκευση…':'Αποθήκευση ιδιωτικού πρόχειρου'}
                    </button>{/if}
                    {#if canPublish&&selected.version>0}
                      <button type="button" class="admin-secondary" disabled={saving||dirty}
                        onclick={()=>void publish(!selected.published)}>
                        {selected.published?'Απόσυρση αλλαγών':'Έγκριση & δημοσίευση'}
                      </button>
                      {#if selected.published}
                        <button type="button" class="admin-secondary" disabled={saving||dirty}
                          onclick={()=>void publish(true)}>Επαναδημοσίευση πρόχειρου</button>
                      {/if}
                    {/if}
                  </div>
                  {#if selected.published&&dirty}<p class="admin-muted">Οι νέες αλλαγές απαιτούν νέα αποθήκευση και ανθρώπινη έγκριση.</p>{/if}
                </form>
              </section>
              <section class="admin-panel sponsorship-preview" aria-label="Ιδιωτική προεπισκόπηση">
                <div class="sponsorship-preview-header"><strong>LIVE PREVIEW</strong><span>Μόνο πρόχειρο · όχι δημόσια αλλαγή</span></div>
                {#if draft.image_url&&publishedMediaUrl(draft.image_url)}
                  <div class="sponsorship-preview-image"><PublicImage src={draft.image_url} alt={draft.name}/></div>
                {/if}
                <h2>{draft.name}</h2>
                {#if draft.description}<p>{draft.description}</p>{/if}
                {#if draft.website_url&&/^https:\/\//i.test(draft.website_url)}
                  <a target="_blank" rel="noopener noreferrer" href={draft.website_url}>Επίσημος ιστότοπος ↗</a>
                {/if}
              </section>
            {:else}
              <section class="admin-panel sponsorship-intro">
                <h2>Διαχείριση συνεργασιών ανά οργανισμό</h2>
                <p>Επιλέξτε χορηγό ή συνεργάτη για να δείτε τα πραγματικά δημοσιευμένα στοιχεία του και να διαμορφώσετε ένα ιδιωτικό πρόχειρο.</p>
                <p class="admin-muted">Η δημοσίευση γίνεται χωριστά, με αρμοδιότητα Manager ή Super Admin.</p>
              </section>
            {/if}
          </main>
        </div>
      {:else}
        <section class="admin-panel"><p role="alert">Δεν έχετε δικαίωμα sponsors:read ή εξουσιοδοτημένο οργανισμό.</p></section>
      {/if}
    </div>
  </AdminFrame>
{:else}
  <AdminSessionStatus unavailable={$auth.status==='unavailable'} busy={$auth.busy} error={$auth.error}
    onRetry={()=>void auth.bootstrap()} onLogout={()=>void auth.logout()}/>
{/if}
<style>
.sponsorship-admin{display:grid;gap:18px;color:#f1f2ef;min-width:0}
.sponsorship-header h1{font:italic 900 clamp(32px,4vw,55px)/1.05 'Fira Sans Extra Condensed',sans-serif;margin:7px 0 12px}
.sponsorship-header .admin-muted{max-width:75ch}
.sponsorship-safety{max-width:78ch;padding:12px 16px;border-left:3px solid #d6a546;background:#252115;color:#f0d6a2;font-size:13px}
.sponsorship-target{display:flex;flex-wrap:wrap;gap:18px;align-items:end;padding:17px}
.sponsorship-target label{min-width:min(290px,100%)}
.sponsorship-grid{display:grid;grid-template-columns:minmax(230px,310px) minmax(0,1fr);align-items:start;gap:18px}
.sponsorship-list{display:grid;gap:12px;padding:17px;min-width:0}
.sponsorship-list h2{font-size:21px;margin:0}
.sponsorship-admin label{display:grid;gap:7px;font-size:12px;font-weight:750;color:#e8e7e1}
.sponsorship-admin :is(input,select,textarea){min-width:0;width:100%;background:#111617;border:1px solid #68675e;color:#fff;border-radius:5px;padding:11px 12px;font:400 14px/1.45 'Fira Sans',sans-serif}
.sponsorship-admin :is(button,input,textarea,select,a):focus-visible{outline:3px solid #f5c55e;outline-offset:2px}
.sponsorship-records{display:grid;gap:8px;max-height:64vh;overflow-y:auto}
.sponsorship-records button{text-align:left;display:grid;gap:5px;padding:12px;border:1px solid #514e44;background:#1a1e20;border-radius:6px;color:#fff;cursor:pointer}
.sponsorship-records button.selected{border-color:#efbd49;background:#282317}
.sponsorship-records span{font-size:10px;letter-spacing:.05em;color:#ecc36d;font-weight:800}
.sponsorship-records strong{font-size:15px;overflow-wrap:anywhere}
.sponsorship-records small{font-size:12px;color:#d1d0ca}
.sponsorship-workspace{min-width:0;display:grid;gap:17px}
.sponsorship-editor,.sponsorship-preview,.sponsorship-intro{padding:clamp(16px,2vw,24px);min-width:0}
.sponsorship-editor-head{display:flex;justify-content:space-between;align-items:start;gap:16px;flex-wrap:wrap;margin-bottom:17px}
.sponsorship-editor-head h2,.sponsorship-intro h2{font-size:23px;margin:0 0 5px}
.sponsorship-editor form{display:grid;gap:15px}
.sponsorship-actions{display:flex;gap:10px;flex-wrap:wrap}
.sponsorship-actions :is(button),.sponsorship-editor .admin-secondary{min-height:44px}
.sponsorship-error{color:#ffafaf;font-size:13px}
.sponsorship-notice{color:#b4edbc;font-size:13px}
.sponsorship-media{max-height:520px;overflow-y:auto;min-width:0}
.sponsorship-preview{display:grid;gap:14px;align-content:start}
.sponsorship-preview-header{display:flex;justify-content:space-between;gap:9px;flex-wrap:wrap;color:#ecbd51;font-size:11px;letter-spacing:.03em}
.sponsorship-preview-header span{color:#d3d2cb}
.sponsorship-preview-image{height:220px;background:#141719;display:grid;place-items:center;border:1px solid #525048;border-radius:6px}
.sponsorship-preview-image :global(img){max-height:100%;max-width:100%;object-fit:contain;padding:10px}
.sponsorship-preview h2{font:italic 900 clamp(25px,3vw,35px)/1.1 'Fira Sans Extra Condensed',sans-serif;margin:0}
.sponsorship-preview p{color:#ddd;white-space:pre-wrap;overflow-wrap:anywhere}
.sponsorship-preview>a{color:#f3c657}
@media(max-width:960px){.sponsorship-grid{grid-template-columns:1fr}.sponsorship-records{max-height:250px}}
</style>
