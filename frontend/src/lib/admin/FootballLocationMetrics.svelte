<script lang="ts">
  import {browser} from '$app/environment';

  let {organization,cityId,locationId,actorId,revision=0,canManageCity=false}:{
    organization:'lefteria_fc_academy';
    cityId:string; locationId:string; actorId:string; revision?:number;
    canManageCity?:boolean;
  }=$props();

  type Metric={city_id:string; location_id:string; active_players:number; groups:number};
  let metrics=$state<Metric[]>([]);
  let loading=$state(false);
  let error=$state('');

  $effect(()=>{
    const owner=organization, actor=actorId, refresh=revision;
    void refresh;
    metrics=[]; error=''; loading=false;
    if(!browser || !actor) return;
    const abort=new AbortController();
    loading=true;
    void (async()=>{
      try{
        const response=await fetch('/api/admin/football/territories/metrics?organization_id='+
          encodeURIComponent(owner),{
          credentials:'same-origin',cache:'no-store',
          headers:{Accept:'application/json'},signal:abort.signal
        });
        if(!response.ok) throw new Error('Τα στατιστικά δεν είναι διαθέσιμα.');
        const data:unknown=await response.json();
        if(!data || typeof data!=='object'||Array.isArray(data)||
          (data as Record<string,unknown>).organization_id!==owner ||
          !Array.isArray((data as Record<string,unknown>).locations))
          throw new Error('Μη έγκυρη απάντηση στατιστικών.');
        const locations=(data as {locations:unknown[]}).locations;
        if(locations.length>3000 || !locations.every((row):row is Metric=>
          !!row && typeof row==='object' && !Array.isArray(row) &&
          typeof (row as Metric).city_id==='string' &&
          typeof (row as Metric).location_id==='string' &&
          Number.isSafeInteger((row as Metric).active_players) &&
          (row as Metric).active_players>=0 &&
          Number.isSafeInteger((row as Metric).groups) &&
          (row as Metric).groups>=0))
          throw new Error('Μη έγκυρα δεδομένα στατιστικών.');
        if(!abort.signal.aborted && actorId===actor && organization===owner)
          metrics=locations as Metric[];
      }catch(e){
        if(!abort.signal.aborted) error=e instanceof Error?e.message:'Αποτυχία φόρτωσης.';
      }finally{
        if(!abort.signal.aborted)loading=false;
      }
    })();
    return ()=>abort.abort();
  });
  const active=$derived(metrics.find(row=>row.city_id===cityId && row.location_id===locationId));
  const city=$derived(metrics.filter(row=>row.city_id===cityId));
  const cityPlayers=$derived(city.reduce((sum,row)=>sum+row.active_players,0));
  const cityGroups=$derived(city.reduce((sum,row)=>sum+row.groups,0));
</script>

<div class="territory-metrics" data-testid="territory-location-private-metrics">
  <h3>Στατιστικά τοποθεσίας</h3>
  <p class="metric-description">Επαληθευμένες ενεργές εγγραφές από τα τμήματα αυτής της τοποθεσίας.
    Χωρίς προσωπικά στοιχεία αθλητών ή οικονομικά δεδομένα.</p>
  {#if loading}
    <p role="status">Φόρτωση στοιχείων τοποθεσίας…</p>
  {:else if error}
    <p role="alert">{error}</p>
  {:else if active}
    <div class="metric-grid">
      <div><strong>{active.active_players}</strong><span>Ενεργοί αθλητές</span></div>
      <div><strong>{active.groups}</strong><span>Τμήματα</span></div>
    </div>
  {:else}
    <p>Δεν υπάρχουν διαθέσιμα επαληθευμένα στοιχεία για την επιλεγμένη τοποθεσία.</p>
  {/if}
  {#if canManageCity && !loading && !error && city.length>0}
    <h4>Πόλη · διαθέσιμες τοποθεσίες ({city.length})</h4>
    <div class="metric-grid">
      <div><strong>{cityPlayers}</strong><span>Εγγραφές αθλητών σε τοποθεσίες</span></div>
      <div><strong>{cityGroups}</strong><span>Τμήματα πόλης</span></div>
    </div>
    <small>Οι εγγραφές είναι ανά τοποθεσία· αθλητές που ανήκουν σε δύο
      τοποθεσίες μπορεί να καταμετρώνται και στις δύο.</small>
  {/if}
</div>

<style>
  .territory-metrics{margin:1.2rem 0;padding:1rem;border:1px solid rgba(173,137,93,.4);
    border-radius:1rem}
  .metric-description{font-size:.92rem;max-width:65ch}
  .metric-grid{display:flex;flex-wrap:wrap;gap:.85rem;margin:.75rem 0}
  .metric-grid div{display:grid;gap:.25rem;padding:1rem;flex:1 1 130px;
    border:1px solid rgba(173,137,93,.45);border-radius:.6rem}
  .metric-grid strong{font-size:2rem;letter-spacing:-.04em}
  .metric-grid span{font-size:.85rem}
  h3,h4{margin:.35rem 0}
  [role="alert"]{font-weight:700}
</style>
