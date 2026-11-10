<script lang="ts">
  import {untrack} from 'svelte';
  import './mobile-theme.css';
  type Invitation={id:string;organization_id:string;status:'pending';expires_at:string};
  type Inbox={items:Invitation[]};
  let {inbox,status,error,busy=false,workingId=null,success=null,onLoad,onRespond}:{
    inbox:Inbox|null;status:'idle'|'loading'|'ready'|'error';error:string|null;
    busy?:boolean;workingId?:string|null;success?:string|null;
    onLoad:()=>boolean|Promise<boolean>;
    onRespond:(id:string,accept:boolean)=>boolean|Promise<boolean>;
  }=$props();
  let confirming=$state<{id:string;accept:boolean}|null>(null);
  const label=(organization:string)=>organization==='lefteria_fc_academy'?
    'Ακαδημία Λευτεριά FC':'Πρώτη ομάδα Λευτεριά FC';
  $effect(()=>{
    if(status==='idle'&&!busy)untrack(()=>{void onLoad();});
  });
  async function confirm(){
    if(!confirming||workingId||busy)return;
    if(await onRespond(confirming.id,confirming.accept))confirming=null;
  }
</script>

<section class="guardian-consent" data-testid="mobile-guardian-consent">
  <h2>Προσκλήσεις Γονέα / Κηδεμόνα</h2>
  <p>Αν έχεις προσκληθεί ως δεύτερος γονέας, έλεγξε το αίτημα
    και επίλεξε αποδοχή ή απόρριψη.</p>
  {#if status==='loading'||status==='idle'}
    <p role="status">Έλεγχος προσκλήσεων…</p>
  {:else if status==='error'}
    <p role="alert">{error||'Δεν είναι διαθέσιμες οι προσκλήσεις κηδεμόνα.'}</p>
    <button type="button" onclick={()=>void onLoad()} disabled={busy}>Επανάληψη</button>
  {:else if inbox?.items.length===0}
    <p>Δεν υπάρχουν εκκρεμείς προσκλήσεις κηδεμόνα.</p>
  {:else if inbox}
    <div class="guardian-consent-list">
      {#each inbox.items as item (item.id)}
        <article class="guardian-consent-row">
          <strong>{label(item.organization_id)}</strong>
          <p>Πρόσκληση για δικαιώματα γονέα ·
            λήγει {new Intl.DateTimeFormat('el-CY',
              {dateStyle:'medium',timeZone:'Asia/Nicosia'}).format(
                new Date(item.expires_at))}</p>
          <p>Η επιβεβαίωση καταγράφει τη συγκατάθεσή σου.
            Δεν ενεργοποιεί ακόμη πρόσβαση σε προσωπικά στοιχεία παιδιών.</p>
          {#if confirming?.id===item.id}
            <div class="guardian-consent-actions">
              <p>Επιβεβαιώνεις την {confirming.accept?'αποδοχή':'απόρριψη'} της πρόσκλησης;</p>
              <button type="button" disabled={!!workingId||busy}
                onclick={()=>confirming=null}>Όχι</button>
              <button type="button" disabled={!!workingId||busy}
                onclick={()=>void confirm()}>
                {workingId===item.id?'Αναμονή…':'Ναι, επιβεβαίωση'}
              </button>
            </div>
          {:else}
            <div class="guardian-consent-actions">
              <button type="button" disabled={!!workingId||busy}
                onclick={()=>confirming={id:item.id,accept:false}}>
                Απόρριψη
              </button>
              <button type="button" disabled={!!workingId||busy}
                onclick={()=>confirming={id:item.id,accept:true}}>
                Αποδοχή
              </button>
            </div>
          {/if}
        </article>
      {/each}
    </div>
  {/if}
  {#if success}<p role="status">{success}</p>{/if}
  {#if error&&status==='ready'}<p role="alert">{error}</p>{/if}
</section>
<style>
.guardian-consent{margin:18px 0;padding:20px;border:1px solid #3e3427;
  border-radius:9px;background:#151615;color:#f4f4f4}
.guardian-consent h2{font-family:'Fira Sans Extra Condensed',sans-serif;
  font-size:24px;color:#ffca46}
.guardian-consent p{font-size:13px;line-height:1.5}
.guardian-consent-list{display:grid;gap:12px;margin-top:14px}
.guardian-consent-row{background:#201d16;border:1px solid #66502b;
  padding:16px;border-radius:8px}
.guardian-consent-actions{display:flex;flex-wrap:wrap;gap:10px;margin-top:10px}
.guardian-consent-actions button,.guardian-consent button{min-height:44px;
  padding:9px 14px;border:1px solid #b78933;border-radius:6px;
  background:#342713;color:#ffca46}
.guardian-consent button:disabled{opacity:.55;cursor:wait}
.guardian-consent button:focus-visible{outline:2px solid #ffca46;outline-offset:3px}
</style>
