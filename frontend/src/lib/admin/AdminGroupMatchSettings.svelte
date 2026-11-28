<script lang="ts">
  import {onMount,getContext} from 'svelte';
  import {ADMIN_AUTH_CONTEXT,type AdminAuthController} from './controller.ts';
  import {validFormat,validGroupMatchSettings,type GroupMatchSettings} from '../mobile/match-timing.ts';
  let {organization,groupId}:{organization:'lefteria_fc'|'lefteria_fc_academy';groupId:string}=$props();
  const auth=getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  let settings=$state<GroupMatchSettings|null>(null),duration=$state(''),format=$state('');
  let busy=$state(true),error=$state(''),notice=$state('');
  let alive=false,request=0;
  const path=$derived('/api/admin/football/territories?organization_id='+encodeURIComponent(organization));
  function apply(value:GroupMatchSettings){
    settings=value;duration=value.duration_minutes===null?'':String(value.duration_minutes);
    format=organization==='lefteria_fc'?'11':value.players_on_field===null?'':String(value.players_on_field);
  }
  async function load(){
    const marker=++request;settings=null;duration='';format='';busy=true;error='';notice='';
    try{
      const response=await fetch(path+'&match_settings_group_id='+encodeURIComponent(groupId),{credentials:'same-origin',cache:'no-store'});
      if(!alive||marker!==request)return;
      if(response.status===401){auth.invalidateSession();return;}
      if(response.status===403){void auth.bootstrap();throw Error('Δεν υπάρχει δικαίωμα ρύθμισης για αυτό το τμήμα.');}
      if(!response.ok)throw Error('Οι ρυθμίσεις αγώνα δεν είναι διαθέσιμες.');
      const raw:unknown=await response.json();
      if(!alive||marker!==request)return;
      if(!validGroupMatchSettings(raw,organization,groupId))throw Error('Οι ρυθμίσεις δεν επαληθεύτηκαν.');
      apply(raw);
    }catch(e){if(alive&&marker===request)error=e instanceof Error?e.message:'Αποτυχία φόρτωσης.';}
    finally{if(alive&&marker===request)busy=false;}
  }
  onMount(()=>{alive=true;void load();return()=>{alive=false;request++;};});
  async function save(){
    if(!alive||busy||!settings)return;
    const minutes=Number(duration),players=Number(format),version=settings.version;
    if(!/^\d{1,3}$/.test(duration)||!Number.isInteger(minutes)||minutes<1||minutes>180||
      !validFormat(players)||(organization==='lefteria_fc'&&players!==11)){
      error='Επίλεξε διάρκεια 1–180 λεπτών και έγκυρη μορφή αγώνα.';return;
    }
    const marker=++request;busy=true;notice='';error='';
    try{
      const response=await fetch(path,{method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({kind:'match_settings',data:{host_id:groupId,expected_version:version,
          duration_minutes:minutes,players_on_field:players}})});
      if(!alive||marker!==request)return;
      if(response.status===401){auth.invalidateSession();settings=null;return;}
      if(response.status===403){void auth.bootstrap();throw Error('Το δικαίωμα ρύθμισης άλλαξε.');}
      if(!response.ok)throw Error(response.status===409?'Οι ρυθμίσεις άλλαξαν. Ανανέωσε πριν την αποθήκευση.':'Η αποθήκευση απέτυχε.');
      const raw:unknown=await response.json();
      if(!alive||marker!==request)return;
      if(!validGroupMatchSettings(raw,organization,groupId)||raw.version!==version+1||
        raw.duration_minutes!==minutes||raw.players_on_field!==players)throw Error('Η αποθήκευση δεν επαληθεύτηκε.');
      apply(raw);notice='Οι ρυθμίσεις αποθηκεύτηκαν για τους επόμενους αγώνες.';
    }catch(e){if(alive&&marker===request){settings=null;error=e instanceof Error?e.message:'Η αποθήκευση δεν επιβεβαιώθηκε.';}}
    finally{if(alive&&marker===request)busy=false;}
  }
</script>
<section class="match-settings" data-testid="admin-group-match-settings">
  <header><div><p class="admin-eyebrow">ΡΥΘΜΙΣΕΙΣ · ΑΓΩΝΕΣ</p><h4>Διάρκεια & μορφή αγώνα</h4></div>
    <button class="admin-secondary" type="button" disabled={busy} onclick={()=>void load()}>Ανανέωση</button></header>
  {#if error}<p role="alert">{error}</p>{/if}
  {#if notice}<p role="status">{notice}</p>{/if}
  {#if busy&&!settings}<p role="status">Φόρτωση ρυθμίσεων…</p>{/if}
  {#if settings}
    {#if settings.version===0}<p class="admin-muted">Δεν έχουν οριστεί ρυθμίσεις για αυτό το τμήμα.</p>{/if}
    <form onsubmit={e=>{e.preventDefault();void save();}}>
      <label>Συνολική διάρκεια αγώνα (λεπτά)
        <input type="number" min="1" max="180" step="1" required disabled={busy} value={duration} oninput={e=>duration=e.currentTarget.value}/>
      </label>
      <label>Μορφή αγώνα
        <select bind:value={format} required disabled={busy||organization==='lefteria_fc'}>
          <option value="">Επίλεξε μορφή</option>
          {#each (organization==='lefteria_fc'?[11]:[5,7,9,11]) as n}
            <option value={String(n)}>{n} vs {n}</option>
          {/each}
        </select>
      </label>
      {#if organization==='lefteria_fc_academy'}<p class="admin-muted">Οι εναλλαγές υπολογίζονται αυτόματα την ημέρα του αγώνα από τους παρόντες παίκτες, τη διάρκεια και τη μορφή αγώνα. Δεν υπάρχει σταθερό διάστημα εναλλαγής.</p>{/if}
      <p class="admin-muted">Οι νέοι αγώνες χρησιμοποιούν τις ρυθμίσεις του τμήματος. Οι ήδη οριστικοποιημένες ρυθμίσεις αγώνα δεν αλλάζουν αναδρομικά.</p>
      <button class="admin-primary" type="submit" disabled={busy}>Αποθήκευση ρυθμίσεων</button>
    </form>
  {/if}
</section>
<style>
  .match-settings{display:grid;gap:12px;padding:14px;border:1px solid #38332a;border-radius:8px;min-width:0}
  header{display:flex;align-items:center;justify-content:space-between;gap:8px;flex-wrap:wrap}
  h4{margin:0;font-size:17px}p{margin:0;line-height:1.5}form,label{display:grid;gap:8px}form{max-width:520px}
  label{font-size:13px;color:#ead099}input,select{min-height:44px;width:100%;box-sizing:border-box}
  button{justify-self:start;min-height:44px}
</style>
