<script lang="ts">
  import { browser } from '$app/env';

  type Owner = 'lefteria_fc' | 'lefteria_fc_academy';
  type Audience = {id: string;name: string;kind: 'team'|'academy_group';active_players: number};
  type Player = {id:string;name:string;team_id:string|null;academy_group_ids:string[]};
  type Page = {organization_id:Owner;ready:true;offset:number;limit:number;total:number;players:Player[]};

  let {owner,actorId,groups,onExpired,onRevoked}: {
    owner:Owner;actorId:string;groups:Audience[];
    onExpired:()=>void;onRevoked:()=>void;
  } = $props();

  const pageSize = 20;
  let offset = $state(0);
  let refresh = $state(0);
  let page = $state<Page|null>(null);
  let loading = $state(false);
  let failed = $state(false);

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

    page=null;loading=false;failed=false;
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
      <p class="admin-muted">Προβολή ενεργών παικτών για επαλήθευση ανάθεσης. Η τροποποίηση αναθέσεων δεν έχει ενεργοποιηθεί.</p>
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
    {#if page.players.length===0}
      <p class="admin-muted" data-testid="admin-players-empty">Δεν υπάρχουν παίκτες σε αυτή τη σελίδα.</p>
    {:else}
      <ul class="admin-player-list" data-testid="admin-players-list">
        {#each page.players as player (player.id)}
          <li>
            <strong>{player.name}</strong>
            <span>{groupLabels(player)}</span>
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
  .admin-player-paging{display:flex;align-items:center;justify-content:flex-end;gap:10px;flex-wrap:wrap;padding-top:15px}
  .admin-player-paging span{font-size:12px;color:#b9bcbe;margin-right:auto}
  @media(max-width:600px){.admin-player-list li{grid-template-columns:1fr}.admin-player-heading{display:grid}.admin-player-paging span{width:100%}}
</style>
