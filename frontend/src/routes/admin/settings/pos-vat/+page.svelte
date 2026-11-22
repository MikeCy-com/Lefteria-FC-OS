<script lang="ts">
  import {getContext} from 'svelte';
  import {browser} from '$app/env';
  import {goto} from '$app/navigation';
  import AdminFrame from '../../../../lib/admin/AdminFrame.svelte';
  import {ADMIN_AUTH_CONTEXT,type AdminAuthController} from '../../../../lib/admin/controller.ts';

  type Owner='lefteria_fc'|'lefteria_fc_academy';
  type CatalogProduct={id:string;name:string;variants:{id:string;name:string}[]};
  type Proposal={id:string;product_id:string;variant_id:string;
    treatment_code:string;effective_from:string;authority_reference:string;
    justification:string;requested_at:string;status:'pending'};
  type Response={organization_id:Owner;checkout_enabled:false;
    approval_available:false;proposals:Proposal[]};
  type VatReview={proposal_id:string;approved:boolean;review_note:string;
    treatment_revision:number|null;reviewed_at:string};
  const auth=getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  const rates=[
    {value:'standard_19',label:'19% · κανονικός'},
    {value:'reduced_9',label:'9% · μειωμένος'},
    {value:'reduced_5',label:'5% · μειωμένος'},
    {value:'reduced_3',label:'3% · μειωμένος'},
    {value:'zero_0',label:'0% · μηδενικός συντελεστής'},
    {value:'exempt',label:'Απαλλασσόμενο · χωρίς ΦΠΑ'}
  ];
  const organizations=$derived($auth.session?.user.organizations.filter(
    o=>o.id==='lefteria_fc'||o.id==='lefteria_fc_academy')??[]);
  const authorized=$derived(['super_admin','manager','accountant'].includes($auth.session?.user.role??''));
  const canReview=$derived($auth.session?.user.role==='super_admin'&&reviewsAvailable);
  let owner=$state<Owner|null>(null),response=$state<Response|null>(null);
  let products=$state<CatalogProduct[]>([]);
  let loading=$state(false),saving=$state(false),error=$state(''),notice=$state('');
  let reviews=$state<VatReview[]>([]),reviewsAvailable=$state(false);
  let reviewTarget=$state<string|null>(null),reviewNote=$state(''),reviewSaving=$state(false);
  let productId=$state(''),variantId=$state(''),code=$state('standard_19');
  let effectiveFrom=$state(''),reference=$state(''),reason=$state('');
  let requestId=$state(''),refresh=$state(0);
  const activeProduct=$derived(products.find(p=>p.id===productId));
  const canSubmit=$derived(!!owner&&authorized&&
    /^[A-Za-z0-9_-]{1,80}$/.test(productId)&&
    /^[A-Za-z0-9_-]{1,60}$/.test(variantId)&&
    rates.some(r=>r.value===code)&&/^20\d\d-\d\d-\d\d$/.test(effectiveFrom)&&
    reference.trim()===reference&&reference.length>=10&&reference.length<=500&&
    reason.trim()===reason&&reason.length>=10&&reason.length<=500);
  function resetRequest(){requestId='';notice='';}
  function verified(raw:unknown,expected:Owner):raw is Response{
    if(!raw||typeof raw!=='object'||Array.isArray(raw))return false;
    const x=raw as Record<string,unknown>;
    return x.organization_id===expected&&x.checkout_enabled===false&&
      x.approval_available===false&&Array.isArray(x.proposals)&&
      x.proposals.length<=100&&x.proposals.every((p:unknown)=>{
        if(!p||typeof p!=='object'||Array.isArray(p))return false;
        const q=p as Record<string,unknown>;
        return typeof q.id==='string'&&typeof q.product_id==='string'&&
          typeof q.variant_id==='string'&&typeof q.treatment_code==='string'&&
          rates.some(r=>r.value===q.treatment_code)&&
          typeof q.effective_from==='string'&&
          typeof q.justification==='string'&&
          typeof q.authority_reference==='string'&&
          typeof q.requested_at==='string'&&q.status==='pending';
      });
  }
  $effect(()=>{
    if(browser&&$auth.status==='anonymous')void goto('/admin/login',{replace:true});
  });
  $effect(()=>{
    const list=organizations;
    if(owner&&!list.some(o=>o.id===owner))owner=null;
    if(!owner&&list.length===1)owner=list[0].id as Owner;
  });
  $effect(()=>{
    const current=owner;void refresh;
    response=null;loading=false;error='';requestId='';
    if(!browser||!authorized||!current)return;
    const controller=new AbortController();
    loading=true;
    void(async()=>{
      try{
        const result=await fetch('/api/admin/finance/pos-vat?'+
          new URLSearchParams({organization_id:current}),{
            credentials:'same-origin',cache:'no-store',signal:controller.signal,
            headers:{Accept:'application/json'}
          });
        if(controller.signal.aborted)return;
        if(result.status===401){void auth.bootstrap();return;}
        if(result.status===403){void auth.bootstrap();return;}
        if(!result.ok)throw Error(result.status===503
          ?'Απαιτείται εξουσιοδοτημένη ενεργοποίηση των δικαιωμάτων διαχείρισης ΦΠΑ.'
          :'Δεν είναι διαθέσιμες οι προτάσεις ΦΠΑ.');
        const data:unknown=await result.json();
        if(!verified(data,current))throw Error('Μη έγκυρα δεδομένα φορολογικών προτάσεων.');
        response=data;
      }catch(e){
        if(!controller.signal.aborted)error=e instanceof Error?e.message:'Σφάλμα σύνδεσης.';
      }finally{if(!controller.signal.aborted)loading=false;}
    })();
    return()=>controller.abort();
  });
  $effect(()=>{
    const current=owner,role=$auth.session?.user.role;
    products=[];
    if(!browser||!current||!['super_admin','manager'].includes(role??''))return;
    const controller=new AbortController();
    void(async()=>{
      try{
        const result=await fetch('/api/admin/pos/catalog?'+
          new URLSearchParams({organization_id:current}),{
            credentials:'same-origin',cache:'no-store',signal:controller.signal
          });
        if(!result.ok||controller.signal.aborted)return;
        const raw:unknown=await result.json();
        if(!raw||typeof raw!=='object'||Array.isArray(raw))return;
        const x=raw as Record<string,unknown>;
        if(x.organization_id!==current||!Array.isArray(x.products))return;
        const list=x.products.filter((p:unknown)=>{
          if(!p||typeof p!=='object'||Array.isArray(p))return false;
          const row=p as Record<string,unknown>;
          return typeof row.id==='string'&&typeof row.name==='string'&&
            Array.isArray(row.variants)&&row.variants.every((v:unknown)=>{
              if(!v||typeof v!=='object'||Array.isArray(v))return false;
              const item=v as Record<string,unknown>;
              return typeof item.id==='string'&&typeof item.name==='string';
            });
        }) as CatalogProduct[];
        if(!controller.signal.aborted&&owner===current)products=list.slice(0,250);
      }catch{/* Private catalog is optional; scoped product IDs remain editable. */}
    })();
    return()=>controller.abort();
  });
  $effect(()=>{
    const current=owner;void refresh;
    reviews=[];reviewsAvailable=false;reviewTarget=null;reviewNote='';
    if(!browser||!authorized||!current)return;
    const controller=new AbortController();
    void(async()=>{
      try{
        const result=await fetch('/api/admin/finance/pos-vat/reviews?'+
          new URLSearchParams({organization_id:current}),{
            credentials:'same-origin',cache:'no-store',signal:controller.signal
          });
        if(!result.ok||controller.signal.aborted)return;
        const raw:unknown=await result.json();
        if(!raw||typeof raw!=='object'||Array.isArray(raw))return;
        const x=raw as Record<string,unknown>;
        if(x.organization_id!==current||x.checkout_enabled!==false||
          !Array.isArray(x.reviews)||x.reviews.length>100)return;
        const valid=x.reviews.every((item:unknown)=>{
          if(!item||typeof item!=='object'||Array.isArray(item))return false;
          const r=item as Record<string,unknown>;
          return typeof r.proposal_id==='string'&&typeof r.approved==='boolean'&&
            typeof r.review_note==='string'&&typeof r.reviewed_at==='string'&&
            (r.treatment_revision===null||Number.isSafeInteger(r.treatment_revision));
        });
        if(valid&&!controller.signal.aborted&&owner===current){
          reviews=x.reviews as VatReview[];
          reviewsAvailable=true;
        }
      }catch{/* Decision review remains disabled if evidence cannot be verified. */}
    })();
    return()=>controller.abort();
  });
  async function submitReview(approve:boolean){
    if(!owner||!canReview||!reviewTarget||reviewSaving||
      reviewNote.trim()!==reviewNote||reviewNote.length<10||reviewNote.length>500)return;
    const current=owner,target=reviewTarget;
    reviewSaving=true;error='';notice='';
    try{
      const result=await fetch('/api/admin/finance/pos-vat/proposals/'+
        encodeURIComponent(target)+'/review?'+
        new URLSearchParams({organization_id:current}),{
          method:'POST',credentials:'same-origin',
          headers:{'Content-Type':'application/json',Accept:'application/json'},
          body:JSON.stringify({approve,review_note:reviewNote})
        });
      if(owner!==current)return;
      if(result.status===401){void auth.bootstrap();return;}
      if(result.status===403)throw Error('Απαιτείται ανεξάρτητος Super Admin. Η αυτοέγκριση δεν επιτρέπεται.');
      if(!result.ok)throw Error(result.status===409
        ?'Έχει καταχωριστεί άλλη απόφαση για αυτό το αίτημα.'
        :result.status===422?'Ελέγξτε την τεκμηρίωση.':
          'Ο ανεξάρτητος έλεγχος δεν ολοκληρώθηκε.');
      const payload:unknown=await result.json();
      if(!payload||typeof payload!=='object'||Array.isArray(payload)||
        (payload as Record<string,unknown>).proposal_id!==target||
        (payload as Record<string,unknown>).approved!==approve||
        (payload as Record<string,unknown>).checkout_enabled!==false)
        throw Error('Η απόφαση δεν επαληθεύτηκε.');
      reviewTarget=null;reviewNote='';
      notice='Η απόφαση καταγράφηκε. Η φορολογική ενεργοποίηση και οι πληρωμές παραμένουν κλειδωμένες.';
      refresh++;
    }catch(e){error=e instanceof Error?e.message:'Σφάλμα ελέγχου.';}
    finally{reviewSaving=false;}
  }
  async function submit(){
    if(!canSubmit||!owner||saving||!response)return;
    const current=owner;
    saving=true;error='';notice='';
    if(!requestId)requestId=crypto.randomUUID();
    const payload={request_id:requestId,product_id:productId,variant_id:variantId,
      treatment_code:code,effective_from:effectiveFrom,
      authority_reference:reference,justification:reason};
    try{
      const res=await fetch('/api/admin/finance/pos-vat/proposals?'+
        new URLSearchParams({organization_id:current}),{
          method:'POST',credentials:'same-origin',
          headers:{'Content-Type':'application/json',Accept:'application/json'},
          body:JSON.stringify(payload)
        });
      if(owner!==current)return;
      if(res.status===401||res.status===403){void auth.bootstrap();return;}
      if(!res.ok)throw Error(res.status===409?'Ασυμφωνία κωδικού αιτήματος.':
        res.status===422?'Ελέγξτε το προϊόν, την παραλλαγή και τη φορολογική τεκμηρίωση.':
        'Δεν αποθηκεύτηκε η πρόταση. Επιβεβαιώστε δικαιώματα και σύνδεση.');
      const data:unknown=await res.json();
      if(!data||typeof data!=='object'||(data as Record<string,unknown>).status!=='pending'||
        (data as Record<string,unknown>).checkout_enabled!==false)
        throw Error('Η αποθήκευση δεν επαληθεύτηκε.');
      requestId='';notice='Η πρόταση καταχωρίστηκε για έλεγχο. Δεν ενεργοποιήθηκε ΦΠΑ ή έκδοση αποδείξεων.';
      refresh++;
    }catch(e){error=e instanceof Error?e.message:'Σφάλμα αποθήκευσης.';}
    finally{saving=false;}
  }
</script>

<svelte:head><title>ΦΠΑ προϊόντων · Οικονομικές ρυθμίσεις | Λευτεριά FC OS</title></svelte:head>
{#if $auth.status==='authenticated'&&$auth.session}
  <AdminFrame user={$auth.session.user} active="settings" busy={$auth.busy}
    onLogout={()=>void auth.logout()}>
    <main class="pos-vat-settings" data-testid="admin-pos-vat-settings">
      <p class="admin-eyebrow">ΟΙΚΟΝΟΜΙΚΑ · ΡΥΘΜΙΣΕΙΣ</p>
      <h1>ΦΠΑ ανά προϊόν / παραλλαγή</h1>
      <p class="admin-muted">Προετοιμασία φορολογικής ταξινόμησης για κάθε προϊόν. Διαχωρίζονται 0% και απαλλαγή. Οι προτάσεις δεν αλλάζουν τιμές, πληρωμές, αποδείξεις ή εγκεκριμένες φορολογικές πολιτικές.</p>
      {#if !authorized||!organizations.length}
        <p role="alert">Απαιτείται εξουσιοδότηση οικονομικών στον οργανισμό.</p>
      {:else}
        <section class="admin-panel">
          <label for="vat-org">Οργανισμός</label>
          <select id="vat-org" value={owner??''} disabled={saving}
            onchange={e=>{owner=e.currentTarget.value as Owner;notice='';error='';}}> 
            {#if organizations.length!==1}<option value="">Επιλέξτε οργανισμό</option>{/if}
            {#each organizations as org}<option value={org.id}>{org.name}</option>{/each}
          </select>
        </section>
        <section class="admin-panel" data-testid="vat-policy-guard">
          <h2>Πολιτική έκδοσης αποδείξεων</h2>
          <p>Έκδοση αποδείξεων: <strong>Μη εγκεκριμένη</strong>.
          Φορολογική έγκριση: <strong>Εκκρεμεί</strong>.
          Payabl live: <strong>Ανενεργό</strong>.</p>
          <p class="admin-muted">Ο λογιστής επιβεβαιώνει την κατάταξη κάθε είδους και το αποδεικτικό της. Η καταχώριση πρότασης δεν αποτελεί φορολογική έγκριση.</p>
        </section>
        {#if loading}<p role="status">Ανάκτηση φορολογικών προτάσεων…</p>{/if}
        {#if error}<p role="alert">{error}</p>{/if}
        {#if notice}<p role="status">{notice}</p>{/if}
        {#if response && owner}
          <section class="admin-panel">
            <h2>Νέα πρόταση συντελεστή ΦΠΑ</h2>
            <form onsubmit={e=>{e.preventDefault();void submit();}} class="vat-form">
              <label>Κωδικός προϊόντος
                <input required maxlength="80" bind:value={productId}
                  list="vat-product-options" oninput={()=>{variantId='';resetRequest();}}
                  placeholder="product_id"/>
                <datalist id="vat-product-options">
                  {#each products as product}<option value={product.id}>{product.name}</option>{/each}
                </datalist>
              </label>
              <label>Κωδικός παραλλαγής
                <input required maxlength="60" bind:value={variantId}
                  list="vat-variant-options" oninput={resetRequest} placeholder="variant_id"/>
                <datalist id="vat-variant-options">
                  {#each activeProduct?.variants??[] as variant}
                    <option value={variant.id}>{variant.name}</option>
                  {/each}
                </datalist>
              </label>
              <label>Φορολογική κατηγορία
                <select bind:value={code} onchange={resetRequest}>
                  {#each rates as rate}<option value={rate.value}>{rate.label}</option>{/each}
                </select>
              </label>
              <label>Ισχύς από
                <input type="date" required bind:value={effectiveFrom} onchange={resetRequest}/>
              </label>
              <label class="wide">Νομική / λογιστική τεκμηρίωση
                <textarea required maxlength="500" minlength="10" rows="2"
                  bind:value={reference} oninput={resetRequest}/>
              </label>
              <label class="wide">Αιτιολόγηση για αυτό το προϊόν
                <textarea required maxlength="500" minlength="10" rows="2"
                  bind:value={reason} oninput={resetRequest}/>
              </label>
              <button type="submit" class="admin-primary" disabled={!canSubmit||saving}>
                {saving?'Αποθήκευση…':'Αποθήκευση πρότασης για έλεγχο'}
              </button>
            </form>
          </section>
          <section class="admin-panel">
            <h2>Καταχωρισμένες προτάσεις</h2>
            {#if !response.proposals.length}<p>Δεν υπάρχουν προτάσεις για τον οργανισμό.</p>
            {:else}
              <div class="vat-list">
                {#each response.proposals as item (item.id)}
                  {@const reviewed=reviews.find(r=>r.proposal_id===item.id)}
                  <article>
                    <strong>{item.product_id} · {item.variant_id}</strong>
                    <p>{rates.find(r=>r.value===item.treatment_code)?.label} · από {item.effective_from}</p>
                    <p>{item.authority_reference}</p>
                    {#if reviewed}
                      <small>{reviewed.approved?'Εγκρίθηκε ως ανενεργή φορολογική ταξινόμηση':'Απορρίφθηκε'} · {reviewed.reviewed_at}</small>
                    {:else}
                      <small>Εκκρεμεί ανεξάρτητος έλεγχος · {item.requested_at}</small>
                      {#if canReview}
                        <button type="button" class="admin-secondary"
                          onclick={()=>{reviewTarget=item.id;reviewNote='';}}
                          data-testid="vat-review-open">Ανεξάρτητος έλεγχος</button>
                      {/if}
                    {/if}
                  </article>
                {/each}
              </div>
            {/if}
          </section>
          {#if canReview&&reviewTarget}
            <section class="admin-panel" data-testid="vat-independent-review">
              <h2>Ανεξάρτητη απόφαση Super Admin</h2>
              <p class="admin-muted">Δεν επιτρέπεται αυτοέγκριση. Ελέγξτε πραγματική νομοθεσία και τεκμηρίωση του προϊόντος. Η έγκριση αποθηκεύει ανενεργή αναθεώρηση ΦΠΑ και δεν επιτρέπει φορολογικές αποδείξεις ή Payabl.</p>
              <label>Αιτιολόγηση απόφασης
                <textarea bind:value={reviewNote} rows="3" minlength="10" maxlength="500"/>
              </label>
              <div class="vat-review-actions">
                <button type="button" disabled={reviewSaving||reviewNote.trim().length<10}
                  onclick={()=>void submitReview(true)}>Έγκριση ταξινόμησης · ανενεργή</button>
                <button type="button" disabled={reviewSaving||reviewNote.trim().length<10}
                  onclick={()=>void submitReview(false)}>Απόρριψη</button>
                <button type="button" disabled={reviewSaving}
                  onclick={()=>{reviewTarget=null;reviewNote='';}}>Ακύρωση</button>
              </div>
            </section>
          {/if}
        {/if}
      {/if}
    </main>
  </AdminFrame>
{/if}
<style>
.pos-vat-settings{display:grid;gap:16px;max-width:1100px;margin:auto}
.pos-vat-settings h1{font-size:clamp(25px,3vw,36px);line-height:1.2}
.pos-vat-settings .vat-form{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:14px}
.pos-vat-settings label{display:grid;gap:6px;font-size:13px}
.pos-vat-settings input,.pos-vat-settings select,.pos-vat-settings textarea{width:100%;min-width:0;border:1px solid #777;background:#101010;color:#fff;padding:10px;border-radius:6px}
.pos-vat-settings .wide{grid-column:1/-1}
.pos-vat-settings .vat-review-actions{display:flex;gap:10px;flex-wrap:wrap}
.pos-vat-settings .vat-review-actions button{border:1px solid #f6bb38;background:#171717;color:#f6bb38;padding:9px;border-radius:5px}
.pos-vat-settings .vat-list{display:grid;gap:10px}
.pos-vat-settings article{border:1px solid #555;padding:12px;border-radius:8px}
.pos-vat-settings article p{margin:5px 0}
.pos-vat-settings small{color:#bdbdbd}
@media(max-width:650px){.pos-vat-settings .vat-form{grid-template-columns:1fr}}
</style>
