<script lang="ts">
  import { browser } from '$app/env';

  type Owner='lefteria_fc'|'lefteria_fc_academy';
  type Group={id:string;name:string;kind:'team'|'academy_group';active_players:number};
  type Coach={id:string;name:string;job_role:string;group_ids:string[]};
  type Page={organization_id:Owner;ready:true;offset:number;limit:number;total:number;coaches:Coach[]};

  let {owner,actorId,groups,onExpired,onRevoked}: {
    owner:Owner;actorId:string;groups:Group[];
    onExpired:()=>void;onRevoked:()=>void;
  }=$props();

  const pageSize=20;
  const roles=new Set(['Head Coach','Assistant Coach','Goalkeeper Coach','Fitness Coach','Youth Coach']);
  let offset=$state(0);
  let refresh=$state(0);
  let page=$state<Page|null>(null);
  let loading=$state(false);
  let unavailable=$state(false);

  function verified(raw:unknown,expected:Owner,wantedOffset:number):raw is Page {
    if(!raw || typeof raw!=='object' || Array.isArray(raw))return false;
    const r=raw as Record<string,unknown>;
    if(r.organization_id!==expected || r.ready!==true || r.offset!==wantedOffset ||
      r.limit!==pageSize || !Number.isSafeInteger(r.total) ||
      (r.total as number)<0 || (r.total as number)>25000 ||
      !Array.isArray(r.coaches) || r.coaches.length>pageSize ||
      r.coaches.length>(r.total as number))return false;
    const ids=new Set<string>();
    return r.coaches.every((entry:unknown)=>{
      if(!entry || typeof entry!=='object' || Array.isArray(entry))return false;
      const c=entry as Record<string,unknown>;
      if(typeof c.id!=='string' || c.id.length<1 || c.id.length>128 || ids.has(c.id) ||
        typeof c.name!=='string' || c.name.length<1 || c.name.length>200 ||
        typeof c.job_role!=='string' || !roles.has(c.job_role) ||
        !Array.isArray(c.group_ids) || c.group_ids.length>150 ||
        !c.group_ids.every((g:unknown)=>typeof g==='string' && g.length>0 && g.length<=128) ||
        new Set(c.group_ids).size!==c.group_ids.length)return false;
      ids.add(c.id);
      return true;
    });
  }

  $effect(()=>{
    const selectedOwner=owner;
    const selectedActor=actorId;
    const selectedOffset=offset;
    const generation=refresh;
    void selectedActor;void generation;
    page=null;loading=false;unavailable=false;
    if(!browser)return;
    const controller=new AbortController();
    loading=true;
    void(async()=>{
      try{
        const query=new URLSearchParams({organization_id:selectedOwner,
          offset:String(selectedOffset),limit:String(pageSize)});
        const response=await fetch('/api/admin/football/coaches?'+query.toString(),{
          method:'GET',credentials:'same-origin',cache:'no-store',signal:controller.signal
        });
        if(controller.signal.aborted)return;
        if(response.status===401){onExpired();return;}
        if(response.status===403){onRevoked();return;}
        if(!response.ok)throw new Error('Coach directory unavailable');
        const raw:unknown=await response.json();
        if(!verified(raw,selectedOwner,selectedOffset))
          throw new Error('Unverified coach directory');
        if(!controller.signal.aborted)page=raw;
      }catch{
        if(!controller.signal.aborted)unavailable=true;
      }finally{if(!controller.signal.aborted)loading=false;}
    })();
    return ()=>controller.abort();
  });
</script>

<section class="admin-panel coach-directory" data-testid="admin-coach-directory"
  aria-labelledby="admin-coaches-heading">
  <div class="coach-heading">
    <div>
      <p class="admin-eyebrow">ΠΡΟΠΟΝΗΤΕΣ · ΕΠΑΛΗΘΕΥΜΕΝΕΣ ΑΝΑΘΕΣΕΙΣ</p>
      <h2 id="admin-coaches-heading">Προπονητές & τμήματα</h2>
      <p class="admin-muted">Προβολή πραγματικών αναθέσεων από το προσωπικό του συλλόγου. Η αλλαγή προπονητών δεν έχει ενεργοποιηθεί ακόμη.</p>
    </div>
    <button type="button" class="admin-secondary" disabled={loading}
      onclick={()=>refresh++}>Ανανέωση</button>
  </div>
  {#if loading}
    <p role="status">Φόρτωση προπονητών…</p>
  {:else if unavailable || !page}
    <div role="alert" data-testid="admin-coach-directory-unavailable">
      <p>Ο κατάλογος προπονητών δεν είναι διαθέσιμος.</p>
      <button type="button" class="admin-secondary" onclick={()=>refresh++}>Δοκιμή ξανά</button>
    </div>
  {:else}
    <p class="coach-count" role="status">{page.total} ενεργοί προπονητές</p>
    {#if !page.coaches.length}
      <p class="admin-muted">Δεν βρέθηκαν προπονητές σε αυτή τη σελίδα.</p>
    {:else}
      <ul class="coach-list" data-testid="admin-coaches-list">
        {#each page.coaches as coach (coach.id)}
          <li>
            <div><strong>{coach.name}</strong><span>{coach.job_role}</span></div>
            <span>{coach.group_ids.length
              ? coach.group_ids.map(id=>groups.find(g=>g.id===id)?.name??'Μη διαθέσιμο τμήμα').join(' · ')
              : 'Χωρίς ανάθεση'}</span>
          </li>
        {/each}
      </ul>
    {/if}
    <nav class="coach-paging" aria-label="Σελίδες προπονητών">
      <span>{page.total===0?0:offset+1}–{Math.min(offset+pageSize,page.total)} / {page.total}</span>
      <button type="button" class="admin-secondary" disabled={loading || offset===0}
        onclick={()=>offset=Math.max(0,offset-pageSize)}>Προηγούμενη</button>
      <button type="button" class="admin-secondary" disabled={loading || offset+pageSize>=page.total}
        onclick={()=>offset+=pageSize}>Επόμενη</button>
    </nav>
  {/if}
</section>

<style>
  .coach-directory{margin:16px 0}
  .coach-heading{display:flex;align-items:start;justify-content:space-between;flex-wrap:wrap;gap:12px}
  .coach-heading h2{margin:0 0 7px}
  .coach-heading .admin-muted{margin:0;max-width:80ch}
  .coach-count{font-family:'Fira Sans Extra Condensed',sans-serif;color:#ffca46;font-size:22px;margin:13px 0}
  .coach-list{padding:0;margin:0;list-style:none}
  .coach-list li{display:grid;grid-template-columns:1fr 1.5fr;gap:16px;align-items:center;min-height:62px;border-top:1px solid #303335;padding:12px 0}
  .coach-list li>div{display:grid;gap:5px}
  .coach-list strong{font-size:14px;overflow-wrap:anywhere}
  .coach-list span{font-size:12px;color:#b9bcbe;overflow-wrap:anywhere}
  .coach-paging{display:flex;justify-content:flex-end;align-items:center;gap:9px;flex-wrap:wrap;padding-top:14px}
  .coach-paging span{margin-right:auto}
  @media(max-width:600px){.coach-list li{grid-template-columns:1fr}.coach-heading{display:grid}}
</style>
