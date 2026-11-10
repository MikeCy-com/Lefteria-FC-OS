<script lang="ts">
  import {browser} from '$app/env';

  type Owner='lefteria_fc'|'lefteria_fc_academy';
  type ScoreSection='technical'|'tactical'|'physical'|'character';
  type Evaluation={id:string;revision:number;summary:string|null;notes:string|null;
    created_at:string;technical:Record<string,number>;tactical:Record<string,number>;
    physical:Record<string,number>;character:Record<string,number>;overall:Record<string,unknown>};
  type Document={id:string;document_type:string;title:string;
    expiry_date:string|null;status:string};
  let {owner,playerId,playerName,actorId,onExpired,onRevoked}:{
    owner:Owner;playerId:string;playerName:string;actorId:string;
    onExpired:()=>void;onRevoked:()=>void;
  }=$props();

  let evaluations=$state<Evaluation[]>([]);
  let documents=$state<Document[]>([]);
  let loading=$state(false);
  let error=$state('');
  let generation=$state(0);
  const sections:ScoreSection[]=['technical','tactical','physical','character'];
  const idValid=(id:string)=>id.length>0&&id.length<=128&&!/[\x00-\x1f\x7f]/.test(id);

  function verifiedList(value:unknown,type:'evaluations'|'documents',expectedOwner:Owner,expectedPlayer:string):value is Record<string,unknown> {
    if(!value||typeof value!=='object'||Array.isArray(value))return false;
    const x=value as Record<string,unknown>;
    if(x.organization_id!==expectedOwner||x.player_id!==expectedPlayer||
      !Number.isSafeInteger(x.total)||!Array.isArray(x[type])||
      (x.total as number)!==x[type].length||x[type].length>(type==='documents'?100:200))return false;
    return x[type].every((entry:unknown)=>{
      if(!entry||typeof entry!=='object'||Array.isArray(entry))return false;
      const row=entry as Record<string,unknown>;
      if(typeof row.id!=='string'||!/^[0-9a-f-]{36}$/.test(row.id))return false;
      if(type==='documents')return typeof row.title==='string'&&row.title.length<=200&&
        typeof row.document_type==='string'&&row.document_type.length<=32&&
        (row.expiry_date===null||typeof row.expiry_date==='string')&&
        ['no_expiry','expired','critical','warning','notice','watch','valid'].includes(String(row.status));
      return typeof row.created_at==='string'&&
        Number.isInteger(row.revision)&&(row.revision as number)>0&&
        (row.summary===null||typeof row.summary==='string'&&row.summary.length<=500)&&
        (row.notes===null||typeof row.notes==='string'&&row.notes.length<=4000)&&
        sections.every(s=>row[s]!==null&&typeof row[s]==='object'&&!Array.isArray(row[s]))&&
        row.overall!==null&&typeof row.overall==='object'&&!Array.isArray(row.overall);
    });
  }
  $effect(()=>{
    const currentOwner=owner,currentPlayer=playerId,currentActor=actorId;
    void generation;
    evaluations=[];documents=[];loading=false;error='';
    if(!browser||!idValid(currentPlayer)||!currentActor)return;
    const controller=new AbortController();
    loading=true;
    void (async()=>{
      try{
        const query=new URLSearchParams({organization_id:currentOwner});
        const prefix='/api/admin/club/players/'+encodeURIComponent(currentPlayer);
        const responses=await Promise.all(['evaluations','documents'].map(resource=>
          fetch(prefix+'/'+resource+'?'+query.toString(),{
            credentials:'same-origin',cache:'no-store',signal:controller.signal,
            headers:{Accept:'application/json'}
          })));
        if(controller.signal.aborted)return;
        if(responses.some(response=>response.status===401)){onExpired();return;}
        if(responses.some(response=>response.status===403)){onRevoked();return;}
        if(responses.some(response=>!response.ok))
          throw new Error('Οι ιδιωτικοί φάκελοι δεν είναι διαθέσιμοι.');
        const payloads:unknown[]=await Promise.all(responses.map(r=>r.json()));
        if(!verifiedList(payloads[0],'evaluations',currentOwner,currentPlayer)||
           !verifiedList(payloads[1],'documents',currentOwner,currentPlayer))
          throw new Error('Μη επαληθευμένη απόκριση ιδιωτικού φακέλου.');
        if(!controller.signal.aborted){
          evaluations=payloads[0].evaluations as Evaluation[];
          documents=payloads[1].documents as Document[];
        }
      }catch{
        if(!controller.signal.aborted){
          evaluations=[];documents=[];error='Η προβολή ιδιωτικού φακέλου απέτυχε.';
        }
      }finally{if(!controller.signal.aborted)loading=false;}
    })();
    return()=>controller.abort();
  });
</script>

<section class="private-player-card" aria-label={'Ιδιωτικός φάκελος '+playerName}
  data-testid="admin-player-private-records">
  <div class="private-player-card-heading">
    <h3>Ιδιωτικός φάκελος · {playerName}</h3>
    <button type="button" class="admin-secondary" onclick={()=>generation++} disabled={loading}>
      Ανανέωση
    </button>
  </div>
  <p class="admin-muted">Αξιολογήσεις και προστατευμένα έγγραφα της επιλεγμένης ομάδας. Μόνο ανάγνωση.</p>
  {#if loading}<p role="status">Ανάκτηση προστατευμένων στοιχείων…</p>{/if}
  {#if error}<p role="alert">{error}</p>{/if}
  {#if !loading&&!error}
    <div class="private-player-card-sections">
      <div>
        <h4>Αξιολογήσεις ({evaluations.length})</h4>
        {#each evaluations as evaluation (evaluation.id)}
          <details>
            <summary>{evaluation.summary??'Αξιολόγηση'} · {new Date(evaluation.created_at).toLocaleDateString('el-CY')}</summary>
            {#if evaluation.notes}<p>{evaluation.notes}</p>{/if}
            {#each sections as section}
              <p>{section}: {Object.entries(evaluation[section]).map(([key,value])=>key+': '+value).join(' · ')||'—'}</p>
            {/each}
          </details>
        {/each}
        {#if evaluations.length===0}<p>Δεν υπάρχουν αξιολογήσεις.</p>{/if}
      </div>
      <div>
        <h4>Έγγραφα ({documents.length})</h4>
        {#each documents as doc (doc.id)}
          <p><strong>{doc.title}</strong> · {doc.document_type}
            · {doc.expiry_date??'Χωρίς λήξη'} ({doc.status})
          </p>
        {/each}
        {#if documents.length===0}<p>Δεν υπάρχουν μεταδεδομένα εγγράφων.</p>{/if}
        <p class="admin-muted">Δεν επιτρέπεται λήψη αρχείων από αυτή την οθόνη.</p>
      </div>
    </div>
  {/if}
</section>
<style>
  .private-player-card{grid-column:1/-1;display:grid;gap:10px;border:1px solid #705c36;border-radius:12px;padding:16px;background:#191b1c}
  .private-player-card-heading{display:flex;justify-content:space-between;align-items:center;gap:12px}
  .private-player-card h3,.private-player-card h4{margin:0;color:#ffca46}
  .private-player-card p,.private-player-card summary{font-size:13px;overflow-wrap:anywhere}
  .private-player-card-sections{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:18px}
  .private-player-card details{padding:8px 0;border-bottom:1px solid #333}
</style>
