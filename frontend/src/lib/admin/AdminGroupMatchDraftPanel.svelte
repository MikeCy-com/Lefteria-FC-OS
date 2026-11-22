<script lang="ts">
  import {browser} from '$app/env';
  import {getContext,untrack} from 'svelte';
  import {ADMIN_AUTH_CONTEXT,type AdminAuthController} from './controller.ts';

  type Owner='lefteria_fc'|'lefteria_fc_academy';
  type Catalog={id:string;name:string;scope_id:string;organization_id:Owner;is_active:boolean};
  type Scope={id:string;organization_id:Owner;team_id:string|null;can_write_catalog:boolean};
  type Location={id:string;can_manage:boolean};
  type Assigned={id:string;scope_id:string;location_id:string};
  type MatchDraft={id:string;scope_id:string;organization_id:Owner;
    academy_group_id:string|null;club_team_id:string|null;
    opponent_id:string;playing_field_id:string;
    match_date:string;kickoff_local:string;status:'draft';competition:string};
  type Directory={organization_id:Owner;groups:Assigned[];scopes:Scope[];
    locations:Location[];opponents:Catalog[];playing_fields:Catalog[];match_drafts:MatchDraft[]};

  let {groupId,organization,actorId,canWrite=false}:{
    groupId:string;organization:Owner;actorId:string;canWrite:boolean
  }=$props();
  const auth=getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  let directory=$state<Directory|null>(null);
  let loading=$state(false),saving=$state(false),revision=$state(0);
  let error=$state(''),message=$state('');
  let formOpen=$state(false);
  let opponentId=$state(''),fieldId=$state(''),matchDate=$state(''),
    matchTime=$state('17:00'),competition=$state(''),homeGame=$state(true);
  const assignment=$derived(organization==='lefteria_fc_academy'
    ?directory?.groups.find(group=>group.id===groupId):null);
  const scope=$derived(organization==='lefteria_fc_academy'
    ?directory?.scopes.find(s=>s.id===assignment?.scope_id)
    :directory?.scopes.find(s=>s.team_id===groupId));
  const location=$derived(organization==='lefteria_fc_academy'
    ?directory?.locations.find(l=>l.id===assignment?.location_id):null);
  const mayWrite=$derived(Boolean(canWrite&&scope&&(
    organization==='lefteria_fc'?scope.can_write_catalog:location?.can_manage)));
  const opponents=$derived((directory?.opponents??[]).filter(row=>
    row.scope_id===scope?.id&&row.organization_id===organization&&row.is_active));
  const fields=$derived((directory?.playing_fields??[]).filter(row=>
    row.scope_id===scope?.id&&row.organization_id===organization&&row.is_active));
  const drafts=$derived((directory?.match_drafts??[]).filter(row=>
    row.organization_id===organization&&row.scope_id===scope?.id&&row.status==='draft'&&
    (organization==='lefteria_fc_academy'?row.academy_group_id===groupId:row.club_team_id===groupId)));
  function validDirectory(value:unknown,owner:Owner):value is Directory{
    if(!value||typeof value!=='object'||Array.isArray(value))return false;
    const obj=value as Record<string,unknown>;
    if(obj.organization_id!==owner)return false;
    for(const name of ['groups','scopes','locations','opponents','playing_fields','match_drafts']){
      const list=obj[name];
      if(!Array.isArray(list)||list.length>3000||list.some(item=>!item||
        typeof item!=='object'||Array.isArray(item)||
        typeof item.id!=='string'))return false;
    }
    return (obj.scopes as Scope[]).every(row=>row.organization_id===owner&&
      typeof row.can_write_catalog==='boolean'&&typeof row.id==='string')&&
      (obj.opponents as Catalog[]).every(row=>row.organization_id===owner)&&
      (obj.playing_fields as Catalog[]).every(row=>row.organization_id===owner)&&
      (obj.match_drafts as MatchDraft[]).every(row=>
        row.organization_id===owner&&row.status==='draft');
  }

  $effect(()=>{
    const owner=organization,actor=actorId,id=groupId;
    void revision;
    directory=null;loading=false;error='';formOpen=false;
    if(!browser||!actor||!id)return;
    const controller=new AbortController();
    loading=true;
    untrack(()=>void(async()=>{
      try{
        const response=await fetch('/api/admin/football/territories?organization_id='+encodeURIComponent(owner),{
          credentials:'same-origin',cache:'no-store',signal:controller.signal,
          headers:{Accept:'application/json'}
        });
        if(controller.signal.aborted)return;
        if(response.status===401){auth.invalidateSession();return;}
        if(response.status===403){void auth.bootstrap();throw Error('Η ανάθεση πρόσβασης άλλαξε.');}
        if(!response.ok)throw Error('Ο επαληθευμένος κατάλογος αγώνων δεν είναι διαθέσιμος.');
        const raw:unknown=await response.json();
        if(!validDirectory(raw,owner))throw Error('Η απάντηση καταλόγου δεν επαληθεύτηκε.');
        if(!controller.signal.aborted)directory=raw;
      }catch(e){
        if(!controller.signal.aborted)error=e instanceof Error?e.message:'Ο κατάλογος αγώνων δεν είναι διαθέσιμος.';
      }finally{if(!controller.signal.aborted)loading=false;}
    })());
    return()=>controller.abort();
  });

  // Do not carry a confirmation into another group's protected workspace.
  $effect(()=>{void groupId;void actorId;void organization;message='';});

  async function submitDraft(){
    if(!browser||saving||!mayWrite||!scope||!directory)return;
    if(!opponents.some(x=>x.id===opponentId)||!fields.some(x=>x.id===fieldId)||
      !/^\d{4}-\d\d-\d\d$/.test(matchDate)||!/^\d\d:\d\d$/.test(matchTime)||
      competition.length>160){
      error='Επιλέξτε αντίπαλο και γήπεδο της ίδιας πόλης και έγκυρη ημερομηνία/ώρα.';
      return;
    }
    const owner=organization,actor=actorId,id=groupId;
    const body={kind:'match_draft',data:{
      scope_id:scope.id,
      ...(owner==='lefteria_fc_academy'
        ?{location_id:assignment?.location_id,group_id:id}:{}),
      opponent_id:opponentId,playing_field_id:fieldId,
      match_date:matchDate,kickoff_local:matchTime,
      home_game:homeGame,competition:competition.trim()
    }};
    saving=true;error='';message='';
    try{
      const response=await fetch('/api/admin/football/territories?organization_id='+encodeURIComponent(owner),{
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify(body)
      });
      if(owner!==organization||id!==groupId||actor!==actorId)return;
      if(response.status===401){auth.invalidateSession();return;}
      if(response.status===403){void auth.bootstrap();throw Error('Το δικαίωμα δημιουργίας αγώνα άλλαξε.');}
      if(!response.ok)throw Error(response.status===409?
        'Η καταχώριση συγκρούεται με τα τρέχοντα στοιχεία. Ανανεώστε τον κατάλογο.':
        'Η δημιουργία του προχείρου απέτυχε.');
      const saved:unknown=await response.json();
      if(!saved||typeof saved!=='object'||Array.isArray(saved)||
        !('kind' in saved)||saved.kind!=='match_draft'||
        !('organization_id' in saved)||saved.organization_id!==owner||
        !('id' in saved)||typeof saved.id!=='string')
        throw Error('Η αποθήκευση δεν επαληθεύτηκε.');
      message='Ο αγώνας αποθηκεύτηκε ως ιδιωτικό πρόχειρο του τμήματος. Η δημοσίευση και η επεξεργασία σκορ είναι ξεχωριστές ενέργειες.';
      formOpen=false;opponentId='';fieldId='';matchDate='';competition='';
      // Refresh private, owner-scoped data after server confirmation.
      revision++;
    }catch(e){error=e instanceof Error?e.message:'Η αποθήκευση απέτυχε.';}
    finally{saving=false;}
  }
</script>

<section class="admin-group-matches" data-testid="admin-group-match-drafts" aria-labelledby="admin-group-match-heading">
  <header class="admin-group-matches-head">
    <div><p class="admin-eyebrow">ΑΓΩΝΙΣΤΙΚΟ · ΕΠΙΛΕΓΜΕΝΟ ΤΜΗΜΑ</p>
      <h4 id="admin-group-match-heading">Αγώνες τμήματος</h4></div>
    {#if mayWrite}
      <button class="admin-secondary" type="button" disabled={saving}
        onclick={()=>formOpen=!formOpen} aria-expanded={formOpen}
        data-testid="admin-group-new-match">Νέος αγώνας</button>
    {/if}
  </header>
  {#if loading}<p role="status" class="admin-muted">Φόρτωση αγώνων και καταλόγων…</p>
  {:else if directory}
    {#if !scope}
      <p class="admin-muted">Απαιτείται ανάθεση του τμήματος σε πόλη και τοποθεσία
        ή δημιουργία του αντίστοιχου αγωνιστικού πεδίου για την πρώτη ομάδα.</p>
      <a href="/admin/territories">Ρυθμίσεις πόλεων και ομάδων ↗</a>
    {:else}
      {#if drafts.length}
        <ul class="admin-group-drafts" data-testid="admin-group-private-drafts">
          {#each drafts as draft (draft.id)}
            <li><strong>{draft.match_date} · {draft.kickoff_local}</strong>
              <span>{opponents.find(item=>item.id===draft.opponent_id)?.name||'Επιβεβαιωμένος αντίπαλος'}</span>
              <small>Ιδιωτικό πρόχειρο · {draft.competition||'Αγώνας'}</small></li>
          {/each}
        </ul>
      {:else}
        <p class="admin-muted">Δεν υπάρχουν ιδιωτικά πρόχειρα αγώνων για αυτό το τμήμα.</p>
      {/if}
      {#if canWrite&&!mayWrite}
        <p class="admin-muted">Δεν υπάρχει επαληθευμένη άδεια δημιουργίας
          σε αυτήν την πόλη, τοποθεσία ή πρώτη ομάδα.</p>
      {/if}
      {#if mayWrite && (opponents.length===0 || fields.length===0)}
        <p class="admin-muted">Καταχωρίστε πρώτα αντίπαλο και γήπεδο αγώνα στον κοινό κατάλογο πόλης.
          Οι τοποθεσίες δεν δημιουργούν δικούς τους ανεξάρτητους αντιπάλους.</p>
        <a href={'/admin/territories?organization_id='+encodeURIComponent(organization)}
          data-testid="admin-match-manage-catalog">
          {organization==='lefteria_fc_academy'?'Κατάλογος αντιπάλων & γηπέδων της πόλης':'Κατάλογος Πρώτης Ομάδας'} ↗
        </a>
      {/if}
      {#if mayWrite && formOpen}
        <form class="admin-group-match-form" onsubmit={e=>{e.preventDefault();void submitDraft();}}
          data-testid="admin-group-match-form">
          <label>{organization==='lefteria_fc_academy'?'Αντίπαλος πόλης':'Αντίπαλος Πρώτης Ομάδας'}
            <select bind:value={opponentId} required disabled={saving}
              data-testid="admin-match-opponent-picker">
              <option value="">Επιλέξτε αντίπαλο</option>
              {#each opponents as row (row.id)}<option value={row.id}>{row.name}</option>{/each}
            </select>
          </label>
          <label>{organization==='lefteria_fc_academy'?'Γήπεδο αγώνα πόλης':'Γήπεδο Πρώτης Ομάδας'}
            <select bind:value={fieldId} required disabled={saving}
              data-testid="admin-match-field-picker">
              <option value="">Επιλέξτε γήπεδο</option>
              {#each fields as row (row.id)}<option value={row.id}>{row.name}</option>{/each}
            </select>
          </label>
          <label>Ημερομηνία<input type="date" bind:value={matchDate} required disabled={saving}/></label>
          <label>Ώρα έναρξης<input type="time" bind:value={matchTime} required disabled={saving}/></label>
          <label>Διοργάνωση<input type="text" bind:value={competition} maxlength="160" disabled={saving}/></label>
          <label class="admin-group-home-toggle"><input type="checkbox" bind:checked={homeGame} disabled={saving}/> Εντός έδρας</label>
          <button class="admin-secondary" type="submit" disabled={saving||!opponentId||!fieldId||!matchDate}>
            {saving?'Αποθήκευση…':'Αποθήκευση ιδιωτικού προχείρου'}
          </button>
        </form>
      {/if}
    {/if}
  {/if}
  {#if error}<p class="admin-group-match-error" role="alert">{error}</p>{/if}
  {#if message}<p class="admin-group-match-success" role="status">{message}</p>{/if}
  <p class="admin-muted admin-group-match-disclaimer">Τα ιδιωτικά πρόχειρα δεν είναι δημοσιευμένοι αγώνες.
    Αποτελέσματα και βαθμολογία εμφανίζονται μόνο μετά από ξεχωριστή επαλήθευση.</p>
</section>
<style>
  .admin-group-matches{display:grid;gap:12px;padding:17px;margin-top:19px;
    background:#101314;border:1px solid #6e5932}
  .admin-group-matches-head{display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:14px}
  .admin-group-matches-head h4{font:italic 850 24px/1.1 'Fira Sans Extra Condensed',sans-serif;margin:0}
  .admin-group-matches a{color:#ffcc62;text-decoration:underline}
  .admin-group-drafts{list-style:none;margin:0;padding:0;display:grid;gap:9px}
  .admin-group-drafts li{display:flex;gap:12px;align-items:center;flex-wrap:wrap;
    border:1px solid #494332;padding:11px;background:#191a18;font-size:13px}
  .admin-group-drafts small{color:#ffcb61}
  .admin-group-match-form{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:13px}
  .admin-group-match-form>label{display:grid;gap:6px;font-size:13px;font-weight:700}
  .admin-group-match-form :is(input,select){background:#080909;color:white;border:1px solid #82785f;
    padding:10px;min-height:44px;border-radius:4px}
  .admin-group-home-toggle{display:flex!important;align-items:center;gap:9px}
  .admin-group-home-toggle input{min-height:20px;width:20px}
  .admin-group-match-form>button{justify-self:start}
  .admin-group-match-error{color:#ffb5a9;border-left:3px solid #ee8271;padding:10px}
  .admin-group-match-success{color:#d4ffc6;border-left:3px solid #93c277;padding:10px}
  .admin-group-match-disclaimer{font-size:12px}
  @media(max-width:650px){.admin-group-match-form{grid-template-columns:1fr}}
</style>
