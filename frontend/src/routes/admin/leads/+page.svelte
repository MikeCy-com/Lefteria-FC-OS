<script lang="ts">
  import { getContext, untrack } from 'svelte';
  import { browser } from '$app/env';
  import { ADMIN_AUTH_CONTEXT, type AdminAuthController } from '../../../lib/admin/controller.ts';
  import { hasAdminPermission } from '../../../lib/admin/permissions.ts';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';

  type LeadStatus='new'|'contacted'|'trial_booked'|'registered'|'lost';
  type Lead={
    id:string;organization_id:'lefteria_fc_academy';contact_name:string;
    phone:string|null;email:string|null;player_age:number|null;
    interested_in:string|null;message:string|null;
    source:'website'|'partner_referral';partner_slug:string|null;
    status:LeadStatus;created_at:string;consent_marketing:boolean;
  };
  type Page={organization_id:'lefteria_fc_academy';offset:number;limit:number;total:number;leads:Lead[]};

  const auth=getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  const manager=$derived($auth.status==='authenticated'&&$auth.session &&
    ['manager','super_admin'].includes($auth.session.user.role) &&
    hasAdminPermission($auth.session.user.permissions,'marketing:read') &&
    $auth.session.user.organizations.some(org=>org.id==='lefteria_fc_academy'));
  const canWrite=$derived(!!manager&&$auth.session &&
    hasAdminPermission($auth.session.user.permissions,'marketing:write'));

  let statusFilter=$state<'all'|LeadStatus>('all');
  let offset=$state(0);
  const limit=25;
  let reload=$state(0);
  let loading=$state(false);
  let page=$state<Page|null>(null);
  let error=$state('');
  let notice=$state('');
  let savingId=$state<string|null>(null);

  const statuses:Record<LeadStatus,string>={
    new:'Νέο',contacted:'Έγινε επικοινωνία',
    trial_booked:'Δοκιμαστική προπόνηση (υφιστάμενο)',
    registered:'Εγγεγραμμένος (υφιστάμενο)',lost:'Δεν ενδιαφέρεται'
  };
  const short=(value:string|null,max=300)=>!value?'—':
    value.length>max?value.slice(0,max)+'…':value;
  const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  function validLead(value:unknown):value is Lead{
    if(!value||typeof value!=='object'||Array.isArray(value))return false;
    const x=value as Record<string,unknown>;
    return typeof x.id==='string'&&uuid.test(x.id) &&
      x.organization_id==='lefteria_fc_academy' &&
      typeof x.contact_name==='string'&&x.contact_name.length>0&&x.contact_name.length<=120 &&
      (x.phone===null||typeof x.phone==='string'&&x.phone.length<=32)&&
      (x.email===null||typeof x.email==='string'&&x.email.length<=254)&&
      (x.player_age===null||Number.isInteger(x.player_age)&&
        (x.player_age as number)>=4&&(x.player_age as number)<=99)&&
      (x.interested_in===null||typeof x.interested_in==='string'&&x.interested_in.length<=120)&&
      (x.message===null||typeof x.message==='string'&&x.message.length<=2000)&&
      (x.source==='website'||x.source==='partner_referral')&&
      (x.partner_slug===null||typeof x.partner_slug==='string'&&x.partner_slug.length<=64)&&
      Object.hasOwn(statuses,String(x.status))&&
      typeof x.created_at==='string'&&Number.isFinite(Date.parse(x.created_at))&&
      typeof x.consent_marketing==='boolean';
  }
  function validPage(value:unknown,filter:string,currentOffset:number):value is Page{
    if(!value||typeof value!=='object'||Array.isArray(value))return false;
    const row=value as Record<string,unknown>;
    return row.organization_id==='lefteria_fc_academy'&&
      row.offset===currentOffset&&row.limit===limit&&
      Number.isSafeInteger(row.total)&&(row.total as number)>=0&&(row.total as number)<=200000&&
      Array.isArray(row.leads)&&row.leads.length<=limit&&
      row.leads.every(item=>validLead(item) &&
        (filter==='all'||item.status===filter));
  }

  $effect(()=>{
    const user=$auth.session?.user.id;
    const hasAccess=manager;
    const filter=statusFilter;
    const pageOffset=offset;
    void reload;void user;
    let active=true;
    page=null;error='';loading=!!hasAccess;
    if(!browser||!hasAccess)return;
    untrack(()=>{void(async()=>{
      try{
        const query=new URLSearchParams({
          organization_id:'lefteria_fc_academy',offset:String(pageOffset),limit:String(limit)
        });
        if(filter!=='all')query.set('status',filter);
        const response=await fetch('/api/admin/crm/academy/leads/?'+query,{
          credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}
        });
        if(!active)return;
        if(response.status===401||response.status===403){
          void auth.bootstrap();
          throw new Error('Η συνεδρία ή τα δικαιώματα έχουν αλλάξει.');
        }
        if(!response.ok)throw new Error('Ο ιδιωτικός κατάλογος δεν είναι διαθέσιμος.');
        const result:unknown=await response.json();
        if(!validPage(result,filter,pageOffset))throw new Error('Μη επαληθευμένα στοιχεία CRM.');
        if(active)page=result;
      }catch(e){
        if(active){error=e instanceof Error?e.message:'Η φόρτωση απέτυχε.';page=null;}
      }finally{if(active)loading=false;}
    })();});
    return()=>{active=false;};
  });

  async function update(lead:Lead,next:'contacted'|'lost'){
    if(!canWrite||savingId||!browser||!page ||
      !(lead.status==='new' && (next==='contacted'||next==='lost')||
        lead.status==='contacted' && next==='lost'))return;
    if(next==='lost'&&!window.confirm('Επιβεβαιώστε ότι το αίτημα δεν θα συνεχιστεί. Η ενέργεια καταγράφεται.'))return;
    const actor=$auth.session?.user.id;
    savingId=lead.id;notice='';error='';
    try{
      const response=await fetch('/api/admin/crm/academy/leads/'+lead.id+
        '/status?organization_id=lefteria_fc_academy',{
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({expected_status:lead.status,next_status:next})
      });
      if($auth.session?.user.id!==actor||!canWrite)return;
      if(response.status===401||response.status===403){
        void auth.bootstrap();throw new Error('Τα δικαιώματα άλλαξαν.');
      }
      if(response.status===409){
        reload++;throw new Error('Το αίτημα άλλαξε. Φορτώστε τα τρέχοντα στοιχεία.');
      }
      if(!response.ok)throw new Error('Η ενημέρωση δεν επιβεβαιώθηκε.');
      const payload:unknown=await response.json();
      if(!payload||typeof payload!=='object'||Array.isArray(payload) ||
        !('id' in payload)||payload.id!==lead.id||
        !('status' in payload)||payload.status!==next||
        !('changed' in payload)||typeof payload.changed!=='boolean')
        throw new Error('Μη επαληθευμένη απάντηση ενημέρωσης.');
      notice=next==='contacted'?'Καταχωρίστηκε η επικοινωνία.':
        'Το αίτημα επισημάνθηκε χωρίς συνέχεια.';
      reload++;
    }catch(e){error=e instanceof Error?e.message:'Η καταχώριση απέτυχε.';}
    finally{savingId=null;}
  }

  const localDate=(value:string)=>new Intl.DateTimeFormat('el-CY',{
    timeZone:'Asia/Nicosia',dateStyle:'medium',timeStyle:'short'
  }).format(new Date(value));
</script>

{#if $auth.status==='authenticated' && $auth.session}
  <AdminFrame user={$auth.session.user} active="leads" busy={$auth.busy}
    onLogout={()=>void auth.logout()}>
    {#if manager}
      <div class="academy-crm" data-testid="admin-academy-crm">
        <p class="admin-eyebrow">CRM · ΛΕΥΤΕΡΙΑ FC ΑΚΑΔΗΜΙΑ</p>
        <h1>Ενδιαφερόμενοι & αιτήματα Ακαδημίας</h1>
        <p class="admin-muted">Ιδιωτικά αιτήματα από τη φόρμα της Ακαδημίας.
          Η αλλαγή κατάστασης δεν δημιουργεί αυτόματα παίκτη, κράτηση
          δοκιμαστικής προπόνησης ή μήνυμα SMS.</p>
        <section class="admin-panel crm-controls" aria-label="Φίλτρα αιτημάτων">
          <label for="crm-filter">Κατάσταση</label>
          <select id="crm-filter" value={statusFilter} onchange={e=>{
            statusFilter=e.currentTarget.value as typeof statusFilter;offset=0;
          }}>
            <option value="all">Όλες οι καταστάσεις</option>
            {#each Object.entries(statuses) as [value,label] (value)}
              <option {value}>{label}</option>
            {/each}
          </select>
          <button class="admin-secondary" type="button" onclick={()=>reload++} disabled={loading}>Ανανέωση</button>
        </section>
        {#if notice}<p role="status" class="crm-feedback">{notice}</p>{/if}
        {#if loading}<section class="admin-panel" role="status">Φόρτωση ασφαλούς λίστας…</section>
        {:else if error||!page}
          <section class="admin-panel" role="alert">
            <p>{error||'Ο κατάλογος CRM δεν είναι ακόμη διαθέσιμος.'}</p>
            <button class="admin-secondary" onclick={()=>reload++} type="button">Δοκιμή ξανά</button>
          </section>
        {:else}
          <div class="crm-summary"><strong>{page.total}</strong> αιτήματα
            {#if statusFilter!=='all'}· {statuses[statusFilter]}{/if}</div>
          {#if page.leads.length===0}
            <section class="admin-panel">Δεν υπάρχουν επαληθευμένα αιτήματα σε αυτή την κατάσταση.</section>
          {:else}
            <ul class="crm-list">
              {#each page.leads as lead (lead.id)}
                <li class="admin-panel" data-testid="crm-lead-row">
                  <header class="crm-row-head">
                    <div><strong>{lead.contact_name}</strong>
                      <small>{localDate(lead.created_at)} · {lead.source==='website'?'Ιστοσελίδα':'Συνεργάτης'}</small></div>
                    <span class="crm-state">{statuses[lead.status]}</span>
                  </header>
                  <div class="crm-contact">
                    {#if lead.phone}<span>Τηλέφωνο: {lead.phone}</span>{/if}
                    {#if lead.email}<span>Email: {lead.email}</span>{/if}
                    {#if lead.player_age!==null}<span>Ηλικία ενδιαφερόμενου αθλητή: {lead.player_age}</span>{/if}
                    {#if lead.interested_in}<span>Ενδιαφέρον: {short(lead.interested_in,120)}</span>{/if}
                    {#if lead.partner_slug}<span>Συνεργάτης: {lead.partner_slug}</span>{/if}
                  </div>
                  {#if lead.message}
                    <p class="crm-message">{short(lead.message,2000)}</p>
                  {/if}
                  <p class="crm-consent">Συγκατάθεση ενημερώσεων: {lead.consent_marketing?'Ναι':'Όχι'}.
                    Η συγκατάθεση επικοινωνίας δεν σημαίνει έγκριση για μαζικό marketing.</p>
                  {#if canWrite && (lead.status==='new'||lead.status==='contacted')}
                    <div class="crm-actions">
                      {#if lead.status==='new'}
                        <button type="button" disabled={savingId!==null}
                          onclick={()=>void update(lead,'contacted')}
                          data-testid="crm-mark-contacted">Καταχώριση επικοινωνίας</button>
                      {/if}
                      <button type="button" class="admin-secondary" disabled={savingId!==null}
                        onclick={()=>void update(lead,'lost')}
                        data-testid="crm-mark-lost">Δεν ενδιαφέρεται</button>
                    </div>
                  {/if}
                </li>
              {/each}
            </ul>
          {/if}
          <nav class="crm-pager" aria-label="Σελίδες CRM">
            <button type="button" class="admin-secondary" disabled={offset===0}
              onclick={()=>offset=Math.max(0,offset-limit)}>Προηγούμενη</button>
            <span>{offset+1}–{Math.min(offset+page.leads.length,page.total)} / {page.total}</span>
            <button type="button" class="admin-secondary"
              disabled={offset+limit>=page.total||offset+limit>20000}
              onclick={()=>offset+=limit}>Επόμενη</button>
          </nav>
        {/if}
      </div>
    {:else}
      <section class="admin-panel" data-testid="admin-academy-crm-denied">
        <h1>Η πρόσβαση στα ιδιωτικά αιτήματα της Ακαδημίας δεν επιτρέπεται.</h1>
        <a href="/admin">Επιστροφή στο κέντρο διαχείρισης</a>
      </section>
    {/if}
  </AdminFrame>
{:else}
  <AdminSessionStatus unavailable={$auth.status==='unavailable'} busy={$auth.busy}
    error={$auth.error} onRetry={()=>void auth.bootstrap()}
    onLogout={()=>void auth.logout()}/>
{/if}

<style>
  .academy-crm{max-width:1120px}.academy-crm h1{margin:0 0 10px}
  .academy-crm>.admin-muted{max-width:83ch;margin:0 0 20px}
  .crm-controls{display:flex;align-items:center;flex-wrap:wrap;gap:12px;margin-bottom:16px}
  .crm-controls label{font-size:13px}.crm-controls select{min-width:min(310px,100%);max-width:100%}
  .crm-summary{margin:14px 0;color:#c3c7ca}.crm-summary strong{color:#ffca46;font-size:22px}
  .crm-list{list-style:none;margin:0;padding:0;display:grid;gap:13px}
  .crm-list li{min-width:0}.crm-row-head{display:flex;justify-content:space-between;align-items:flex-start;gap:15px}
  .crm-row-head strong{font-size:17px}.crm-row-head small{display:block;color:#a8acb0;margin-top:6px}
  .crm-state{border:1px solid #7e692d;border-radius:8px;color:#ffca46;padding:6px 9px;font-size:12px}
  .crm-contact{display:flex;gap:8px 16px;flex-wrap:wrap;margin-top:16px;font-size:13px;overflow-wrap:anywhere}
  .crm-message{background:#16191a;border-left:3px solid #ffca46;padding:12px;
    white-space:pre-wrap;overflow-wrap:anywhere;line-height:1.6}
  .crm-consent{font-size:12px;color:#b8bdc0}
  .crm-actions,.crm-pager{display:flex;flex-wrap:wrap;gap:10px;align-items:center;margin-top:14px}
  .crm-pager{justify-content:center}.crm-feedback{color:#c7eac7}
  .crm-actions button{border:1px solid #b58b31;border-radius:9px;padding:10px 14px;cursor:pointer}
  .crm-actions button:disabled{opacity:.5;cursor:not-allowed}
  @media(max-width:530px){.crm-row-head{flex-direction:column}.crm-state{align-self:flex-start}}
</style>
