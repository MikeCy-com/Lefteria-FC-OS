<script lang="ts">
  import { browser } from '$app/env';

  type Owner = 'lefteria_fc' | 'lefteria_fc_academy';
  type Audience = {id: string;name: string;kind: 'team'|'academy_group';active_players: number};
  type Player = {id:string;name:string;team_id:string|null;academy_group_ids:string[]};
  type Page = {organization_id:Owner;ready:true;offset:number;limit:number;total:number;players:Player[]};

  let {owner,actorId,groups,onExpired,onRevoked,onChanged}: {
    owner:Owner;actorId:string;groups:Audience[];
    onExpired:()=>void;onRevoked:()=>void;onChanged:()=>void;
  } = $props();

  const pageSize = 20;
  let offset = $state(0);
  let refresh = $state(0);
  let page = $state<Page|null>(null);
  let loading = $state(false);
  let failed = $state(false);
  let editing = $state<string|null>(null);
  let selectedGroups = $state<string[]>([]);
  let saving = $state(false);
  let saveError = $state<string|null>(null);
  let saveSuccess = $state<string|null>(null);

  function currentGroups(player:Player):string[] {
    return owner==='lefteria_fc'
      ? (player.team_id ? [player.team_id] : []) : [...player.academy_group_ids];
  }
  function startEditing(player:Player) {
    if (saving || loading || !groups.length) return;
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
    if (!browser || saving || loading || editing!==player.id || !page) return;
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

  function verified(raw:unknown,expected:Owner,wantedOffset:number):raw is Page {
    if(!raw || typeof raw!=='object' || Array.isArray(raw))return false;
    const response=raw as Record<string,unknown>;
    if(response.organization_id!==expected || response.ready!==true ||
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
        (expected==='lefteria_fc_academy' && p.team_id!==null))return false;
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
    const generation=refresh;
    void actor;void generation;

    page=null;loading=false;failed=false;editing=null;selectedGroups=[];saveError=null;
    if(!browser)return;
    const controller=new AbortController();
    loading=true;
    void (async()=>{
      try{
        const params=new URLSearchParams({
          organization_id:currentOwner,offset:String(currentOffset),limit:String(pageSize)
        });
        const response=await fetch('/api/admin/football/players?'+params.toString(),{
          method:'GET',credentials:'same-origin',cache:'no-store',signal:controller.signal
        });
        if(controller.signal.aborted)return;
        if(response.status===401){onExpired();return;}
        if(response.status===403){onRevoked();return;}
        if(!response.ok)throw new Error('Roster directory unavailable.');
        const json:unknown=await response.json();
        if(!verified(json,currentOwner,currentOffset))throw new Error('Invalid roster response.');
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
  {#if loading}
    <p role="status" data-testid="admin-players-loading">Ανάκτηση επαληθευμένου ρόστερ…</p>
  {:else if failed || !page}
    <div role="alert" data-testid="admin-players-error">
      <p>Τα στοιχεία του ρόστερ δεν είναι διαθέσιμα.</p>
      <button type="button" class="admin-secondary" onclick={()=>refresh++}>Επανάληψη</button>
    </div>
  {:else}
    <div class="admin-player-total" role="status">
      <strong>{page.total}</strong><span>ενεργοί παίκτες στον επιλεγμένο οργανισμό</span>
    </div>
    {#if saveError}<p role="alert" class="admin-player-error" data-testid="admin-assignment-error">{saveError}</p>{/if}
    {#if saveSuccess}<p role="status" data-testid="admin-assignment-success">{saveSuccess}</p>{/if}
    {#if page.players.length===0}
      <p class="admin-muted" data-testid="admin-players-empty">Δεν υπάρχουν παίκτες σε αυτή τη σελίδα.</p>
    {:else}
      <ul class="admin-player-list" data-testid="admin-players-list">
        {#each page.players as player (player.id)}
          <li>
            <strong>{player.name}</strong>
            <div class="admin-player-actions">
              <span>{groupLabels(player)}</span>
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
              {:else}
                <button type="button" class="admin-secondary" disabled={saving || loading || groups.length===0}
                  onclick={()=>startEditing(player)}>Αλλαγή ανάθεσης</button>
              {/if}
            </div>
          </li>
        {/each}
      </ul>
    {/if}
    <nav class="admin-player-paging" aria-label="Σελίδες ρόστερ">
      <span>{page.total===0 ? 0 : offset+1}–{Math.min(offset+pageSize,page.total)} / {page.total}</span>
      <button type="button" class="admin-secondary" disabled={offset===0 || loading}
        onclick={()=>offset=Math.max(0,offset-pageSize)}>Προηγούμενη</button>
      <button type="button" class="admin-secondary" disabled={loading || offset+pageSize>=page.total}
        onclick={()=>offset+=pageSize}>Επόμενη</button>
    </nav>
  {/if}
</section>

<style>
  .admin-player-directory{margin:16px 0}
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
  @media(max-width:600px){.admin-player-list li{grid-template-columns:1fr}.admin-player-heading{display:grid}.admin-player-paging span{width:100%}}
</style>
