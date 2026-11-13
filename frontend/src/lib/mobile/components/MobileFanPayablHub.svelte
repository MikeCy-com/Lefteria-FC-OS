<script lang="ts">
 import {onMount,untrack} from 'svelte';
 import type {MobileAuthController} from '../controller.ts';
 import type {FanHubOrganization,FanHubTrack,FanHubAudience,FanHubStatus,
   FanHubLessonPage} from '../fan-payabl.ts';
 import {validHostedPayablUrl} from '../fan-payabl.ts';
 import './mobile-theme.css';

 let {controller}:{controller:MobileAuthController}=$props();
 let organization=$state<FanHubOrganization>('lefteria_fc_academy');
 let track=$state<FanHubTrack>('parent');
 let audience=$state<FanHubAudience>('parent');
 let status=$state<FanHubStatus|null>(null);
 let lesson=$state<FanHubLessonPage|null>(null);
 let email=$state(''),firstName=$state(''),lastName=$state('');
 let consent=$state(false);
 let busy=$state(true),error=$state<string|null>(null);
 let alive=false,epoch=0;
 const active=$derived(status?.status==='active');
 const allowed=$derived(active&&
   (audience==='parent'?status?.audience_parent:status?.audience_player));

 onMount(()=>{
   alive=true;void refresh();
   return()=>{alive=false;epoch++;status=null;lesson=null;};
 });
 $effect(()=>{
   const owner=organization;
   if(alive)untrack(()=>void refresh(owner));
 });
 $effect(()=>{
   const a=audience;
   if(alive&&active)untrack(()=>void readLesson(a));
 });
 async function refresh(owner=organization){
   const token=++epoch;busy=true;error=null;status=null;lesson=null;
   try{
     const data=await controller.getFanHubSubscription(owner);
     if(alive&&epoch===token&&organization===owner){
       status=data;
       if(data.status==='active'){
         const selected=data.audience_parent?'parent':'player';
         audience=selected;
         await readLesson(selected);
       }
     }
   }catch{
     if(alive&&epoch===token)error='Δεν επαληθεύτηκε η συνδρομή HUB.';
   }finally{if(alive&&epoch===token)busy=false;}
 }
 async function readLesson(selected=audience){
   const owner=organization,token=++epoch;
   lesson=null;error=null;
   try{
     const data=await controller.getFanHubNextLesson(owner,selected);
     if(alive&&epoch===token&&organization===owner&&audience===selected)lesson=data;
   }catch{
     if(alive&&epoch===token)error='Το εγκεκριμένο μάθημα δεν είναι διαθέσιμο. Έλεγξε τη συνδρομή.';
   }
 }
 async function checkout(){
   if(busy||!consent)return;
   const token=++epoch,owner=organization;busy=true;error=null;lesson=null;
   try{
     const data=await controller.startFanHubCheckout(owner,track,
       email,firstName,lastName,consent);
     if(!alive||epoch!==token||organization!==owner)return;
     await refresh(owner);
     if(data.checkout_url&&validHostedPayablUrl(data.checkout_url)){
       // Redirect only after server returns a validated Payabl sandbox HPP.
       window.location.assign(data.checkout_url);
     }else if(data.status!=='active'){
       error='Υπάρχει ήδη αίτημα πληρωμής. Δεν θα ξεκινήσουμε δεύτερη χρέωση. Επικοινώνησε με τη διαχείριση.';
     }
   }catch{
     if(alive&&epoch===token)error='Η δοκιμαστική πληρωμή δεν ξεκίνησε. Δεν έγινε επιβεβαίωση χρέωσης.';
   }finally{if(alive&&epoch===token)busy=false;}
 }
 async function completeLesson(){
   if(!lesson?.next||busy)return;
   const token=++epoch,submitted=lesson;
   busy=true;error=null;lesson=null;
   try{
     await controller.completeFanHubLesson(submitted);
     if(alive&&epoch===token)await refresh();
   }catch{
     if(alive&&epoch===token)error='Το μάθημα δεν ολοκληρώθηκε. Ανανέωσε τη συνδρομή σου.';
   }finally{if(alive&&epoch===token)busy=false;}
 }
</script>

<section class="fan-hub" data-testid="fan-payabl-verified-hub">
 <header>
  <p class="kicker">LEFTERIA FC · FAN HUB</p>
  <h2>Η δική μου εκπαιδευτική διαδρομή</h2>
  <p>Κάθε συνδρομητής ξεκινά από το πρώτο εγκεκριμένο μάθημα, ανεξάρτητα από την ημερομηνία εγγραφής.</p>
 </header>
 <div class="toolbar" role="group" aria-label="Οργανισμός">
  <button type="button" class:chosen={organization==='lefteria_fc_academy'}
   disabled={busy} onclick={()=>organization='lefteria_fc_academy'}>Ακαδημία</button>
  <button type="button" class:chosen={organization==='lefteria_fc'}
   disabled={busy} onclick={()=>organization='lefteria_fc'}>Lefteria FC</button>
  <button type="button" disabled={busy} onclick={()=>void refresh()}>Ανανέωση κατάστασης</button>
 </div>
 {#if busy}
  <p role="status">Γίνεται ασφαλής επαλήθευση…</p>
 {:else if active}
  <p role="status" class="success">Ενεργή επαληθευμένη συνδρομή.
   Λήξη: {status?.expires_at?.slice(0,10)}.</p>
  <div class="toolbar" role="group" aria-label="Εκπαιδευτική διαδρομή">
   {#if status?.audience_parent}
    <button type="button" class:chosen={audience==='parent'}
     onclick={()=>audience='parent'}>Μαθήματα γονέων</button>
   {/if}
   {#if status?.audience_player}
    <button type="button" class:chosen={audience==='player'}
     onclick={()=>audience='player'}>Ποδοσφαιρικά μαθήματα</button>
   {/if}
  </div>
  {#if allowed&&lesson?.next}
   <article class="lesson">
    <p class="kicker">ΕΠΟΜΕΝΟ ΕΓΚΕΚΡΙΜΕΝΟ ΜΑΘΗΜΑ · {audience==='parent'?'ΓΟΝΕΙΣ':'ΠΑΙΚΤΕΣ'}</p>
    <h3>{lesson.next.title}</h3>
    <p class="body">{lesson.next.body}</p>
    <button type="button" class="primary" onclick={()=>void completeLesson()}>
     Ολοκλήρωσα · επόμενο μάθημα
    </button>
   </article>
  {:else if allowed&&lesson}
   <p role="status">Ολοκλήρωσες τα εγκεκριμένα μαθήματα αυτής της διαδρομής.</p>
  {/if}
 {:else}
  <p>Το εκπαιδευτικό υλικό ξεκλειδώνει μόνο μετά από επαληθευμένη πληρωμή Payabl.</p>
  {#if status?.status==='pending'&&status.checkout_url}
   <p>Η συνδρομή σου αναμένει πληρωμή. Δεν έχει ενεργοποιηθεί.</p>
   {#if validHostedPayablUrl(status.checkout_url)}
    <a href={status.checkout_url} rel="noopener noreferrer">Συνέχεια στην ασφαλή δοκιμαστική πληρωμή Payabl</a>
   {/if}
  {:else if status?.status==='pending'}
   <p role="status">Η προηγούμενη πληρωμή βρίσκεται σε διαδικασία συμφωνίας. Δεν δημιουργείται δεύτερο αίτημα.</p>
  {:else}
   <div class="form">
    <label>Ενδιαφέρον συνδρομής
     <select bind:value={track}>
      <option value="parent">Μαθήματα γονέων</option>
      <option value="player">Μαθήματα αθλητών</option>
      <option value="both">Και οι δύο διαδρομές</option>
     </select>
    </label>
    <label>Email πληρωτή <input type="email" maxlength="50" bind:value={email} autocomplete="email"/></label>
    <label>Όνομα πληρωτή <input maxlength="50" bind:value={firstName} autocomplete="given-name"/></label>
    <label>Επώνυμο πληρωτή <input maxlength="50" bind:value={lastName} autocomplete="family-name"/></label>
    <label class="consent"><input type="checkbox" bind:checked={consent}/>
     <span>Συναινώ στη δημιουργία μηνιαίας συνδρομής μέσω δοκιμαστικού περιβάλλοντος Payabl.
      Η ανανέωση δεν είναι ακόμη αυτόματη. Δεν θα χρεωθώ από παραγωγικό Payabl.</span></label>
    <button type="button" class="primary" disabled={!consent||busy||!email.includes('@')||
      firstName.trim().length===0||lastName.trim().length===0}
      onclick={()=>void checkout()}>Συνέχεια στο Payabl Sandbox</button>
   </div>
  {/if}
 {/if}
 {#if error}<p role="alert" class="error">{error}</p>{/if}
 <p class="note">Η επιλογή ενδιαφέροντος δεν αλλάζει τον πραγματικό ρόλο σου και δεν δίνει πρόσβαση σε προσωπικά στοιχεία παιδιών.</p>
</section>
<style>
 .fan-hub{margin:18px 0;padding:24px;background:#1b1d1c;border:1px solid #775d2b;border-radius:15px;color:#f1f0ee}
 .kicker{color:#efb74b;font-weight:700;letter-spacing:.13em;font-size:11px}
 h2{font-size:clamp(25px,5vw,34px);margin:8px 0}h3{font-size:23px;margin:9px 0}
 p{line-height:1.7;font-size:14px}.toolbar{display:flex;gap:10px;flex-wrap:wrap;margin:18px 0}
 button{min-height:44px;border:1px solid #a57c2a;background:#30271c;color:#ffd68c;border-radius:9px;padding:10px 16px;cursor:pointer}
 button:disabled{opacity:.5;cursor:default}.chosen,.primary{background:#b88723;color:#151411;font-weight:700}
 .form{display:grid;gap:12px;max-width:560px;margin:20px 0}
 label{display:grid;gap:7px;font-size:14px;font-weight:600}
 input,select{background:#0f1111;color:white;border:1px solid #777;border-radius:9px;padding:10px;min-height:44px;font:inherit}
 .consent{display:flex;gap:12px;align-items:flex-start}.consent input{width:20px;min-height:20px}
 .note{font-size:12px;color:#c6beb0}.error{color:#ffbfb0}.success{color:#d8efa8}
 .lesson{padding-top:14px;border-top:1px solid #575047}.body{white-space:pre-wrap;overflow-wrap:anywhere}
 a{color:#f8d48d}button:focus-visible,a:focus-visible{outline:2px solid #ffd177;outline-offset:3px}
</style>
