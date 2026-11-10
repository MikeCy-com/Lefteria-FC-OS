<script lang="ts">
  import {browser} from '$app/env';

  type Owner='lefteria_fc'|'lefteria_fc_academy';
  type Register={
    id:string;label:string;location_id:string;location_name:string;
    shift_id:string|null;opened_at:string|null;opening_cash_cents:number|null;
  };
  type Response={organization_id:Owner;total:number;registers:Register[]};
  let {owner,actorId,onExpired,onRevoked}:{
    owner:Owner;actorId:string;onExpired:()=>void;onRevoked:()=>void
  }=$props();

  let catalog=$state<Response|null>(null);
  let loading=$state(false);
  let writing=$state(false);
  let error=$state('');
  let notice=$state('');
  let revision=$state(0);
  let selected=$state('');
  let opening=$state('0.00');
  let counted=$state('');
  let confirmedClose=$state(false);
  let lastScope='';
  let pendingOpenId=$state('');
  let pendingCloseId=$state('');
  const euros=(cents:number)=>new Intl.NumberFormat('el-CY',
    {style:'currency',currency:'EUR'}).format(cents/100);
  const money=(value:string):number|null=>{
    if(!/^\d{1,6}(?:[.,]\d{1,2})?$/.test(value))return null;
    const [whole,decimal='']=value.replace(',','.').split('.');
    const result=Number(whole)*100+Number(decimal.padEnd(2,'0'));
    return Number.isSafeInteger(result)&&result<=100000000?result:null;
  };
  const selectedRegister=$derived(catalog?.registers.find(r=>r.id===selected));
  const validRegister=(r:unknown):r is Register=>{
    if(!r||typeof r!=='object'||Array.isArray(r))return false;
    const item=r as Record<string,unknown>;
    return typeof item.id==='string'&&/^[A-Za-z0-9_-]{1,80}$/.test(item.id)&&
      typeof item.label==='string'&&item.label.length<=140&&
      typeof item.location_id==='string'&&item.location_id.length<=80&&
      typeof item.location_name==='string'&&item.location_name.length<=140&&
      (item.shift_id===null || typeof item.shift_id==='string'&&
        /^[0-9a-f-]{36}$/.test(item.shift_id))&&
      (item.opened_at===null||typeof item.opened_at==='string'&&
        !Number.isNaN(Date.parse(item.opened_at)))&&
      (item.opening_cash_cents===null||
        Number.isSafeInteger(item.opening_cash_cents)&&
        (item.opening_cash_cents as number)>=0);
  };
  function verified(payload:unknown,expected:Owner):payload is Response{
    if(!payload||typeof payload!=='object'||Array.isArray(payload))return false;
    const v=payload as Record<string,unknown>;
    return v.organization_id===expected&&Number.isSafeInteger(v.total)&&
      (v.total as number)>=0&&(v.total as number)<=100&&
      Array.isArray(v.registers)&&v.registers.length===v.total&&
      v.registers.every(validRegister);
  }
  $effect(()=>{
    const currentOwner=owner,currentActor=actorId;
    void revision;
    catalog=null;loading=false;selected='';confirmedClose=false;
    error='';pendingOpenId='';pendingCloseId='';
    const contextKey=currentOwner+':'+currentActor;
    if(contextKey!==lastScope){notice='';lastScope=contextKey;}
    if(!browser||!currentActor)return;
    const controller=new AbortController();
    loading=true;
    void(async()=>{
      try{
        const q=new URLSearchParams({organization_id:currentOwner});
        const response=await fetch('/api/admin/pos/cashier/registers?'+q,{
          cache:'no-store',credentials:'same-origin',signal:controller.signal
        });
        if(controller.signal.aborted)return;
        if(response.status===401){onExpired();return;}
        if(response.status===403){onRevoked();return;}
        if(!response.ok)throw Error(response.status===503
          ? 'Τα ταμεία δεν έχουν ενεργοποιηθεί ακόμη.'
          : 'Μη διαθέσιμα ταμεία.');
        const data:unknown=await response.json();
        if(!verified(data,currentOwner))throw Error('Μη επαληθευμένα ταμεία.');
        if(!controller.signal.aborted)catalog=data;
      }catch(e){
        if(!controller.signal.aborted)error=e instanceof Error?e.message:'Σφάλμα ταμείου.';
      }finally{if(!controller.signal.aborted)loading=false;}
    })();
    return()=>controller.abort();
  });
  async function submitShift(close:boolean){
    if(!browser||writing||!selectedRegister||!catalog)return;
    const register=selectedRegister,ownerAtStart=owner,actorAtStart=actorId;
    if(close&&!confirmedClose)return;
    const cents=money(close?counted:opening);
    if(cents===null){error='Ελέγξτε το ποσό σε ευρώ με έως δύο δεκαδικά.';return;}
    const id=close?(pendingCloseId||crypto.randomUUID()):
      (pendingOpenId||crypto.randomUUID());
    if(close)pendingCloseId=id;else pendingOpenId=id;
    const shiftId=register.shift_id;
    if(close&&!shiftId)return;
    const path=close
      ?'/api/admin/pos/cashier/shifts/'+encodeURIComponent(shiftId!)+'/close'
      :'/api/admin/pos/cashier/registers/'+encodeURIComponent(register.id)+'/shifts';
    writing=true;error='';notice='';
    try{
      const response=await fetch(path+'?'+new URLSearchParams({organization_id:ownerAtStart}),{
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify(close?
          {request_id:id,counted_cash_cents:cents}:
          {request_id:id,opening_cash_cents:cents})
      });
      if(owner!==ownerAtStart||actorId!==actorAtStart)return;
      if(response.status===401){onExpired();return;}
      if(response.status===403){onRevoked();return;}
      if(!response.ok)throw Error(response.status===409
        ?'Το ταμείο άλλαξε ή δεν συμφωνούν οι συναλλαγές. Ανανέωση απαιτείται.'
        :'Η πράξη ταμείου δεν ολοκληρώθηκε.');
      const raw:unknown=await response.json();
      if(!raw||typeof raw!=='object'||Array.isArray(raw)||
        (raw as Record<string,unknown>).organization_id!==ownerAtStart||
        typeof (raw as Record<string,unknown>).shift_id!=='string')
        throw Error('Μη επαληθευμένη απόκριση ταμείου.');
      if(close)pendingCloseId='';else pendingOpenId='';
      confirmedClose=false;
      notice=close?'Η βάρδια έκλεισε. Η καταμέτρηση καταγράφηκε.':
        'Η βάρδια άνοιξε.';
      revision++;
    }catch(e){
      error=e instanceof Error?e.message:'Αποτυχία πράξης ταμείου.';
    }finally{writing=false;}
  }
</script>
<section class="admin-panel pos-cashier-shifts" data-testid="admin-pos-cashier-shifts">
  <p class="admin-eyebrow">ΤΑΜΕΙΑ · ΔΙΑΧΕΙΡΙΣΗ ΒΑΡΔΙΩΝ</p>
  <h2>Ταμεία & βάρδιες</h2>
  <p class="admin-muted">Μόνο οι ενεργές ταμειακές θέσεις που σας έχουν ανατεθεί. Η έναρξη βάρδιας δεν ενεργοποιεί πωλήσεις ή εισπράξεις.</p>
  {#if loading}<p role="status">Έλεγχος ταμείων…</p>{/if}
  {#if error}<p role="alert">{error}</p>{/if}
  {#if catalog}
    {#if catalog.total===0}
      <p class="admin-muted">Δεν υπάρχει ενεργό ταμείο ανατεθειμένο στον λογαριασμό σας.</p>
    {:else}
      <label>Ταμείο
        <select bind:value={selected} onchange={()=>{
          pendingOpenId='';pendingCloseId='';confirmedClose=false;counted='';
        }}>
          <option value="">Επιλέξτε ταμείο</option>
          {#each catalog.registers as register (register.id)}
            <option value={register.id}>{register.label} · {register.location_name}</option>
          {/each}
        </select>
      </label>
      {#if selectedRegister}
        {#if selectedRegister.shift_id}
          <p>Ανοικτή βάρδια · {selectedRegister.opened_at
            ?new Date(selectedRegister.opened_at).toLocaleString('el-CY'):'—'}</p>
          <p>Αρχικό ποσό: {euros(selectedRegister.opening_cash_cents??0)}</p>
          <label>Μετρημένα μετρητά κατά το κλείσιμο (€)
            <input type="text" inputmode="decimal" bind:value={counted}
              placeholder="0.00" maxlength="10" oninput={()=>pendingCloseId=''} />
          </label>
          <label class="pos-cashier-confirm">
            <input type="checkbox" bind:checked={confirmedClose}/>
            Επιβεβαιώνω ότι καταμέτρησα το ταμείο.
          </label>
          <button type="button" class="admin-secondary"
            disabled={writing||!confirmedClose||money(counted)===null}
            onclick={()=>void submitShift(true)}>Κλείσιμο βάρδιας</button>
        {:else}
          <label>Αρχικά μετρητά (€)
            <input type="text" inputmode="decimal" bind:value={opening}
              maxlength="10" oninput={()=>pendingOpenId=''}/>
          </label>
          <button type="button" class="admin-secondary"
            disabled={writing||money(opening)===null}
            onclick={()=>void submitShift(false)}>Άνοιγμα βάρδιας</button>
        {/if}
      {/if}
    {/if}
  {/if}
  {#if notice}<p role="status">{notice}</p>{/if}
  <p class="admin-muted">Δεν υπάρχει ακόμη ενεργή πώληση, επαλήθευση Payabl ή επίσημη έκδοση απόδειξης από αυτή την οθόνη.</p>
</section>
<style>
.pos-cashier-shifts{display:grid;gap:12px;margin:16px 0}
.pos-cashier-shifts label:not(.pos-cashier-confirm){display:grid;gap:6px;max-width:450px}
.pos-cashier-shifts select,.pos-cashier-shifts input[type=text]{width:100%}
.pos-cashier-confirm{display:flex;gap:8px;align-items:center}
</style>
