<script lang="ts">
  import {browser} from '$app/env';
  let {playerId,actorId}:{playerId:string;actorId:string}=$props();
  type Review={organization_id:string;player_id:string;name:string;state:'active'|'archived'|'transferred'|'review';revision:number;identity_review_required:boolean;financial_review_required:boolean;can_change:boolean};
  let review=$state<Review|null>(null),loading=$state(false),busy=$state(false);
  let reason=$state(''),confirmation=$state(''),message=$state(''),refresh=$state(0);
  const scope=$derived(JSON.stringify([playerId,actorId]));
  const action=$derived(review?.state==='archived'?'reactivate':'archive');
  const confirmKey=$derived(JSON.stringify([scope,review?.revision,action,reason.trim()]));
  const url=$derived('/api/admin/football/players/'+encodeURIComponent(playerId)+'/private-profile/lifecycle?organization_id=lefteria_fc_academy');
  function valid(raw:unknown):raw is Review{
    if(!raw||typeof raw!=='object'||Array.isArray(raw))return false;
    const r=raw as Record<string,unknown>;
    return r.organization_id==='lefteria_fc_academy'&&r.player_id===playerId&&typeof r.name==='string'&&
      ['active','archived','transferred','review'].includes(String(r.state))&&
      Number.isSafeInteger(r.revision)&&Number(r.revision)>=0&&typeof r.identity_review_required==='boolean'&&
      typeof r.financial_review_required==='boolean'&&typeof r.can_change==='boolean'&&
      r.can_change===(!r.identity_review_required&&!r.financial_review_required&&['active','archived'].includes(String(r.state)));
  }
  $effect(()=>{
    const current=scope,endpoint=url;void refresh;
    review=null;confirmation='';loading=false;
    if(!browser||!playerId||!actorId||playerId.length>128)return;
    const controller=new AbortController();loading=true;
    void(async()=>{
      try{
        const response=await fetch(endpoint,{cache:'no-store',credentials:'same-origin',signal:controller.signal});
        if(!response.ok)throw Error('Ο έλεγχος κατάστασης δεν είναι διαθέσιμος.');
        const raw:unknown=await response.json();
        if(!controller.signal.aborted&&scope===current){
          if(!valid(raw))throw Error('Μη επαληθευμένη κατάσταση αθλητή.');
          review=raw;
        }
      }catch(e){if(!controller.signal.aborted&&scope===current)message=e instanceof Error?e.message:'Σφάλμα.';}
      finally{if(!controller.signal.aborted&&scope===current)loading=false;}
    })();
    return()=>controller.abort();
  });
  async function change(){
    const current=scope,expected=review?.revision,operation=action,why=reason.trim();
    if(!review?.can_change||expected===undefined||busy||confirmation!==confirmKey||why.length<8||why.length>240)return;
    busy=true;confirmation='';message='';
    try{
      const response=await fetch(url,{method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({action:operation,expectedRevision:expected,requestId:crypto.randomUUID(),reason:why})});
      if(!response.ok)throw Error('Η αλλαγή δεν επιβεβαιώθηκε. Απαιτείται νέος έλεγχος.');
      const raw=await response.json();
      if(scope!==current)return;
      if(raw.organization_id!=='lefteria_fc_academy'||raw.player_id!==playerId||
        raw.revision!==expected+1||raw.state!==(operation==='archive'?'archived':'active'))
        throw Error('Μη επαληθευμένη αλλαγή.');
      reason='';message='Η κατάσταση ενημερώθηκε. Τα ιστορικά στοιχεία διατηρήθηκαν.';
    }catch(e){if(scope===current)message=e instanceof Error?e.message:'Σφάλμα.';}
    finally{if(scope===current){busy=false;refresh++;}}
  }
</script>
<section class="admin-panel lifecycle" data-testid="admin-player-lifecycle-control">
  <h2>Αποχώρηση / επανενεργοποίηση αθλητή</h2>
  {#if loading}<p role="status">Έλεγχος κατάστασης…</p>{/if}
  {#if message}<p role="status">{message}</p>{/if}
  {#if review}
    <strong>{review.name} · {review.state==='active'?'Ενεργός':review.state==='archived'?'Αρχειοθετημένος':'Απαιτείται έλεγχος'}</strong>
    {#if review.identity_review_required}<p>Απαιτείται ξεχωριστός έλεγχος των συνδέσεων παίκτη και κηδεμόνων.</p>{/if}
    {#if review.financial_review_required}<p>Υπάρχουν ανεξόφλητες χρεώσεις ή οικονομικές εκκρεμότητες.</p>{/if}
    {#if review.can_change}
      <p>Διατηρούνται οι αναθέσεις, τα έγγραφα και τα ιστορικά στοιχεία. Η ενέργεια δεν δημιουργεί δικαιώματα πρόσβασης.</p>
      <label>Αιτιολογία <textarea bind:value={reason} maxlength="240" rows="2" disabled={busy}/></label>
      <label><input type="checkbox" checked={confirmation===confirmKey} disabled={busy||reason.trim().length<8}
        onchange={e=>confirmation=e.currentTarget.checked?confirmKey:''}/> Επιβεβαιώνω την αλλαγή για τον συγκεκριμένο αθλητή.</label>
      <button type="button" class="admin-secondary" disabled={busy||confirmation!==confirmKey} onclick={()=>void change()}>
        {action==='archive'?'Αρχειοθέτηση αθλητή':'Επανενεργοποίηση αθλητή'}
      </button>
    {/if}
  {/if}
  <button type="button" class="admin-secondary" disabled={loading||busy} onclick={()=>refresh++}>Νέος έλεγχος</button>
</section>
<style>
.lifecycle{display:grid;gap:12px}
.lifecycle label{display:grid;gap:6px}
.lifecycle label:has(input){display:flex;align-items:center}
.lifecycle button{justify-self:start}
</style>
