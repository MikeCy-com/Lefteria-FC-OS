<script lang="ts">
  import {browser} from '$app/env';
  type Owner='lefteria_fc'|'lefteria_fc_academy';
  type City={id:string;name:string;active:boolean;version:number};
  type Location={id:string;city_id:string;name:string;code:string;
    active:boolean;version:number;
    products:{product_id:string;enabled:boolean;version:number;available_units:number}[]};
  type Directory={organization_id:Owner;cities:City[];locations:Location[]};
  let {owner,actorId,canWrite,onExpired,onRevoked}:{
    owner:Owner;actorId:string;canWrite:boolean;
    onExpired:()=>void;onRevoked:()=>void;
  }=$props();
  let directory=$state<Directory|null>(null);
  let loading=$state(false);
  let refresh=$state(0);
  let saving=$state(false);
  let error=$state('');
  let notice=$state('');
  let cityName=$state('');
  let locationName=$state('');
  let locationCity=$state('');
  let locationCode=$state('');
  let showCity=$state(false);
  let showLocation=$state(false);
  const validId=(x:unknown)=>typeof x==='string'&&/^[A-Za-z0-9_-]{1,80}$/.test(x);
  function verified(value:unknown,expected:Owner):value is Directory{
    if(!value||typeof value!=='object'||Array.isArray(value))return false;
    const x=value as Record<string,unknown>;
    if(x.organization_id!==expected||!Array.isArray(x.cities)||!Array.isArray(x.locations)||
      x.cities.length>100||x.locations.length>80)return false;
    const cities=x.cities.every((c:unknown)=>{
      if(!c||typeof c!=='object'||Array.isArray(c))return false;
      const row=c as Record<string,unknown>;
      return validId(row.id)&&typeof row.name==='string'&&row.name.length<=100&&
        typeof row.active==='boolean'&&Number.isSafeInteger(row.version);
    });
    const locations=x.locations.every((l:unknown)=>{
      if(!l||typeof l!=='object'||Array.isArray(l))return false;
      const row=l as Record<string,unknown>;
      return validId(row.id)&&validId(row.city_id)&&
        typeof row.code==='string'&&/^[a-z0-9][a-z0-9-]{1,63}$/.test(row.code)&&
        typeof row.name==='string'&&row.name.length<=100&&
        typeof row.active==='boolean'&&Number.isSafeInteger(row.version)&&
        Array.isArray(row.products)&&row.products.length<=250&&
        row.products.every((p:unknown)=>{
          if(!p||typeof p!=='object'||Array.isArray(p))return false;
          const item=p as Record<string,unknown>;
          return validId(item.product_id)&&typeof item.enabled==='boolean'&&
            Number.isSafeInteger(item.version)&&Number.isSafeInteger(item.available_units)&&
            (item.available_units as number)>=0;
        });
    });
    return cities&&locations;
  }
  $effect(()=>{
    const currentOwner=owner,actor=actorId;
    void refresh;void actor;
    directory=null;error='';notice='';loading=false;
    if(!browser||!currentOwner||!actor)return;
    const controller=new AbortController();
    loading=true;
    void (async()=>{
      try{
        const response=await fetch('/api/admin/pos/locations?'+
          new URLSearchParams({organization_id:currentOwner}),{
          credentials:'same-origin',cache:'no-store',signal:controller.signal,
          headers:{Accept:'application/json'}
        });
        if(controller.signal.aborted)return;
        if(response.status===401){onExpired();return;}
        if(response.status===403){onRevoked();return;}
        if(!response.ok)throw new Error('Οι τοποθεσίες POS δεν είναι διαθέσιμες.');
        const raw:unknown=await response.json();
        if(!verified(raw,currentOwner))throw new Error('Μη επαληθευμένο μητρώο τοποθεσιών.');
        if(!controller.signal.aborted){
          directory=raw;
          if(!raw.cities.some(c=>c.id===locationCity && c.active))
            locationCity=raw.cities.find(c=>c.active)?.id??'';
        }
      }catch(e){
        if(!controller.signal.aborted)error=e instanceof Error?e.message:'Σφάλμα τοποθεσιών.';
      }finally{if(!controller.signal.aborted)loading=false;}
    })();
    return()=>controller.abort();
  });

  async function create(kind:'city'|'location'){
    if(!browser||!canWrite||!directory||saving||loading)return;
    const selectedOwner=owner,selectedActor=actorId;
    const name=kind==='city'?cityName:locationName;
    if(!name||name.length>100||name!==name.trim()||/[\x00-\x1f\x7f]/.test(name)){
      error='Ορίστε έγκυρο όνομα έως 100 χαρακτήρες.';return;
    }
    if(kind==='location'&&(!directory.cities.some(c=>c.id===locationCity&&c.active)||
      !/^[a-z0-9][a-z0-9-]{1,63}$/.test(locationCode))){
      error='Επιλέξτε ενεργή πόλη και έγκυρο μοναδικό κωδικό ταμείου.';return;
    }
    saving=true;error='';notice='';
    const body=kind==='city'?{name}:{city_id:locationCity,name,code:locationCode};
    try{
      const response=await fetch('/api/admin/pos/'+(kind==='city'?'cities':'locations')+'?'+
        new URLSearchParams({organization_id:selectedOwner}),{
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify(body)
      });
      if(owner!==selectedOwner||actorId!==selectedActor)return;
      if(response.status===401){onExpired();return;}
      if(response.status===403){onRevoked();return;}
      if(!response.ok)throw new Error(response.status===409?
        'Ο κωδικός υπάρχει ήδη. Επιλέξτε άλλον.':
        'Η δημιουργία δεν ολοκληρώθηκε.');
      const saved:unknown=await response.json();
      if(!saved||typeof saved!=='object'||Array.isArray(saved)||
        (saved as Record<string,unknown>).organization_id!==selectedOwner||
        (saved as Record<string,unknown>).name!==name||
        !validId((saved as Record<string,unknown>).id))
        throw new Error('Μη επαληθευμένη απάντηση.');
      if(kind==='city'){cityName='';showCity=false;}
      else{locationName='';locationCode='';showLocation=false;}
      notice='Η καταχώριση έγινε. Επαλήθευση καταλόγου…';
      refresh++;
    }catch(e){
      if(owner===selectedOwner&&actorId===selectedActor)
        error=e instanceof Error?e.message:'Αποτυχία αποθήκευσης.';
    }finally{saving=false;}
  }
</script>

<section class="admin-panel admin-pos-locations" aria-labelledby="admin-pos-locations-heading"
  data-testid="admin-pos-locations">
  <header class="pos-locations-head">
    <div>
      <p class="admin-eyebrow">POS · ΤΟΠΟΘΕΣΙΕΣ ΚΑΙ ΠΟΛΕΙΣ</p>
      <h2 id="admin-pos-locations-heading">Πόλεις & σημεία πώλησης</h2>
      <p class="admin-muted">Ιδιωτικό μητρώο σημείων. Η καταχώριση δεν ενεργοποιεί πωλήσεις, πρόσβαση χειριστών ή πληρωμές.</p>
    </div>
    <button type="button" class="admin-secondary" onclick={()=>refresh++} disabled={loading}>Ανανέωση</button>
  </header>
  {#if loading}<p role="status">Φόρτωση τοποθεσιών…</p>{/if}
  {#if error}<p role="alert" class="pos-error">{error}</p>{/if}
  {#if notice}<p role="status">{notice}</p>{/if}
  {#if directory}
    <div class="pos-locations-grid">
      <div>
        <h3>Πόλεις ({directory.cities.length})</h3>
        {#each directory.cities as city (city.id)}
          <p>{city.name} · {city.active?'Ενεργή':'Ανενεργή'}</p>
        {/each}
      </div>
      <div>
        <h3>Σημεία ({directory.locations.length})</h3>
        {#each directory.locations as location (location.id)}
          <article>
            <strong>{location.name}</strong>
            <p class="admin-muted">{location.code} · {directory.cities.find(c=>c.id===location.city_id)?.name??'—'}</p>
            <p class="admin-muted">Προϊόντα καταστήματος: {location.products.length}</p>
          </article>
        {/each}
      </div>
    </div>
    {#if canWrite}
      <div class="pos-location-tools">
        <button type="button" class="admin-secondary" disabled={saving}
          onclick={()=>showCity=!showCity}>Νέα πόλη</button>
        <button type="button" class="admin-secondary" disabled={saving||!directory.cities.some(c=>c.active)}
          onclick={()=>showLocation=!showLocation}>Νέο σημείο</button>
      </div>
      {#if showCity}
        <form class="pos-location-form" onsubmit={e=>{e.preventDefault();void create('city');}}>
          <label>Όνομα πόλης <input bind:value={cityName} required maxlength="100"/></label>
          <button class="admin-secondary" type="submit" disabled={saving}>Δημιουργία πόλης</button>
        </form>
      {/if}
      {#if showLocation}
        <form class="pos-location-form" onsubmit={e=>{e.preventDefault();void create('location');}}>
          <label>Πόλη
            <select bind:value={locationCity} required>
              {#each directory.cities.filter(c=>c.active) as city (city.id)}
                <option value={city.id}>{city.name}</option>
              {/each}
            </select>
          </label>
          <label>Όνομα σημείου <input bind:value={locationName} maxlength="100" required/></label>
          <label>Μοναδικός κωδικός <input bind:value={locationCode} maxlength="64" pattern="[a-z0-9][a-z0-9-]{1,63}" required/></label>
          <button class="admin-secondary" type="submit" disabled={saving}>Δημιουργία σημείου</button>
        </form>
      {/if}
    {/if}
  {/if}
</section>
<style>
  .admin-pos-locations{margin-top:18px}
  .pos-locations-head{display:flex;justify-content:space-between;align-items:start;gap:14px;flex-wrap:wrap}
  .pos-locations-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(230px,1fr));gap:20px}
  .pos-locations-grid article{padding:9px;border-top:1px solid #383838}
  .pos-location-tools{display:flex;gap:8px;flex-wrap:wrap;margin:15px 0}
  .pos-location-form{display:grid;gap:10px;margin:10px 0;padding:15px;border:1px solid #4a4a4a;border-radius:9px}
  .pos-location-form label{display:grid;gap:6px;font-size:13px}
  .pos-location-form input,.pos-location-form select{padding:9px;background:#171717;color:#fff;border:1px solid #545454;border-radius:8px}
  .pos-error{color:#e5a77b}
</style>
