<script lang="ts">
  import { browser } from '$app/env';
  import AdminPlayerPrivateRecordsPanel from './AdminPlayerPrivateRecordsPanel.svelte';
  import AdminPlayerDevelopmentGoals from './AdminPlayerDevelopmentGoals.svelte';
  import AdminPlayerPrivateProfilePanel from './AdminPlayerPrivateProfilePanel.svelte';
  import AdminGroupAttendancePanel from './AdminGroupAttendancePanel.svelte';

  type Owner = 'lefteria_fc' | 'lefteria_fc_academy';
  type Audience = {id: string;name: string;kind: 'team'|'academy_group';active_players: number};
  type Player = {id:string;name:string;team_id:string|null;academy_group_ids:string[]};
  type Page = {organization_id:Owner;group_id?:string;ready:true;offset:number;limit:number;total:number;players:Player[]};

  let {owner,actorId,groups,focusGroupId=null,allowPrivate,allowEvaluationWrite,allowFinance=false,allowGoalManagement=false,allowGoalRead=false,canReadAttendance=false,canWriteAttendance=false,onExpired,onRevoked,onChanged}: {
    owner:Owner;actorId:string;groups:Audience[];focusGroupId?:string|null;
    allowPrivate:boolean;allowEvaluationWrite:boolean;allowFinance?:boolean;allowGoalManagement?:boolean;
    allowGoalRead?:boolean;
    canReadAttendance?:boolean;canWriteAttendance?:boolean;
    onExpired:()=>void;onRevoked:()=>void;onChanged:()=>void;
  } = $props();

  const pageSize = 20;
  let offset = $state(0);
  let rosterMode=$state<'group'|'assign'>('group');
  const currentGroup=$derived(focusGroupId&&rosterMode==='group'?focusGroupId:null);
  let refresh = $state(0);
  let playerSearch=$state('');
  let playerGroupFilter=$state('');
  let page = $state<Page|null>(null);
  const visiblePlayers=$derived((page?.players??[]).filter(player=>{
    const term=playerSearch.trim().toLocaleLowerCase('el-GR');
    const matchesGroup=!playerGroupFilter || currentGroups(player).includes(playerGroupFilter);
    return matchesGroup && (!term || player.name.toLocaleLowerCase('el-GR').includes(term) ||
      player.id.toLocaleLowerCase('el-GR').includes(term) ||
      groupLabels(player).toLocaleLowerCase('el-GR').includes(term));
  }));

  let loading = $state(false);
  let failed = $state(false);
  let editing = $state<string|null>(null);
  let selectedGroups = $state<string[]>([]);
  let inspectedPlayer = $state<string|null>(null);
  let inspectedGoals = $state<string|null>(null);
  let inspectedProfile = $state<string|null>(null);
  let selectedPlayer360=$state<string|null>(null);
  const focusedPlayer360=$derived(page?.players.find(player=>player.id===selectedPlayer360)??null);
  type Player360Tab='overview'|'profile'|'documents'|'development'|'goals'|'attendance';
  let player360Tab=$state<Player360Tab>('overview');
  function closePlayer360(){
    selectedPlayer360=null;player360Tab='overview';
    inspectedProfile=null;inspectedPlayer=null;inspectedGoals=null;
  }
  function selectPlayer360Tab(player:Player,tab:Player360Tab){
    if(selectedPlayer360!==player.id)return;
    if(tab==='profile'&&!(allowPrivate&&owner==='lefteria_fc_academy'))return;
    if((tab==='documents'||tab==='development')&&!(allowPrivate&&currentGroups(player).length>0))return;
    if(tab==='goals'&&!(allowGoalRead&&currentGroup&&currentGroups(player).includes(currentGroup)))return;
    if(tab==='attendance'&&!(allowPrivate&&currentGroup&&owner==='lefteria_fc_academy'&&canReadAttendance&&currentGroups(player).includes(currentGroup)))return;
    player360Tab=tab;
    inspectedProfile=tab==='profile'?player.id:null;
    inspectedPlayer=tab==='documents'||tab==='development'?player.id:null;
    inspectedGoals=tab==='goals'?player.id:null;
  }
  let saving = $state(false);
  let saveError = $state<string|null>(null);
  let saveSuccess = $state<string|null>(null);

  function currentGroups(player:Player):string[] {
    return owner==='lefteria_fc'
      ? (player.team_id ? [player.team_id] : []) : [...player.academy_group_ids];
  }
  function startEditing(player:Player) {
    // Exact-group rows redact all other group memberships. Editing from
    // that minimized view could silently remove a child's other groups.
    // Open explicit organization-wide assignment mode before any PUT.
    if (saving || loading || !groups.length || currentGroup) return;
    closePlayer360();
    editing=player.id;
    selectedGroups=currentGroups(player);
    saveError=null;
    saveSuccess=null;
  }
  function cancelEditing() {
    if (saving) return;
    editing=null;
    selectedGroups=[];
    saveError=null;
  }
  function toggleGroup(id:string,checked:boolean) {
    if(saving) return;
    selectedGroups=checked
      ? [...selectedGroups,id].sort()
      : selectedGroups.filter(group=>group!==id);
  }
  async function saveAssignment(player:Player) {
    if (!browser || saving || loading || editing!==player.id || !page || currentGroup) return;
    const selectedOwner=owner;
    const selectedActor=actorId;
    const previous=currentGroups(player);
    const desired=selectedGroups.slice().sort();
    if(desired.length>20 || (selectedOwner==='lefteria_fc' && desired.length>1) ||
      desired.some(id=>!groups.some(g=>g.id===id))) {
      saveError='Μη έγκυρη επιλογή τμήματος.';
      return;
    }
    saving=true;
    saveError=null;
    saveSuccess=null;
    try {
      const query=new URLSearchParams({organization_id:selectedOwner});
      const response=await fetch('/api/admin/football/assignments?'+query.toString(),{
        method:'PUT',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({player_id:player.id,expected_group_ids:previous,group_ids:desired})
      });
      if (owner!==selectedOwner || actorId!==selectedActor) return;
      if(response.status===401){onExpired();throw new Error('Η σύνδεση έληξε.');}
      if(response.status===403){onRevoked();throw new Error('Η πρόσβαση άλλαξε.');}
      if(response.status===409){
        editing=null;
        refresh++;
        onChanged();
        throw new Error('Το ρόστερ ενημερώθηκε από άλλον διαχειριστή. Ελέγξτε τα νέα στοιχεία.');
      }
      if(!response.ok)throw new Error('Η ανάθεση δεν αποθηκεύτηκε.');
      const raw:unknown=await response.json();
      if(!raw || typeof raw!=='object' || Array.isArray(raw))
        throw new Error('Μη επαληθευμένη απάντηση.');
      const saved=raw as Record<string,unknown>;
      const returnedGroups=saved.academy_group_ids;
      if(saved.organization_id!==selectedOwner || saved.player_id!==player.id ||
        !Array.isArray(returnedGroups) ||
        (selectedOwner==='lefteria_fc' &&
          (saved.team_id!==(desired[0]??null) || returnedGroups.length!==0)) ||
        (selectedOwner==='lefteria_fc_academy' &&
          (saved.team_id!==null ||
           returnedGroups.length!==desired.length ||
           !desired.every(id=>returnedGroups.includes(id)))))
        throw new Error('Μη επαληθευμένη ανάθεση.');
      editing=null;
      selectedGroups=[];
      saveSuccess='Η ανάθεση αποθηκεύτηκε. Ο κατάλογος ανανεώνεται.';
      refresh++;
      onChanged();
    } catch(error) {
      if(owner===selectedOwner && actorId===selectedActor)
        saveError=error instanceof Error?error.message:'Η αποθήκευση απέτυχε.';
    } finally { saving=false; }
  }

  function verified(raw:unknown,expected:Owner,wantedOffset:number,group:string|null):raw is Page {
    if(!raw || typeof raw!=='object' || Array.isArray(raw))return false;
    const response=raw as Record<string,unknown>;
    if(response.organization_id!==expected ||
      (group!==null && response.group_id!==group) ||
      (group===null && response.group_id!=null) || response.ready!==true ||
      response.offset!==wantedOffset || response.limit!==pageSize ||
      !Number.isSafeInteger(response.total) || (response.total as number)<0 ||
      (response.total as number)>25000 || !Array.isArray(response.players) ||
      response.players.length>pageSize || response.players.length>(response.total as number))return false;
    const ids=new Set<string>();
    return response.players.every((value:unknown)=>{
      if(!value || typeof value!=='object' || Array.isArray(value))return false;
      const p=value as Record<string,unknown>;
      if(typeof p.id!=='string' || p.id.length<1 || p.id.length>128 ||
        ids.has(p.id) || typeof p.name!=='string' || p.name.length<1 || p.name.length>200 ||
        !(p.team_id===null || (typeof p.team_id==='string' && p.team_id.length<=128)) ||
        !Array.isArray(p.academy_group_ids) || p.academy_group_ids.length>150 ||
        !p.academy_group_ids.every((id:unknown)=>typeof id==='string' && id.length>0 && id.length<=128) ||
        (expected==='lefteria_fc' && p.academy_group_ids.length>0) ||
        (expected==='lefteria_fc_academy' && p.team_id!==null) ||
        (group!==null && (expected==='lefteria_fc' ? p.team_id!==group :
          !(p.academy_group_ids as string[]).includes(group))))return false;
      ids.add(p.id);
      return true;
    });
  }

  function groupLabels(player:Player):string {
    const assigned=owner==='lefteria_fc'
      ? (player.team_id ? [player.team_id] : []) : player.academy_group_ids;
    return assigned.length
      ? assigned.map(id => groups.find(group=>group.id===id)?.name ?? 'Μη διαθέσιμο τμήμα').join(' · ')
      : 'Χωρίς ανάθεση';
  }

  $effect(()=>{
    const currentOwner=owner;
    const actor=actorId;
    const currentOffset=offset;
    const group=currentGroup;
    const generation=refresh;
    void actor;void generation;

    page=null;loading=false;failed=false;editing=null;closePlayer360();
    selectedGroups=[];saveError=null;
    if(!browser)return;
    const controller=new AbortController();
    loading=true;
    void (async()=>{
      try{
        const params=new URLSearchParams({
          organization_id:currentOwner,offset:String(currentOffset),limit:String(pageSize)
        });
        if(group)params.set('group_id',group);
        const source=group?'/api/admin/football/group-players?':'/api/admin/football/players?';
        const response=await fetch(source+params.toString(),{
          method:'GET',credentials:'same-origin',cache:'no-store',signal:controller.signal
        });
        if(controller.signal.aborted)return;
        if(response.status===401){onExpired();return;}
        if(response.status===403){onRevoked();return;}
        if(!response.ok)throw new Error('Roster directory unavailable.');
        const json:unknown=await response.json();
        if(!verified(json,currentOwner,currentOffset,group))throw new Error('Invalid roster response.');
        if(!controller.signal.aborted)page=json;
      }catch{
        if(!controller.signal.aborted)failed=true;
      }finally{if(!controller.signal.aborted)loading=false;}
    })();
    return ()=>controller.abort();
  });
</script>

<section class="admin-panel admin-player-directory" data-testid="admin-private-player-directory"
  aria-labelledby="admin-private-player-heading">
  {#if !focusedPlayer360}
  <div class="admin-player-heading">
    <div>
      <p class="admin-eyebrow">ΕΞΟΥΣΙΟΔΟΤΗΜΕΝΟ ΡΟΣΤΕΡ · ΜΟΝΟ ΔΙΑΧΕΙΡΙΣΤΕΣ</p>
      <h2 id="admin-private-player-heading">Παίκτες & αναθέσεις τμημάτων</h2>
      <p class="admin-muted">Προβολή ενεργών παικτών για επαλήθευση ανάθεσης. Επιλέξτε παίκτη και αναθέστε τον σε επαληθευμένη ομάδα. Κάθε αλλαγή ελέγχεται και καταγράφεται.</p>
    </div>
    <button type="button" class="admin-secondary" onclick={()=>refresh++} disabled={loading}>
      Ανανέωση παικτών
    </button>
  </div>
  {#if focusGroupId}
    <div class="admin-player-group-scope" role="group" aria-label="Επιλογή ρόστερ" data-testid="group-player-scope">
      <button type="button" aria-pressed={rosterMode==='group'} disabled={saving}
        onclick={()=>{rosterMode='group';offset=0;}} data-testid="group-current-player-filter">
        Αθλητές τμήματος</button>
      <button type="button" aria-pressed={rosterMode==='assign'} disabled={saving}
        onclick={()=>{rosterMode='assign';offset=0;}} data-testid="group-assign-player-filter">
        Εύρεση αθλητή για ανάθεση</button>
    </div>
    {#if rosterMode==='assign'}
      <p class="admin-muted">Η εύρεση χρησιμοποιεί τον εξουσιοδοτημένο κατάλογο του οργανισμού
        για να προσθέσετε αθλητή στο τμήμα. Η ανάθεση ελέγχεται από το API.</p>
    {/if}
  {/if}
  <div class="admin-player-filters" role="search" aria-label="Αναζήτηση αθλητών">
    <label for="admin-player-search">Αναζήτηση στην τρέχουσα σελίδα
      <input id="admin-player-search" type="search" bind:value={playerSearch}
        data-testid="admin-player-search" placeholder="Όνομα, αναγνωριστικό ή τμήμα…" autocomplete="off"/>
    </label>
    {#if !currentGroup}
      <label for="admin-player-group-filter">Ομάδα / τμήμα
        <select id="admin-player-group-filter" bind:value={playerGroupFilter}
          data-testid="admin-player-group-filter">
          <option value="">Όλα τα εξουσιοδοτημένα τμήματα</option>
          {#each groups as group (group.id)}
            <option value={group.id}>{group.name}</option>
          {/each}
        </select>
      </label>
    {/if}
    <p class="admin-muted">Η αναζήτηση αφορά μόνο τις εγγραφές της τρέχουσας σελίδας.
      Για πλήρη έλεγχο του ρόστερ χρησιμοποιήστε την πλοήγηση σελίδων.</p>
  </div>
  {/if}
  {#if loading}
    <p role="status" data-testid="admin-players-loading">Ανάκτηση επαληθευμένου ρόστερ…</p>
  {:else if failed || !page}
    <div role="alert" data-testid="admin-players-error">
      <p>Τα στοιχεία του ρόστερ δεν είναι διαθέσιμα.</p>
      <button type="button" class="admin-secondary" onclick={()=>refresh++}>Επανάληψη</button>
    </div>
  {:else}
    {#if !focusedPlayer360}<div class="admin-player-total" role="status">
      <strong>{page.total}</strong><span>{currentGroup?'ενεργοί αθλητές στο επιλεγμένο τμήμα':'ενεργοί παίκτες στον επιλεγμένο οργανισμό'}</span>
      <span class="admin-player-filter-count">Εμφανίζονται {visiblePlayers.length} / {page.players.length} στη σελίδα</span>
    </div>{/if}
    {#if saveError}<p role="alert" class="admin-player-error" data-testid="admin-assignment-error">{saveError}</p>{/if}
    {#if saveSuccess}<p role="status" data-testid="admin-assignment-success">{saveSuccess}</p>{/if}
    {#if page.players.length===0}
      <p class="admin-muted" data-testid="admin-players-empty">{currentGroup?'Δεν υπάρχουν ακόμη αθλητές σε αυτό το τμήμα. Επιλέξτε «Εύρεση αθλητή για ανάθεση».':'Δεν υπάρχουν παίκτες σε αυτή τη σελίδα.'}</p>
    {:else}
      {#if !focusedPlayer360}<div class="admin-player-columns" aria-hidden="true">
        <span>ΠΑΙΚΤΗΣ</span><span>ΟΜΑΔΑ / ΤΜΗΜΑ · ΕΝΕΡΓΕΙΕΣ</span>
      </div>{/if}
      <ul class="admin-player-list" data-testid="admin-players-list">
        {#each (focusedPlayer360?[focusedPlayer360]:visiblePlayers) as player (player.id)}
          <li class:admin-player-focused={focusedPlayer360!==null}>
            {#if !focusedPlayer360}<div class="admin-player-identity">
              <span class="admin-player-avatar" aria-hidden="true">{player.name.slice(0,1).toLocaleUpperCase('el-GR')}</span>
              <div><strong>{player.name}</strong></div>
            </div>
            <div class="admin-player-actions">
              <span class="admin-player-team-label">{groupLabels(player)}</span>
              {#if allowPrivate || allowGoalRead || allowFinance}
                <button type="button" class="admin-primary admin-player360-open" disabled={saving||loading||editing!==null}
                  data-testid="admin-open-player-360"
                  aria-expanded={selectedPlayer360===player.id}
                  onclick={()=>{if(selectedPlayer360===player.id)closePlayer360();else{closePlayer360();selectedPlayer360=player.id;}}}>
                  {selectedPlayer360===player.id?'Κλείσιμο φακέλου':'Player 360 · Προφίλ'} <span aria-hidden="true">›</span>
                </button>
              {/if}
              {#if editing===player.id}
                <div class="admin-player-editor" data-testid="admin-player-assignment-editor">
                  {#if owner==='lefteria_fc'}
                    <label for={'admin-player-team-'+player.id}>Ομάδα</label>
                    <select id={'admin-player-team-'+player.id} value={selectedGroups[0]??''}
                      disabled={saving} onchange={event=>selectedGroups=event.currentTarget.value?[event.currentTarget.value]:[]}>
                      <option value="">Χωρίς ανάθεση</option>
                      {#each groups as group (group.id)}
                        <option value={group.id}>{group.name}</option>
                      {/each}
                    </select>
                  {:else}
                    <fieldset disabled={saving}>
                      <legend>Τμήματα Ακαδημίας (έως 20)</legend>
                      {#each groups as group (group.id)}
                        <label class="admin-player-group-choice">
                          <input type="checkbox" checked={selectedGroups.includes(group.id)}
                            disabled={saving || (!selectedGroups.includes(group.id) && selectedGroups.length>=20)}
                            onchange={event=>toggleGroup(group.id,event.currentTarget.checked)} />
                          {group.name}
                        </label>
                      {/each}
                    </fieldset>
                  {/if}
                  <div class="admin-player-editor-buttons">
                    <button type="button" class="admin-secondary" disabled={saving}
                      onclick={()=>void saveAssignment(player)}>{saving?'Αποθήκευση…':'Αποθήκευση'}</button>
                    <button type="button" class="admin-secondary" disabled={saving} onclick={cancelEditing}>Ακύρωση</button>
                  </div>
                </div>
              {:else if !currentGroup}
                <button type="button" class="admin-secondary" disabled={saving || loading || groups.length===0}
                  onclick={()=>startEditing(player)}>Αλλαγή ανάθεσης</button>
              {:else}
                <span class="admin-group-minimal-roster-note">Οι αναθέσεις σε περισσότερα τμήματα
                  εμφανίζονται μόνο στη λειτουργία «Εύρεση αθλητή για ανάθεση».</span>
              {/if}
            </div>
            {/if}
            {#if selectedPlayer360===player.id && (allowPrivate||allowGoalRead||allowFinance)}
              {#key owner+':'+actorId+':'+player.id}
              <section class="admin-player360 admin-player-individual-workspace" aria-label={'Player 360 · '+player.name}
                data-testid="admin-player-360-workspace">
                <header class="admin-player360-head">
                  <div class="admin-player360-identity">
                    <span class="admin-player-avatar" aria-hidden="true">{player.name.slice(0,1).toLocaleUpperCase('el-GR')}</span>
                    <div><p class="admin-eyebrow">PLAYER 360 · {owner==='lefteria_fc_academy'?'ΑΚΑΔΗΜΙΑ':'ΠΡΩΤΗ ΟΜΑΔΑ'}</p>
                      <h3>{player.name}</h3><p class="admin-muted">{groupLabels(player)}</p></div>
                  </div>
                  <button type="button" class="admin-secondary" onclick={closePlayer360}>Πίσω στους παίκτες</button>
                </header>
                <div class="admin-player360-tabs" role="group" aria-label="Ενότητες αθλητή">
                  <button type="button" aria-pressed={player360Tab==='overview'} onclick={()=>selectPlayer360Tab(player,'overview')}>Επισκόπηση</button>
                  {#if allowPrivate && owner==='lefteria_fc_academy'}
                    <button type="button" data-testid="admin-open-player-private-profile" aria-pressed={player360Tab==='profile'} onclick={()=>selectPlayer360Tab(player,'profile')}>Στοιχεία & φωτογραφία</button>
                  {/if}
                  {#if allowPrivate && currentGroups(player).length>0}
                    <button type="button" aria-pressed={player360Tab==='documents'} onclick={()=>selectPlayer360Tab(player,'documents')}>Έγγραφα</button>
                    <button type="button" aria-pressed={player360Tab==='development'} onclick={()=>selectPlayer360Tab(player,'development')}>Ανάπτυξη & αξιολογήσεις</button>
                  {/if}
                  {#if allowGoalRead && currentGroup && currentGroups(player).includes(currentGroup)}
                    <button type="button" data-testid="admin-manager-goal-reader-toggle" aria-pressed={player360Tab==='goals'} onclick={()=>selectPlayer360Tab(player,'goals')}>Ατομικοί στόχοι</button>
                  {/if}
                  {#if allowPrivate && currentGroup && owner==='lefteria_fc_academy' && canReadAttendance}
                    <button type="button" aria-pressed={player360Tab==='attendance'} onclick={()=>selectPlayer360Tab(player,'attendance')}>Παρουσίες</button>
                  {/if}
                  {#if allowFinance && owner === 'lefteria_fc_academy'}
                    <a href={`/admin/finance?organization_id=lefteria_fc_academy&player_id=${encodeURIComponent(player.id)}`}
                      class="admin-player-finance-link" data-testid="admin-player-billing-link">Οικονομικά ›</a>
                  {/if}
                </div>
                {#if player360Tab==='overview'}
                  <div class="admin-player360-summary" data-testid="admin-player-360-overview">
                    <div><span>Οργανισμός</span><strong>{owner==='lefteria_fc_academy'?'Ακαδημία':'Πρώτη ομάδα'}</strong></div>
                    <div><span>Ομάδα / τμήμα</span><strong>{groupLabels(player)}</strong></div>
                    <div><span>Εγγραφή ρόστερ</span><strong>Ενεργός παίκτης</strong></div>
                  </div>
                  <details><summary>Κωδικός αναφοράς αθλητή</summary><small>{player.id}</small></details>
                  <p class="admin-muted">Επιλέξτε ενότητα για να δείτε τα πραγματικά στοιχεία του αθλητή. Εμφανίζονται μόνο οι ενότητες για τις οποίες έχετε δικαίωμα πρόσβασης.</p>
                {:else}
                  <p class="admin-muted admin-player360-save-note">Αποθηκεύστε τις αλλαγές πριν μεταβείτε σε άλλη ενότητα.</p>
                {/if}
                {#if inspectedProfile===player.id && allowPrivate && owner==='lefteria_fc_academy'}
                  <div data-testid="admin-player-private-profile-workspace">
                    <AdminPlayerPrivateProfilePanel playerId={player.id} playerName={player.name}
                      {actorId} allowWrite={allowEvaluationWrite} {onExpired} {onRevoked}/>
                  </div>
                {/if}
                {#if inspectedGoals===player.id && allowGoalRead && currentGroup &&
                  currentGroups(player).includes(currentGroup)}
                  <div data-testid="admin-manager-player-goals-workspace">
                    <AdminPlayerDevelopmentGoals {owner} playerId={player.id}
                      playerName={player.name} {actorId} readOnly {onExpired} {onRevoked}/>
                  </div>
                {/if}
                {#if inspectedPlayer===player.id && allowPrivate && currentGroups(player).length>0}
                  <div data-testid="admin-player-individual-workspace">
                    <AdminPlayerPrivateRecordsPanel {owner} playerId={player.id}
                      playerName={player.name} {actorId} allowCreate={allowEvaluationWrite} {allowGoalManagement} section={player360Tab==='documents'?'documents':'development'} {onExpired} {onRevoked}/>
                  </div>
                {/if}
                {#if player360Tab==='attendance' && allowPrivate && currentGroup && owner==='lefteria_fc_academy' && canReadAttendance && currentGroups(player).includes(currentGroup)}
                  <div class="admin-player-attendance-details" data-testid="admin-player-attendance-history">
                    <AdminGroupAttendancePanel groupId={currentGroup} {actorId}
                      canRead={canReadAttendance} canWrite={canWriteAttendance}
                      playerId={player.id} playerName={player.name}/>
                  </div>
                {/if}
              </section>
              {/key}
            {/if}
          </li>
        {/each}
      </ul>
      {#if !focusedPlayer360 && visiblePlayers.length===0}
        <p class="admin-muted" role="status" data-testid="admin-player-search-empty">Δεν υπάρχουν αποτελέσματα σε αυτή τη σελίδα.</p>
      {/if}
    {/if}
    {#if !focusedPlayer360}<nav class="admin-player-paging" aria-label="Σελίδες ρόστερ">
      <span>{page.total===0 ? 0 : offset+1}–{Math.min(offset+pageSize,page.total)} / {page.total}</span>
      <button type="button" class="admin-secondary" disabled={offset===0 || loading}
        onclick={()=>offset=Math.max(0,offset-pageSize)}>Προηγούμενη</button>
      <button type="button" class="admin-secondary" disabled={loading || offset+pageSize>=page.total}
        onclick={()=>offset+=pageSize}>Επόμενη</button>
    </nav>{/if}
  {/if}
</section>

<style>
  .admin-player-directory{margin:16px 0;border-radius:11px}
  .admin-player-directory h2{font-size:clamp(18px,1.7vw,23px)}
  .admin-player-filters{margin-top:16px;padding:13px;border:1px solid #303335;
    border-radius:9px;background:#101112;display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:9px}
  .admin-player-filters label{display:grid;gap:7px;margin:0;color:#cbd0ce;font-size:12px}
  .admin-player-filters input{min-height:42px;border-radius:6px}
  .admin-player-filters p{grid-column:1/-1;font-size:11px;margin:0;line-height:1.5}
  @media(max-width:670px){.admin-player-filters{grid-template-columns:1fr}}
  .admin-player-filter-count{margin-left:auto}
  .admin-player-columns{display:grid;grid-template-columns:1fr 1.5fr;gap:15px;
    padding:9px 0;border-top:1px solid #363a3a;color:#8e9caa;letter-spacing:.08em;font-size:10px}
  .admin-player-list li:hover{background:#ffffff05}
  .admin-player-identity{display:flex;gap:12px;min-width:0;align-items:center}
  .admin-player-identity>div{display:grid;gap:3px;min-width:0}
  .admin-player-identity small{font-size:10px;color:#939b9b;overflow-wrap:anywhere}
  .admin-player-avatar{width:35px;height:35px;border-radius:50%;display:grid;
    place-items:center;flex:none;background:#222629;color:#d2b775;font-size:14px;font-weight:700}
  .admin-player-team-label{color:#b3d3df!important}

  .admin-player-individual-workspace{grid-column:1/-1;min-width:0;display:grid;gap:12px}
  .admin-player360{padding:clamp(12px,2vw,24px);border:1px solid var(--admin-line);border-radius:12px;background:var(--admin-surface,#141617)}
  .admin-player360-head,.admin-player360-identity{display:flex;align-items:center;gap:14px;min-width:0;flex-wrap:wrap}
  .admin-player360-head{justify-content:space-between}
  .admin-player360-head h3,.admin-player360-head p{margin:0;overflow-wrap:anywhere}
  .admin-player360-tabs{display:flex;flex-wrap:wrap;gap:6px;border-bottom:1px solid var(--admin-line);padding-block:8px}
  .admin-player360-tabs :is(button,a){font:inherit;color:inherit;background:transparent;border:1px solid var(--admin-line);border-radius:6px;min-height:44px;padding:8px 12px;text-decoration:none;display:inline-flex;align-items:center;cursor:pointer}
  .admin-player360-tabs [aria-pressed=true]{color:var(--admin-gold);border-color:var(--admin-gold)}
  .admin-player360-tabs :is(button,a):focus-visible{outline:2px solid var(--admin-gold);outline-offset:2px}
  .admin-player360-summary{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,180px),1fr));gap:10px}
  .admin-player360-summary>div{display:grid;gap:6px;padding:12px;border:1px solid var(--admin-line);border-radius:8px;min-width:0;overflow-wrap:anywhere}
  .admin-player360-summary span,.admin-player360-save-note{font-size:12px}
  .admin-player360-open{min-height:44px}
  .admin-player-list>li.admin-player-focused{display:block;padding:0;border:0;background:transparent}
  .admin-player-focused>.admin-player360{width:100%;box-sizing:border-box}
  .admin-player-attendance-details{padding:13px;border:1px solid #98712b;background:#111414}
  .admin-player-attendance-details>summary{cursor:pointer;font-weight:800;color:#f8c95d;min-height:44px;display:flex;align-items:center}
  .admin-player-attendance-details>summary:focus-visible{outline:3px solid #f8c95d;outline-offset:3px}
  .admin-player-finance-link{display:inline-flex;align-items:center;min-height:44px;
    padding:9px 13px;border:1px solid #99702f;border-radius:7px;
    color:#ffca46;font-size:13px;font-weight:700}
  .admin-player-finance-link:hover{background:#f6bb3820;border-color:#ffca46}
  .admin-player-finance-link:focus-visible{outline:2px solid #ffca46;outline-offset:3px}
  .admin-player-heading{display:flex;align-items:start;justify-content:space-between;flex-wrap:wrap;gap:12px}
  .admin-player-heading h2{margin:0 0 7px}
  .admin-player-heading .admin-muted{margin:0;max-width:80ch}
  .admin-player-total{display:flex;align-items:baseline;gap:10px;padding:14px 0}
  .admin-player-total strong{font-family:'Fira Sans Extra Condensed',sans-serif;color:#ffca46;font-size:29px}
  .admin-player-total span{color:#b9bcbe;font-size:12px}
  .admin-player-list{list-style:none;padding:0;margin:0}
  .admin-player-list li{display:grid;grid-template-columns:1fr 1.5fr;gap:15px;align-items:center;border-top:1px solid #303335;min-height:58px;padding:10px 0}
  .admin-player-list strong{font-size:14px;overflow-wrap:anywhere}
  .admin-player-list span{font-size:12px;color:#b9bcbe;overflow-wrap:anywhere}
  .admin-player-actions{display:flex;flex-wrap:wrap;align-items:center;justify-content:space-between;gap:12px}
  .admin-player-editor{width:100%;display:grid;gap:9px;padding:12px;border:1px solid #4a412d;border-radius:10px}
  .admin-player-editor label,.admin-player-editor legend{font-size:12px;color:#d2d4d5}
  .admin-player-editor fieldset{border:0;padding:0;margin:0;display:grid;gap:7px;max-height:230px;overflow-y:auto}
  .admin-player-group-choice{display:flex;align-items:center;gap:9px}
  .admin-player-editor select{width:100%;max-width:410px}
  .admin-player-editor-buttons{display:flex;gap:8px;flex-wrap:wrap}
  .admin-player-error{color:#ffd29b}
  .admin-player-paging{display:flex;align-items:center;justify-content:flex-end;gap:10px;flex-wrap:wrap;padding-top:15px}
  .admin-player-paging span{font-size:12px;color:#b9bcbe;margin-right:auto}
  @media(max-width:600px){.admin-player-list li{grid-template-columns:1fr}.admin-player-columns{display:none}.admin-player-heading{display:grid}.admin-player-paging span{width:100%}}

  .admin-player-group-scope{display:flex;flex-wrap:wrap;gap:7px;margin:11px 0 8px}
  .admin-player-group-scope button{min-height:44px;padding:10px 14px;border:1px solid #777;
    border-radius:5px;background:#171a1c;color:#eee;cursor:pointer;
    font:800 14px 'Fira Sans Extra Condensed',sans-serif}
  .admin-player-group-scope button[aria-pressed=true]{background:#f6bb38;color:#080808;border-color:#f6bb38}
  .admin-player-group-scope button:focus-visible{outline:3px solid #ffd164;outline-offset:2px}
  .admin-player-group-scope button:disabled{opacity:.6;cursor:not-allowed}
  .admin-group-minimal-roster-note{font-size:11px;color:#bfb5a0;line-height:1.4;max-width:30ch}
</style>
