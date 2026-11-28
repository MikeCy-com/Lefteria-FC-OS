<script lang="ts">
  import {browser} from '$app/env';
  let {actorId}:{actorId:string}=$props();
  type Player={id:string;name:string;state:'archived'|'transferred'|'review';groups:string[]};
  let open=$state(false),search=$state(''),applied=$state(''),offset=$state(0),total=$state(0);
  let rows=$state<Player[]>([]),loading=$state(false),error=$state(''),revision=$state(0);
  const limit=25;
  function verify(raw:unknown,skip:number):raw is {organization_id:string;offset:number;limit:number;total:number;players:Player[]}{
    if(!raw||typeof raw!=='object'||Array.isArray(raw))return false;
    const r=raw as Record<string,unknown>;
    return r.organization_id==='lefteria_fc_academy'&&r.offset===skip&&r.limit===limit&&
      Number.isSafeInteger(r.total)&&Number(r.total)>=0&&Array.isArray(r.players)&&r.players.length<=limit&&
      r.players.every(p=>p&&typeof p.id==='string'&&p.id.length>0&&p.id.length<=128&&typeof p.name==='string'&&
        ['archived','transferred','review'].includes(p.state)&&Array.isArray(p.groups)&&p.groups.every((g:unknown)=>typeof g==='string'));
  }
  $effect(()=>{
    const actor=actorId,term=applied,skip=offset;void revision;
    rows=[];total=0;error='';loading=false;
    if(!browser||!open||!actor)return;
    const controller=new AbortController();loading=true;
    void(async()=>{
      try{
        const query=new URLSearchParams({organization_id:'lefteria_fc_academy',search:term,offset:String(skip),limit:String(limit)});
        const response=await fetch('/api/admin/football/players/archived?'+query,{credentials:'same-origin',cache:'no-store',signal:controller.signal});
        if(!response.ok)throw Error('Το αρχείο δεν είναι διαθέσιμο ή η πρόσβαση έχει ανακληθεί.');
        const raw:unknown=await response.json();
        if(controller.signal.aborted||actorId!==actor||!open)return;
        if(!verify(raw,skip))throw Error('Μη επαληθευμένο αρχείο αθλητών.');
        rows=raw.players;total=raw.total;
      }catch(e){if(!controller.signal.aborted&&actorId===actor)error=e instanceof Error?e.message:'Σφάλμα.';}
      finally{if(!controller.signal.aborted&&actorId===actor)loading=false;}
    })();
    return()=>controller.abort();
  });
  function submit(e:SubmitEvent){e.preventDefault();applied=search.trim().slice(0,100);offset=0;revision++;}
</script>
<section class="admin-panel archive-directory" data-testid="admin-archived-player-directory">
  <h2>Αρχείο αθλητών Ακαδημίας</h2>
  <p>Αναζητήστε υπάρχοντα μη ενεργό αθλητή πριν δημιουργήσετε νέα εγγραφή. Το αρχείο δεν εμφανίζεται στο ενεργό ρόστερ.</p>
  <button type="button" class="admin-secondary" aria-expanded={open} onclick={()=>open=!open}>{open?'Κλείσιμο αρχείου':'Άνοιγμα αρχείου'}</button>
  {#if open}
    <form onsubmit={submit}><label>Όνομα ή ID αθλητή <input type="search" bind:value={search} maxlength="100"/></label>
      <button type="submit" class="admin-secondary">Αναζήτηση</button></form>
    {#if loading}<p role="status">Φόρτωση…</p>
    {:else if error}<p role="alert">{error}</p>
    {:else}
      <p>{total} εγγραφές</p>
      {#each rows as row (row.id)}
        <article class="archive-row"><div><strong>{row.name}</strong><p>{row.state==='archived'?'Αρχειοθετημένος':row.state==='transferred'?'Μεταγραφή':'Απαιτείται έλεγχος'} · {row.groups.join(', ')||'Χωρίς τμήμα'}</p><small>{row.id}</small></div>
          <a class="admin-secondary" href={'/admin/finance?'+new URLSearchParams({organization_id:'lefteria_fc_academy',player_id:row.id})}>Έλεγχος κατάστασης & οικονομικών ›</a>
        </article>
      {:else}<p>Δεν βρέθηκαν εγγραφές.</p>{/each}
      <div class="archive-paging"><button type="button" class="admin-secondary" disabled={offset===0} onclick={()=>offset=Math.max(0,offset-limit)}>Προηγούμενα</button>
        <button type="button" class="admin-secondary" disabled={offset+limit>=total||offset+limit>20000} onclick={()=>offset+=limit}>Επόμενα</button></div>
    {/if}
  {/if}
</section>
<style>
.archive-directory{display:grid;gap:12px}
.archive-directory>button{justify-self:start}
.archive-directory form{display:flex;gap:8px;align-items:end;flex-wrap:wrap}
.archive-directory label{display:grid;gap:5px}
.archive-row{display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap;padding:12px;border:1px solid var(--admin-line);border-radius:8px}
.archive-row p{margin:4px 0}
.archive-paging{display:flex;gap:8px}
</style>
