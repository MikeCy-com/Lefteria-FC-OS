<script lang="ts">
  import {browser} from '$app/env';
  type Owner='lefteria_fc'|'lefteria_fc_academy';
  let {owner,groupId,groupName,actorId,allowed,onExpired,onRevoked,onChanged}:{
    owner:Owner;groupId:string;groupName:string;actorId:string;allowed:boolean;
    onExpired:()=>void;onRevoked:()=>void;onChanged:()=>void;
  }=$props();
  let name=$state('');
  let editing=$state(false),saving=$state(false),error=$state(''),notice=$state('');
  const valid=$derived(name.trim()===name&&name.length>=1&&name.length<=120&&!/[\x00-\x1f\x7f]/.test(name));
  $effect(()=>{
    const current=groupName,who=actorId,id=groupId,org=owner,canEdit=allowed;
    void who;void id;void org;
    name=current;editing=false;error='';notice='';
    if(!canEdit)saving=false;
  });
  async function save(){
    if(!browser||!allowed||saving||!editing||!valid||name===groupName)return;
    const scope=groupId,org=owner,actor=actorId,expected=groupName,next=name;
    saving=true;error='';notice='';
    try{
      const response=await fetch('/api/admin/football/groups/'+encodeURIComponent(scope)+'/name?organization_id='+encodeURIComponent(org),{
        method:'PUT',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({expected_name:expected,name:next})
      });
      if(scope!==groupId||org!==owner||actor!==actorId||!allowed)return;
      if(response.status===401){onExpired();return;}
      if(response.status===403){onRevoked();return;}
      if(response.status===409)throw Error('Το όνομα χρησιμοποιείται ήδη ή άλλαξε. Ανανεώστε τον κατάλογο.');
      if(!response.ok)throw Error('Δεν επιβεβαιώθηκε η αλλαγή ονόματος.');
      const raw:unknown=await response.json();
      if(!raw||typeof raw!=='object'||Array.isArray(raw))throw Error('Μη επαληθευμένη απάντηση.');
      const result=raw as Record<string,unknown>;
      if(result.organization_id!==org||result.id!==scope||result.name!==next)
        throw Error('Μη επαληθευμένη αλλαγή ονόματος.');
      if(scope!==groupId||org!==owner||actor!==actorId||!allowed)return;
      editing=false;notice='Το όνομα αποθηκεύτηκε.';onChanged();
    }catch(e){if(scope===groupId&&org===owner&&actor===actorId&&allowed)error=e instanceof Error?e.message:'Αποτυχία αποθήκευσης.';}
    finally{saving=false;}
  }
</script>

{#if allowed}
  <section class="admin-panel group-name-editor" data-testid="admin-group-name-editor">
    <div class="group-name-heading">
      <div><h3>Όνομα ομάδας / τμήματος</h3><p>{groupName}</p></div>
      {#if !editing}<button type="button" class="admin-secondary" onclick={()=>{name=groupName;editing=true;error='';notice='';}}>Αλλαγή ονόματος</button>{/if}
    </div>
    {#if editing}
      <form onsubmit={event=>{event.preventDefault();void save();}}>
        <label>Νέο όνομα <input bind:value={name} maxlength="120" required disabled={saving}/></label>
        <p class="admin-muted">Οι αναθέσεις, οι παίκτες και το ιστορικό παραμένουν στο ίδιο αναγνωριστικό ομάδας.</p>
        <div class="group-name-actions">
          <button type="submit" class="admin-primary" disabled={saving||!valid||name===groupName}>{saving?'Αποθήκευση…':'Αποθήκευση ονόματος'}</button>
          <button type="button" class="admin-secondary" disabled={saving} onclick={()=>{editing=false;name=groupName;error='';}}>Ακύρωση</button>
        </div>
      </form>
    {/if}
    {#if error}<p role="alert">{error}</p>{/if}
    {#if notice}<p role="status">{notice}</p>{/if}
  </section>
{/if}
<style>
.group-name-editor,.group-name-editor form{display:grid;gap:12px;min-width:0}
.group-name-heading,.group-name-actions{display:flex;flex-wrap:wrap;justify-content:space-between;align-items:center;gap:12px}
.group-name-heading h3,.group-name-heading p{margin:0;overflow-wrap:anywhere}
.group-name-editor label{display:grid;gap:6px;max-width:640px}
.group-name-actions{justify-content:flex-start}
</style>
