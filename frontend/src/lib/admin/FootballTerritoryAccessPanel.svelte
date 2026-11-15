<script lang="ts">
  import {browser} from '$app/env';

  type Owner='lefteria_fc_academy'|'lefteria_fc';
  type Kind='city'|'location'|'team';
  type Scope={id:string;team_id:string|null;team_name:string|null};
  type City={id:string;name:string};
  type Location=City & {city_id:string};
  type Account={id:string;display_name:string;role:string};
  type Grant={admin_account_id:string;scope_id:string;role:string;kind:Kind};
  type RawGrant=Omit<Grant,'kind'>;
  type Response={organization_id:Owner;accounts:Account[];
    city_grants:RawGrant[];location_grants:RawGrant[];
    team_grants:RawGrant[]};

  let {organization,actorId,cities,locations,scopes,revision=0}:{
    organization:Owner;actorId:string;cities:City[];
    locations:Location[];scopes:Scope[];revision?:number;
  }=$props();

  let access=$state<Response|null>(null);
  let loading=$state(false),busy=$state(false);
  let error=$state(''),notice=$state(''),refresh=$state(0);
  let selectedAccount=$state(''),kind=$state<Kind>('city');
  let selectedScope=$state(''),selectedRole=$state('');
  const choices=$derived(kind==='city'
    ?cities.map(c=>({id:c.id,label:c.name}))
    :kind==='location'
      ?locations.map(l=>({id:l.id,label:
        (cities.find(c=>c.id===l.city_id)?.name??'—')+' / '+l.name}))
      :scopes.filter(s=>s.team_id!==null)
        .map(s=>({id:s.id,label:s.team_name??s.team_id??'—'})));
  const actor=$derived(access?.accounts.find(a=>a.id===selectedAccount));
  const roles=$derived(!actor?[]
    :kind==='city'
      ?actor.role==='manager'?['city_manager']:[]
      :actor.role==='manager'?[kind==='team'?'team_manager':'location_manager']
      :actor.role==='coach'?['coach']
      :['editor','matchday_editor'].includes(actor.role)?['staff']:[]);
  const grantItems=$derived<Grant[]>(!access?[]
    :organization==='lefteria_fc_academy'
      ?[...access.city_grants.map(x=>({...x,kind:'city' as Kind})),
        ...access.location_grants.map(x=>({...x,kind:'location' as Kind}))]
      :access.team_grants.map(x=>({...x,kind:'team' as Kind})));

  $effect(()=>{
    const owner=organization,identity=actorId,rev=revision,reload=refresh;
    void rev;void reload;
    access=null;loading=false;error='';
    if(!browser||!identity)return;
    const controller=new AbortController();
    loading=true;
    void (async()=>{
      try{
        const response=await fetch('/api/admin/football/territories/access?organization_id='+
          encodeURIComponent(owner),{credentials:'same-origin',cache:'no-store',
          headers:{Accept:'application/json'},signal:controller.signal});
        if(!response.ok)throw new Error('Δεν είναι διαθέσιμα τα δικαιώματα τοποθεσιών.');
        const value:unknown=await response.json();
        if(!value || typeof value!=='object' || Array.isArray(value) ||
          (value as Record<string,unknown>).organization_id!==owner)
          throw new Error('Μη επαληθευμένη απάντηση δικαιωμάτων.');
        const result=value as Response;
        if(!Array.isArray(result.accounts) || !Array.isArray(result.city_grants)
          ||!Array.isArray(result.location_grants) ||!Array.isArray(result.team_grants)
          ||[result.accounts,result.city_grants,result.location_grants,
              result.team_grants].some(list=>list.length>3000)
          ||!result.accounts.every(a=>typeof a.id==='string' &&
              typeof a.display_name==='string' && typeof a.role==='string')
          ||![result.city_grants,result.location_grants,result.team_grants]
            .every(list=>list.every(g=>typeof g.admin_account_id==='string'
              &&typeof g.scope_id==='string'&&typeof g.role==='string')))
          throw new Error('Μη έγκυρα δεδομένα δικαιωμάτων.');
        if(!controller.signal.aborted&&actorId===identity&&organization===owner)
          access=result;
      }catch(e){if(!controller.signal.aborted)error=
        e instanceof Error?e.message:'Αποτυχία δικαιωμάτων.';}
      finally{if(!controller.signal.aborted)loading=false;}
    })();
    return ()=>controller.abort();
  });
  $effect(()=>{
    if(organization==='lefteria_fc')kind='team';
    else if(kind==='team')kind='city';
  });
  $effect(()=>{
    if(!access?.accounts.some(a=>a.id===selectedAccount))
      selectedAccount=access?.accounts[0]?.id??'';
  });
  $effect(()=>{
    if(!choices.some(choice=>choice.id===selectedScope))
      selectedScope=choices[0]?.id??'';
  });
  $effect(()=>{
    if(!roles.includes(selectedRole))selectedRole=roles[0]??'';
  });
  const scopeName=(g:Grant):string=>
    g.kind==='city'?(cities.find(c=>c.id===g.scope_id)?.name??'Πόλη')
    :g.kind==='location'?(locations.find(l=>l.id===g.scope_id)?.name??'Τοποθεσία')
    :(scopes.find(s=>s.id===g.scope_id)?.team_name??'Ομάδα');
  const accountName=(id:string):string=>
    access?.accounts.find(a=>a.id===id)?.display_name??'Μη ενεργός λογαριασμός';

  async function apply(input:Grant,enabled:boolean){
    if(!browser||busy||!access)return;
    if(!enabled&&!window.confirm('Ανάκληση πρόσβασης για '+accountName(input.admin_account_id)+';'))return;
    const owner=organization,identity=actorId;
    busy=true;error='';notice='';
    try{
      const response=await fetch('/api/admin/football/territories/access?organization_id='+
        encodeURIComponent(owner),{
        method:'PUT',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({
          admin_account_id:input.admin_account_id,
          scope_id:input.scope_id,kind:input.kind,role:input.role,enabled
        })
      });
      if(owner!==organization||identity!==actorId)return;
      if(!response.ok)throw new Error(response.status===403
        ?'Η πρόσβαση έχει ανακληθεί ή ο λογαριασμός/τοποθεσία δεν είναι εξουσιοδοτημένα.'
        :'Η αλλαγή πρόσβασης δεν αποθηκεύτηκε.');
      const result:unknown=await response.json();
      if(!result||typeof result!=='object'||Array.isArray(result)||
        (result as Record<string,unknown>).organization_id!==owner||
        (result as Record<string,unknown>).admin_account_id!==input.admin_account_id||
        (result as Record<string,unknown>).scope_id!==input.scope_id||
        (result as Record<string,unknown>).kind!==input.kind||
        (result as Record<string,unknown>).role!==input.role||
        (result as Record<string,unknown>).enabled!==enabled)
        throw new Error('Η διακομιστική επιβεβαίωση δεν συμφωνεί.');
      notice=enabled?'Η πρόσβαση εκχωρήθηκε.':'Η πρόσβαση ανακλήθηκε.';
      refresh++;
    }catch(e){error=e instanceof Error?e.message:'Αποτυχία αλλαγής.';}
    finally{busy=false;}
  }
</script>

<section class="delegation" aria-labelledby="delegation-heading"
  data-testid="football-territory-delegated-access">
  <h2 id="delegation-heading">Δικαιώματα πόλεων, τοποθεσιών και προσωπικού</h2>
  <p>Μόνο Super Admin. Κάθε άδεια συνδέεται με επιβεβαιωμένο λογαριασμό,
    τη σωστή πόλη/τοποθεσία ή ομάδα και ελέγχεται σε κάθε ενέργεια.</p>
  {#if loading}
    <p role="status">Φόρτωση λογαριασμών και δικαιωμάτων…</p>
  {:else if access}
    {#if access.accounts.length===0}
      <p>Δεν υπάρχουν ενεργοί λογαριασμοί με πρόσβαση στον οργανισμό.</p>
    {:else}
      <form class="access-form" onsubmit={e=>{
        e.preventDefault();
        if(selectedAccount&&selectedScope&&selectedRole) void apply({
          admin_account_id:selectedAccount,scope_id:selectedScope,
          kind,role:selectedRole
        },true);
      }}>
        <h3>Εκχώρηση πρόσβασης</h3>
        <label>Λογαριασμός
          <select bind:value={selectedAccount}>
            {#each access.accounts as a (a.id)}
              <option value={a.id}>{a.display_name} · {a.role}</option>
            {/each}
          </select>
        </label>
        <label>Επίπεδο
          <select bind:value={kind}>
            {#if organization==='lefteria_fc_academy'}
              <option value="city">Πόλη · διαχειριστής πόλης</option>
              <option value="location">Τοποθεσία · manager / coach / staff</option>
            {:else}
              <option value="team">Πρώτη ομάδα · manager / coach / staff</option>
            {/if}
          </select>
        </label>
        <label>Πόλη, τοποθεσία ή ομάδα
          <select bind:value={selectedScope}>
            {#each choices as c (c.id)}<option value={c.id}>{c.label}</option>{/each}
          </select>
        </label>
        <label>Ρόλος
          <select bind:value={selectedRole}>
            {#each roles as role (role)}<option value={role}>{role}</option>{/each}
          </select>
        </label>
        {#if roles.length===0}
          <p>Αυτός ο τύπος λογαριασμού δεν επιτρέπεται για το επιλεγμένο επίπεδο.</p>
        {/if}
        <button type="submit" disabled={busy||!selectedAccount||!selectedScope||!selectedRole}>
          Εκχώρηση πρόσβασης
        </button>
      </form>
    {/if}
    <h3>Υπάρχουσες άδειες</h3>
    {#if grantItems.length===0}
      <p>Δεν έχουν καταχωριστεί αναθέσεις.</p>
    {:else}
      <ul class="grant-list">
        {#each grantItems as grant (grant.kind + '-' + grant.scope_id + '-' + grant.admin_account_id + '-' + grant.role)}
          <li>
            <span><strong>{accountName(grant.admin_account_id)}</strong>
              <small>{scopeName(grant)} · {grant.role}</small></span>
            <button type="button" disabled={busy}
              onclick={()=>void apply(grant,false)}
              aria-label={'Ανάκληση δικαιώματος '+accountName(grant.admin_account_id)+' σε '+scopeName(grant)}>
              Ανάκληση
            </button>
          </li>
        {/each}
      </ul>
    {/if}
  {/if}
  {#if notice}<p role="status">{notice}</p>{/if}
  {#if error}<p role="alert">{error}</p>{/if}
</section>

<style>
  .delegation{padding:1.25rem;border:1px solid rgba(173,137,93,.45);
    border-radius:1rem}
  h2{font-size:1.2rem}
  h3{font-size:1.02rem;margin:1rem 0}
  .access-form{display:grid;gap:.9rem;margin:1rem 0;max-width:650px;
    padding:1rem 0;border-block:1px solid rgba(173,137,93,.35)}
  label{display:grid;gap:.4rem;font-weight:600}
  select{width:100%;min-height:44px;padding:.6rem;color:inherit;background:transparent;
    border:1px solid #8c806b;border-radius:.5rem}
  button{min-height:44px;padding:.6rem .9rem;cursor:pointer}
  button:disabled{opacity:.55;cursor:not-allowed}
  select:focus-visible,button:focus-visible{outline:3px solid #bd9763;outline-offset:2px}
  .grant-list{list-style:none;display:grid;gap:.6rem;padding:0;margin:0}
  .grant-list li{display:flex;gap:.8rem;justify-content:space-between;
    align-items:center;flex-wrap:wrap;border:1px solid rgba(173,137,93,.35);
    border-radius:.5rem;padding:.7rem}
  .grant-list small{display:block;margin-top:.25rem}
  [role="alert"]{font-weight:700}
</style>
