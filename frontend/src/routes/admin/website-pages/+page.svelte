<script lang="ts">
  import { getContext } from 'svelte';
  import { browser } from '$app/env';
  import { goto } from '$app/navigation';
  import { ADMIN_AUTH_CONTEXT, type AdminAuthController } from '../../../lib/admin/controller.ts';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';
  import { hasAdminPermission } from '../../../lib/admin/permissions.ts';
  import WebsitePageBlocks from '../../../lib/public/WebsitePageBlocks.svelte';
  import { websitePagePath, type WebsiteBlock, type WebsitePageContent, type WebsitePageRecord } from '../../../lib/public/website-pages.ts';
  import type { PublicOrganization } from '../../../lib/public/types.ts';

  const auth = getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  const blank = (): WebsitePageContent => ({
    title:'', summary:'', seo_title:'', seo_description:'',
    menu_parent:'hidden', menu_order:0,
    blocks:[{type:'heading',text:'Ο τίτλος της ενότητας',url:null}]
  });
  const parents = [
    {value:'hidden',label:'Δεν εμφανίζεται στο μενού'},
    {value:'root',label:'Κεντρικό μενού · νέα ενότητα'},
    {value:'club',label:'Σύλλογος'},
    {value:'team',label:'Πρώτη ομάδα'},
    {value:'academy',label:'Ακαδημία'},
    {value:'matches',label:'Αγώνες'},
    {value:'news',label:'Νέα'},
    {value:'events',label:'Εκδηλώσεις'},
    {value:'shop',label:'Κατάστημα'},
    {value:'contact',label:'Επικοινωνία'}
  ] as const;

  const permitted = $derived($auth.status==='authenticated' && !!$auth.session &&
    hasAdminPermission($auth.session.user.permissions,'news:read') ?
    $auth.session.user.organizations.filter(o=>o.id==='lefteria_fc' || o.id==='lefteria_fc_academy') : []);
  let organization = $state<PublicOrganization|null>(null);
  let records = $state<WebsitePageRecord[]>([]);
  let picked = $state<string|null>(null);
  let creating = $state(false);
  let slug = $state('');
  let draft = $state<WebsitePageContent>(blank());
  let loading = $state(false);
  let saving = $state(false);
  let retry = $state(0);
  let notice = $state('');
  let problem = $state('');
  let preview = $state(true);
  const selected = $derived(records.find(p=>p.slug===picked) ?? null);
  const canWrite = $derived(!!organization && $auth.status==='authenticated' && !!$auth.session &&
    hasAdminPermission($auth.session.user.permissions,'news:write') &&
    $auth.session.user.organizations.some(o=>o.id===organization));
  const canPublish = $derived(canWrite && ['manager','super_admin'].includes($auth.session?.user.role ?? ''));
  const dirty = $derived(creating || !!selected && JSON.stringify(draft)!==JSON.stringify(selected.draft));
  const editing = $derived(creating || !!selected);

  $effect(() => {
    if (browser && $auth.status==='anonymous') void goto('/admin/login',{replace:true});
  });
  $effect(() => {
    if (!permitted.some(o=>o.id===organization)) {
      organization = permitted[0]?.id as PublicOrganization ?? null;
      picked = null; creating = false; slug = ''; draft = blank();
    }
  });
  $effect(() => {
    const owner=organization, ticket=retry, actor=$auth.session?.user.id;
    void ticket;void actor;
    records=[];loading=false;
    if (!browser || !owner || $auth.status!=='authenticated' ||
        !permitted.some(o=>o.id===owner)) return;
    const controller=new AbortController();
    loading=true;
    void (async()=>{
      try {
        const response=await fetch('/api/admin/website/pages?organization_id='+owner,{
          credentials:'same-origin',cache:'no-store',signal:controller.signal
        });
        if(response.status===401){auth.invalidateSession();return;}
        if(response.status===403){void auth.bootstrap();throw Error('Η πρόσβαση άλλαξε.');}
        if(!response.ok)throw Error('Δεν φορτώθηκαν οι σελίδες. Ελέγξτε τη βάση δεδομένων και τα δικαιώματα.');
        const data:unknown=await response.json();
        if(!Array.isArray(data) || data.length>100 || !data.every(p=>p &&
           p.organization_id===owner && typeof p.slug==='string' &&
           Number.isSafeInteger(p.version) && p.version>0 && p.draft &&
           Array.isArray(p.draft.blocks) && (p.published===null || p.published &&
           Array.isArray(p.published.blocks))))
          throw Error('Η απόκριση των σελίδων δεν επαληθεύτηκε.');
        if(!controller.signal.aborted){records=data;problem='';}
      } catch(e) {
        if(!controller.signal.aborted)problem=e instanceof Error?e.message:'Αδύνατη φόρτωση.';
      } finally {if(!controller.signal.aborted)loading=false;}
    })();
    return ()=>controller.abort();
  });

  function setOrganization(next: PublicOrganization) {
    if(next===organization || !permitted.some(o=>o.id===next))return;
    if(dirty && !window.confirm('Οι αλλαγές δεν έχουν αποθηκευτεί. Αλλαγή οργανισμού;'))return;
    organization=next;picked=null;creating=false;slug='';draft=blank();notice='';problem='';
  }
  function openPage(page: WebsitePageRecord) {
    if(dirty && !window.confirm('Απόρριψη μη αποθηκευμένων αλλαγών;'))return;
    picked=page.slug;slug=page.slug;draft=structuredClone(page.draft);
    creating=false;problem='';notice='';
  }
  function newPage() {
    if(dirty && !window.confirm('Απόρριψη μη αποθηκευμένων αλλαγών;'))return;
    creating=true;picked=null;slug='';draft=blank();problem='';notice='';
  }
  function addBlock(type: WebsiteBlock['type']) {
    if(!canWrite || draft.blocks.length>=40)return;
    draft.blocks=[...draft.blocks,{type,text:type==='heading'?'Νέα ενότητα':'',url:type==='image'||type==='button'?'':null}];
  }
  function removeBlock(index:number) {
    if(draft.blocks.length<=1)return;
    draft.blocks=draft.blocks.filter((_,i)=>i!==index);
  }
  function moveBlock(index:number,direction:-1|1) {
    const other=index+direction;
    if(other<0||other>=draft.blocks.length)return;
    const items=[...draft.blocks];[items[index],items[other]]=[items[other],items[index]];
    draft.blocks=items;
  }
  function valid():boolean {
    if(!/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(slug) || slug.length<2 || slug.length>80 ||
       !draft.title.trim() || draft.title.length>180 || draft.summary.length>500 ||
       draft.seo_title.length>180 || draft.seo_description.length>300 ||
       !parents.some(p=>p.value===draft.menu_parent) ||
       !Number.isSafeInteger(draft.menu_order) || draft.menu_order<0 || draft.menu_order>1000 ||
       draft.blocks.length<1 || draft.blocks.length>40 ||
       draft.blocks.some(b=>!b.text.trim() || b.text.length>5000 ||
         ((b.type==='image'||b.type==='button') && (!b.url ||
           !(/^(\/(?!\/)|https:\/\/)/i.test(b.url)) || b.url.includes('\\')))))
       return false;
    return true;
  }
  async function mutate(url:string,body:unknown,method:'POST'|'PUT'):Promise<WebsitePageRecord> {
    const response=await fetch(url,{method,credentials:'same-origin',cache:'no-store',
      headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1',Accept:'application/json'},
      body:JSON.stringify(body)});
    if(response.status===401){auth.invalidateSession();throw Error('Η σύνδεση έληξε.');}
    if(response.status===403){void auth.bootstrap();throw Error('Δεν έχετε άδεια για αυτή την ενέργεια.');}
    if(response.status===409)throw Error('Αλλαγή από άλλον διαχειριστή ή ήδη υπάρχον URL. Ανανέωση απαιτείται.');
    if(!response.ok)throw Error('Η ενέργεια απορρίφθηκε. Ελέγξτε τα πεδία και τα δικαιώματα.');
    const value:unknown=await response.json();
    const p=value as WebsitePageRecord;
    if(!value || typeof value!=='object' || p.organization_id!==organization ||
      p.slug!==slug || !Number.isSafeInteger(p.version) || !p.draft || !Array.isArray(p.draft.blocks))
      throw Error('Η απάντηση αποθήκευσης δεν επαληθεύτηκε. Ανανέωση απαιτείται.');
    return p;
  }
  function accept(page: WebsitePageRecord) {
    records=[page,...records.filter(p=>p.slug!==page.slug)];
    picked=page.slug;slug=page.slug;creating=false;
    draft=structuredClone(page.draft);
  }
  async function save() {
    if(!organization || !canWrite || saving || !editing)return;
    if(!valid()){problem='Ελέγξτε τίτλο, URL (πεζά λατινικά), θέση μενού, κείμενο και εικόνες.';return;}
    saving=true;problem='';notice='';
    try {
      const url='/api/admin/website/pages'+(creating?'':'/'+encodeURIComponent(slug))+
        '?organization_id='+organization;
      const page=await mutate(url,creating?{slug,page:draft}:{expected_version:selected?.version,page:draft},
        creating?'POST':'PUT');
      accept(page);notice='Το ιδιωτικό πρόχειρο αποθηκεύτηκε. Η δημοσιευμένη σελίδα δεν άλλαξε.';
    } catch(e){problem=e instanceof Error?e.message:'Αποτυχία αποθήκευσης.';}
    finally{saving=false;}
  }
  async function publication(publish:boolean) {
    if(!organization || !selected || !canPublish || dirty || saving)return;
    if(!window.confirm(publish?'Δημοσίευση του ελεγμένου πρόχειρου στη δημόσια ιστοσελίδα;':
      'Απόσυρση της σελίδας από τη δημόσια ιστοσελίδα;'))return;
    saving=true;problem='';notice='';
    try {
      const page=await mutate('/api/admin/website/pages/'+encodeURIComponent(slug)+
        '/publication?organization_id='+organization,
        {expected_version:selected.version,publish},'POST');
      accept(page);notice=publish?'Η σελίδα δημοσιεύτηκε.':'Η δημόσια σελίδα αποσύρθηκε.';
    } catch(e){problem=e instanceof Error?e.message:'Η δημοσίευση απέτυχε.';}
    finally{saving=false;}
  }
</script>

<svelte:head><title>Σελίδες · Visual Builder | Λευτεριά FC</title></svelte:head>

{#if $auth.status==='authenticated' && $auth.session}
  <AdminFrame user={$auth.session.user} active="pages" busy={$auth.busy || saving}
    onLogout={()=>void auth.logout()}>
    <div class="cms" data-testid="website-page-builder">
      <header class="cms-header">
        <div><p class="admin-eyebrow">ΙΣΤΟΣΕΛΙΔΑ · NATIVE CMS</p>
          <h1>Σελίδες · Visual Builder</h1>
          <p class="admin-muted">Δημιουργήστε σελίδες χωρίς plugins, τοποθετήστε τις σε ενότητες του μενού και δείτε αμέσως την εμφάνιση. Η αποθήκευση είναι ιδιωτική· η δημοσίευση απαιτεί έγκριση.</p>
        </div>
        <div class="cms-header-actions">
          <a class="admin-secondary" href="/admin/club">Προφίλ συλλόγου</a>
          <a class="admin-secondary" href="/admin/news">Άρθρα & νέα</a>
        </div>
      </header>
      {#if permitted.length}
        <section class="admin-panel cms-organization">
          <label>Περιεχόμενο οργανισμού
            <select value={organization ?? ''} onchange={e=>setOrganization(e.currentTarget.value as PublicOrganization)}
              aria-label="Οργανισμός διαχείρισης σελίδων">
              {#each permitted as owner (owner.id)}
                <option value={owner.id}>{owner.id==='lefteria_fc'?'Πρώτη ομάδα':'Ακαδημία'}</option>
              {/each}
            </select>
          </label>
          <p class="admin-muted">Κάθε σελίδα, θέση μενού και δημοσίευση ανήκει αποκλειστικά στον επιλεγμένο οργανισμό.</p>
        </section>
        <div class="cms-grid">
          <aside class="admin-panel cms-library" aria-label="Σελίδες οργανισμού">
            <div class="cms-library-heading"><h2>Σελίδες</h2>
              {#if canWrite}<button type="button" class="admin-primary" onclick={newPage}>+ Νέα</button>{/if}
            </div>
            {#if loading}<p role="status">Φόρτωση…</p>{/if}
            {#if !loading && records.length===0}<p class="admin-muted">Δεν έχουν δημιουργηθεί σελίδες.</p>{/if}
            <nav aria-label="Επεξεργασία σελίδας" class="cms-page-list">
              {#each records as page (page.slug)}
                <button type="button" class:cms-selected={picked===page.slug}
                  onclick={()=>openPage(page)} aria-current={picked===page.slug?'page':undefined}>
                  <strong>{page.draft.title || page.slug}</strong>
                  <small>/{page.slug} · {page.published ? 'Δημοσιευμένη' : 'Πρόχειρο'}</small>
                </button>
              {/each}
            </nav>
            <button type="button" class="admin-secondary" onclick={()=>retry++} disabled={loading}>Ανανέωση λίστας</button>
          </aside>
          <div class="cms-workspace">
            {#if editing}
              <section class="admin-panel cms-editor">
                <div class="cms-editor-head"><div><p class="admin-eyebrow">{creating?'ΝΕΑ ΣΕΛΙΔΑ':'ΕΠΕΞΕΡΓΑΣΙΑ ΣΕΛΙΔΑΣ'}</p><h2>{draft.title || 'Χωρίς τίτλο'}</h2></div>
                  <label class="cms-preview-toggle"><input type="checkbox" bind:checked={preview}/> Ζωντανή προεπισκόπηση</label>
                </div>
                <form onsubmit={e=>{e.preventDefault();void save();}}>
                  <div class="cms-field-grid">
                    <label>Τίτλος σελίδας <input required maxlength="180" bind:value={draft.title} disabled={!canWrite || saving}/></label>
                    <label>URL (slug, μοναδικό στον οργανισμό) <input required minlength="2" maxlength="80"
                      pattern="[a-z0-9]+(-[a-z0-9]+)*" placeholder="our-values" bind:value={slug}
                      disabled={!creating || !canWrite || saving}/></label>
                  </div>
                  <label>Σύντομη περιγραφή <textarea rows="2" maxlength="500" bind:value={draft.summary} disabled={!canWrite || saving}></textarea></label>
                  <div class="cms-field-grid">
                    <label>Θέση στο μενού <select bind:value={draft.menu_parent} disabled={!canWrite || saving}>
                      {#each parents as parent (parent.value)}<option value={parent.value}>{parent.label}</option>{/each}
                    </select></label>
                    <label>Σειρά στο μενού (0 = πρώτη) <input type="number" min="0" max="1000" step="1" bind:value={draft.menu_order} disabled={!canWrite || saving}/></label>
                  </div>
                  <details class="cms-seo"><summary>SEO · τίτλος και περιγραφή αναζήτησης</summary>
                    <label>SEO τίτλος <input maxlength="180" bind:value={draft.seo_title} disabled={!canWrite || saving}/></label>
                    <label>Meta description <textarea rows="2" maxlength="300" bind:value={draft.seo_description} disabled={!canWrite || saving}></textarea></label>
                  </details>
                  <div class="cms-block-heading"><h3>Περιεχόμενο σελίδας</h3><span>{draft.blocks.length} / 40 blocks</span></div>
                  <div class="cms-block-list">
                    {#each draft.blocks as block, index (index)}
                      <div class="cms-block" data-testid="website-page-block">
                        <div class="cms-block-tools">
                          <strong>Block {index+1} · {block.type==='heading'?'Τίτλος':block.type==='paragraph'?'Κείμενο':block.type==='image'?'Εικόνα':'Κουμπί'}</strong>
                          <div>
                            <button type="button" aria-label="Μετακίνηση πάνω" title="Πάνω" disabled={index===0 || !canWrite || saving} onclick={()=>moveBlock(index,-1)}>↑</button>
                            <button type="button" aria-label="Μετακίνηση κάτω" title="Κάτω" disabled={index===draft.blocks.length-1 || !canWrite || saving} onclick={()=>moveBlock(index,1)}>↓</button>
                            <button type="button" aria-label="Αφαίρεση block" title="Αφαίρεση" disabled={draft.blocks.length===1 || !canWrite || saving} onclick={()=>removeBlock(index)}>×</button>
                          </div>
                        </div>
                        <label>{block.type==='image'?'Εναλλακτικό κείμενο εικόνας':block.type==='button'?'Ετικέτα κουμπιού':'Κείμενο'}
                          {#if block.type==='paragraph'}
                            <textarea rows="4" maxlength="5000" value={block.text} oninput={e=>draft.blocks[index].text=e.currentTarget.value} disabled={!canWrite || saving}></textarea>
                          {:else}
                            <input maxlength="5000" value={block.text} oninput={e=>draft.blocks[index].text=e.currentTarget.value} disabled={!canWrite || saving}/>
                          {/if}
                        </label>
                        {#if block.type==='image' || block.type==='button'}
                          <label>{block.type==='image'?'Ασφαλές URL εικόνας':'Προορισμός κουμπιού'}
                            <input type="text" maxlength="2048" placeholder="https://… ή /…" value={block.url ?? ''}
                              oninput={e=>draft.blocks[index].url=e.currentTarget.value} disabled={!canWrite || saving}/>
                          </label>
                        {/if}
                      </div>
                    {/each}
                  </div>
                  {#if canWrite}<div class="cms-add-block" aria-label="Προσθήκη block">
                    <button type="button" onclick={()=>addBlock('heading')} disabled={saving || draft.blocks.length>=40}>+ Τίτλος</button>
                    <button type="button" onclick={()=>addBlock('paragraph')} disabled={saving || draft.blocks.length>=40}>+ Κείμενο</button>
                    <button type="button" onclick={()=>addBlock('image')} disabled={saving || draft.blocks.length>=40}>+ Εικόνα</button>
                    <button type="button" onclick={()=>addBlock('button')} disabled={saving || draft.blocks.length>=40}>+ Κουμπί</button>
                  </div>{/if}
                  {#if notice}<p role="status" class="cms-success">{notice}</p>{/if}
                  {#if problem}<p role="alert" class="cms-error">{problem}</p>{/if}
                  <div class="cms-action-row">
                    {#if canWrite}<button type="submit" class="admin-primary" disabled={saving || !dirty}>{saving?'Αποθήκευση…':'Αποθήκευση πρόχειρου'}</button>{/if}
                    {#if selected && canPublish}
                      <button type="button" class="admin-secondary" disabled={dirty || saving}
                        onclick={()=>void publication(!selected.published)}>
                        {selected.published?'Απόσυρση':'Έγκριση & δημοσίευση'}
                      </button>
                      {#if selected.published && dirty}<span class="admin-muted">Αποθηκεύστε και εγκρίνετε τις νέες αλλαγές χωριστά.</span>{/if}
                      {#if selected.published && !dirty}<button type="button" class="admin-secondary" disabled={saving}
                        onclick={()=>void publication(true)}>Επαναδημοσίευση πρόχειρου</button>{/if}
                    {/if}
                    {#if selected?.published && organization}
                      <a class="admin-secondary" href={websitePagePath(selected.slug,organization)} target="_blank" rel="noopener noreferrer">Δημόσια προβολή ↗</a>
                    {/if}
                  </div>
                </form>
              </section>
              {#if preview}
                <section class="admin-panel cms-preview" aria-label="Ζωντανή προεπισκόπηση">
                  <div class="cms-preview-bar"><strong>LIVE PREVIEW</strong><span>Ιδιωτική · δεν έχει δημοσιευτεί</span></div>
                  <p class="admin-eyebrow">{organization==='lefteria_fc'?'ΠΡΩΤΗ ΟΜΑΔΑ':'ΑΚΑΔΗΜΙΑ'}</p>
                  <h2>{draft.title || 'Τίτλος σελίδας'}</h2>
                  {#if draft.summary}<p>{draft.summary}</p>{/if}
                  <WebsitePageBlocks blocks={draft.blocks}/>
                </section>
              {/if}
            {:else}
              <section class="admin-panel cms-start">
                <h2>Διαχείριση περιεχομένου χωρίς plugins</h2>
                <p>Επιλέξτε μια σελίδα ή δημιουργήστε νέα. Χρησιμοποιήστε blocks, ταξινόμηση και ζωντανή προεπισκόπηση.</p>
                {#if canWrite}<button type="button" class="admin-primary" onclick={newPage}>Δημιουργία σελίδας</button>{/if}
                <p class="admin-muted">Οι υπάρχουσες βασικές διαδρομές παραμένουν ενεργές και διαχειρίζονται από τις αντίστοιχες ενότητες ώσπου να μεταφερθούν με έλεγχο συμβατότητας.</p>
              </section>
            {/if}
            {#if problem && !editing}<p class="cms-error" role="alert">{problem}</p>{/if}
          </div>
        </div>
      {:else}
        <section class="admin-panel"><p>Δεν έχετε οργανισμό ή δικαίωμα πρόσβασης στις σελίδες.</p></section>
      {/if}
    </div>
  </AdminFrame>
{:else}
  <AdminSessionStatus unavailable={$auth.status==='unavailable'} busy={$auth.busy} error={$auth.error}
    onRetry={()=>void auth.bootstrap()} onLogout={()=>void auth.logout()}/>
{/if}

<style>
.cms{color:#f3f4f1}
.cms-header{display:flex;justify-content:space-between;gap:18px;align-items:flex-start;flex-wrap:wrap;margin-bottom:20px}
.cms-header h1{margin:5px 0 12px;font:italic 900 clamp(34px,4vw,58px)/1 'Fira Sans Extra Condensed',sans-serif}
.cms-header .admin-muted{max-width:75ch}
.cms-header-actions,.cms-action-row,.cms-add-block{display:flex;gap:10px;flex-wrap:wrap;align-items:center}
.cms-header-actions a,.cms-action-row a{display:inline-flex;align-items:center;text-decoration:none}
.cms-organization{display:flex;flex-wrap:wrap;align-items:end;gap:18px;margin-bottom:18px}
.cms-organization label{min-width:min(300px,100%);max-width:400px}
.cms-grid{display:grid;grid-template-columns:minmax(230px,290px) minmax(0,1fr);gap:17px;align-items:start}
.cms-library{position:sticky;top:12px;padding:17px}
.cms-library-heading,.cms-editor-head,.cms-block-heading,.cms-block-tools,.cms-preview-bar{display:flex;align-items:center;justify-content:space-between;gap:12px}
.cms-library-heading h2,.cms-editor-head h2,.cms-start h2{margin:0;font:italic 850 27px/1.15 'Fira Sans Extra Condensed',sans-serif}
.cms-page-list{display:grid;gap:8px;margin:18px 0}
.cms-page-list button{text-align:start;display:grid;gap:6px;border:1px solid #534b38;background:#1a1d1f;color:#fff;padding:13px;cursor:pointer}
.cms-page-list button.cms-selected{border-color:#ffd36d;background:#312b1c}
.cms-page-list small{color:#c0c2c0}
.cms-workspace{display:grid;gap:17px;min-width:0}
.cms-editor,.cms-preview,.cms-start{padding:clamp(16px,2vw,26px)}
.cms-editor form{display:grid;gap:18px;margin-top:20px}
.cms label{display:grid;gap:7px;font-size:13px;font-weight:700;color:#eee}
.cms input:not([type=checkbox]),.cms textarea,.cms select{width:100%;border:1px solid #66645a;background:#111416;color:#fff;padding:10px;border-radius:3px;font:400 14px/1.5 'Fira Sans',sans-serif}
.cms textarea{resize:vertical}
.cms-field-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px}
.cms-seo{padding:14px;border:1px solid #484c4f}
.cms-seo summary{font-weight:750;cursor:pointer}
.cms-seo label{margin-top:12px}
.cms-block-heading h3{margin:0;font-size:20px}
.cms-block-list{display:grid;gap:14px}
.cms-block{padding:14px;border:1px solid #4f4b44;background:#171a1c;display:grid;gap:12px}
.cms-block-tools strong{font-size:13px}
.cms-block-tools>div{display:flex;gap:5px}
.cms-block-tools button,.cms-add-block button{min-height:34px;min-width:36px;border:1px solid #79715d;background:#262b2e;color:#f5e4c0;cursor:pointer;padding:6px 10px}
.cms-add-block{border-top:1px solid #4a4f4d;padding-top:16px}
.cms-preview-toggle{display:flex!important;align-items:center;gap:7px}
.cms-preview-toggle input{width:auto}
.cms-preview{border-top:3px solid #f9c454}
.cms-preview-bar{font-size:11px;color:#f9d282;letter-spacing:.07em;padding-bottom:16px;border-bottom:1px solid #4b4f4e}
.cms-preview>h2{font:italic 900 clamp(33px,4vw,54px)/1.1 'Fira Sans Extra Condensed',sans-serif}
.cms-preview :global(.website-blocks){margin-top:24px}
.cms-error{color:#ff9f9f}.cms-success{color:#a7e2b4}
.cms-start{display:grid;justify-items:start;gap:16px}
.cms button:disabled{opacity:.5;cursor:not-allowed}
.cms :is(button,a,input,textarea,select,summary):focus-visible{outline:2px solid #ffce63;outline-offset:3px}
@media(max-width:1000px){.cms-grid{grid-template-columns:1fr}.cms-library{position:static}.cms-page-list{grid-template-columns:repeat(auto-fit,minmax(180px,1fr))}}
@media(max-width:630px){.cms-field-grid{grid-template-columns:1fr}.cms-header-actions{width:100%}.cms-editor-head{align-items:start;flex-direction:column}}
</style>
