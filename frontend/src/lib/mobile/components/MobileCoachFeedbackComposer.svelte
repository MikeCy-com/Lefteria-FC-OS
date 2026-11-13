<script lang="ts">
  import {onMount} from 'svelte';
  import type {MobileAuthController} from '../controller.ts';
  import type {CoachFeedbackTarget,CoachFeedbackDraft,CoachFeedbackApproved} from '../coach-feedback-composer.ts';
  import {validCoachText} from '../coach-feedback-composer.ts';
  import './mobile-theme.css';

  let {controller}:{controller:MobileAuthController}=$props();
  type Stage='edit'|'draft'|'approved'|'sent';
  let targets=$state<CoachFeedbackTarget[]>([]);
  let selectedKey=$state('');
  let observation=$state('');
  let focusPoint=$state('');
  let language=$state<'el'|'en'>('el');
  let finalText=$state('');
  let signedText=$state('');
  let stage=$state<Stage>('edit');
  let draft=$state<CoachFeedbackDraft|null>(null);
  let approved=$state<CoachFeedbackApproved|null>(null);
  let acknowledgeApproval=$state(false);
  let acknowledgeDelivery=$state(false);
  let busy=$state(false);
  let loading=$state(true);
  let error=$state<string|null>(null);
  let alive=false;
  const key=(target:CoachFeedbackTarget)=>target.organization_id+':'+target.id;
  const selected=$derived(targets.find(target=>key(target)===selectedKey));

  onMount(()=>{
    alive=true;
    void loadTargets();
    return()=>{alive=false;targets=[];draft=null;approved=null;signedText='';};
  });
  async function loadTargets(){
    loading=true;error=null;
    try{
      const result=await controller.getCoachFeedbackTargets();
      if(!alive)return;
      targets=result.items;
      selectedKey='';
    }catch{
      if(alive)error='Δεν ήταν δυνατή η επαλήθευση των παικτών που προπονείς.';
    }finally{if(alive)loading=false;}
  }
  async function createDraft(){
    if(!selected||busy||stage!=='edit'||
      !validCoachText(observation,5,500)||!validCoachText(focusPoint,5,500))return;
    busy=true;error=null;
    try{
      const result=await controller.createCoachFeedbackDraft(selected,observation,focusPoint,language);
      if(!alive)return;
      draft=result;
      signedText='';
      finalText=language==='el'
        ? `Γεια σου ${selected.name}! Παρατήρησα την πρόοδό σου: ${observation}. Ένα σημείο που θα ήθελα να δουλέψεις είναι: ${focusPoint}. Συνέχισε με προσπάθεια και χαρά!`
        : `Hello ${selected.name}! I have noticed your progress: ${observation}. One thing I'd like you to focus on is ${focusPoint}. Keep learning and enjoying football!`;
      stage='draft';acknowledgeApproval=false;
    }catch{
      if(alive)error='Δεν αποθηκεύτηκε το προσχέδιο. Έλεγξε ότι είσαι ακόμη προπονητής του παίκτη.';
    }finally{if(alive)busy=false;}
  }
  async function approveDraft(){
    if(!draft||stage!=='draft'||busy||!acknowledgeApproval||
      !validCoachText(finalText,30,1600))return;
    busy=true;error=null;
    // Display exactly the final text that was submitted for coach signoff.
    // The editor is locked during the request; never display a later edit as approved.
    const submittedText=finalText;
    try{
      const result=await controller.approveCoachFeedbackDraft(draft,submittedText);
      if(!alive)return;
      signedText=submittedText;
      approved=result;stage='approved';acknowledgeDelivery=false;
    }catch{
      if(alive)error='Η έγκριση απέτυχε. Το μήνυμα δεν αποστάλθηκε.';
    }finally{if(alive)busy=false;}
  }
  async function sendApproved(){
    if(!approved||stage!=='approved'||busy||!acknowledgeDelivery)return;
    busy=true;error=null;
    try{
      await controller.sendCoachFeedback(approved);
      if(alive)stage='sent';
    }catch{
      if(alive)error='Δεν επιβεβαιώθηκε αποστολή. Έλεγξε την ανάθεση και τη σύνδεση πριν δοκιμάσεις ξανά.';
    }finally{if(alive)busy=false;}
  }
</script>

<section class="coach-composer" data-testid="mobile-coach-feedback-composer" aria-labelledby="coach-feedback-title">
  <p class="kicker">ΛΕΥΤΕΡΙΑ FC · ΠΡΟΠΟΝΗΤΗΣ</p>
  <h1 id="coach-feedback-title">Προσωπική ενθάρρυνση παίκτη</h1>
  <p class="explain">Γράψε τι παρατήρησες πραγματικά και ένα συγκεκριμένο σημείο βελτίωσης.
    Το μήνυμα αποστέλλεται μόνο όταν το ελέγξεις, το εγκρίνεις και πατήσεις ξεχωριστά αποστολή.</p>
  {#if loading}
    <p role="status">Επαλήθευση των παικτών της ομάδας σου…</p>
  {:else if targets.length===0}
    <p role="status">Δεν βρέθηκαν διαθέσιμοι παίκτες με ενεργή ανάθεση για τον λογαριασμό σου.</p>
  {/if}
  {#if targets.length>0}
    <div class="fields">
      <label for="coach-feedback-player">Παίκτης</label>
      <select id="coach-feedback-player" bind:value={selectedKey} disabled={stage!=='edit'||busy}>
        <option value="">Επίλεξε παίκτη</option>
        {#each targets as item (key(item))}
          <option value={key(item)}>{item.name} · {item.organization_id==='lefteria_fc_academy'?'Ακαδημία':'Πρώτη ομάδα'}</option>
        {/each}
      </select>
      <label for="coach-feedback-language">Γλώσσα μηνύματος</label>
      <select id="coach-feedback-language" bind:value={language} disabled={stage!=='edit'||busy}>
        <option value="el">Ελληνικά</option>
        <option value="en">English</option>
      </select>
      <label for="coach-feedback-observation">Τι παρατήρησες στην προπόνηση;</label>
      <textarea id="coach-feedback-observation" bind:value={observation} rows="3"
        minlength="5" maxlength="500" disabled={stage!=='edit'||busy}
        placeholder="Π.χ. Έχεις βελτιώσει τον έλεγχο της μπάλας."></textarea>
      <label for="coach-feedback-focus">Σε τι να επικεντρωθεί ο παίκτης;</label>
      <textarea id="coach-feedback-focus" bind:value={focusPoint} rows="3"
        minlength="5" maxlength="500" disabled={stage!=='edit'||busy}
        placeholder="Π.χ. Να κοιτάζει γύρω πριν δεχτεί μια πάσα."></textarea>
    </div>
    {#if stage==='edit'}
      <button type="button" class="primary" disabled={busy||!selected||
        !validCoachText(observation,5,500)||!validCoachText(focusPoint,5,500)}
        onclick={()=>void createDraft()}>Αποθήκευση προσχεδίου</button>
    {:else}
      <section class="review" aria-labelledby="coach-feedback-review">
        <h2 id="coach-feedback-review">{stage==='sent'?'Αποσταλμένο μήνυμα':'Έλεγχος μηνύματος'}</h2>
        {#if selected}<p class="recipient">Παραλήπτης: <strong>{selected.name}</strong> · {selected.organization_id==='lefteria_fc_academy'?'Ακαδημία':'Πρώτη ομάδα'}</p>{/if}
        {#if stage==='draft'}
          <label for="coach-feedback-final">Επεξεργάσου το τελικό κείμενο πριν το εγκρίνεις</label>
          <textarea id="coach-feedback-final" bind:value={finalText} rows="6" maxlength="1600" disabled={busy}></textarea>
          <label class="confirmation">
            <input type="checkbox" bind:checked={acknowledgeApproval} disabled={busy}/>
            Βεβαιώνω ότι αυτή είναι δική μου παρατήρηση και εγκρίνω προσωπικά τις τελικές λέξεις.
          </label>
          <button type="button" class="primary"
            disabled={busy||!acknowledgeApproval||!validCoachText(finalText,30,1600)}
            onclick={()=>void approveDraft()}>Έγκριση τελικού κειμένου</button>
          <p class="explain">Η έγκριση δεν στέλνει το μήνυμα.</p>
        {:else}
          <p class="message">{signedText}</p>
          {#if stage==='approved'}
            <label class="confirmation">
              <input type="checkbox" bind:checked={acknowledgeDelivery} disabled={busy}/>
              Επιβεβαιώνω την ξεχωριστή αποστολή μόνο σε αυτόν τον παίκτη.
            </label>
            <button type="button" class="primary" disabled={busy||!acknowledgeDelivery}
              onclick={()=>void sendApproved()}>Αποστολή στον παίκτη</button>
          {:else}
            <p class="success" role="status">Το μήνυμα καταχωρίστηκε στα προσωπικά μηνύματα του παίκτη. Δεν σημαίνει ότι έχει διαβαστεί.</p>
            <button type="button" class="primary" disabled={busy} onclick={()=>{
              stage='edit';draft=null;approved=null;signedText='';finalText='';
              observation='';focusPoint='';acknowledgeApproval=false;acknowledgeDelivery=false;
              selectedKey='';error=null;void loadTargets();
            }}>Νέο μήνυμα σε επαληθευμένο παίκτη</button>
          {/if}
        {/if}
      </section>
    {/if}
  {/if}
  {#if error}<p class="error" role="alert">{error}</p>{/if}
  <p class="explain">Μην καταγράφεις ιατρικές πληροφορίες, συγκρίσεις με συμπαίκτες ή παρατηρήσεις που δεν έχεις κάνει προσωπικά. Ο AI agent δεν μπορεί να εγκρίνει ή να στείλει μήνυμα αντί για εσένα.</p>
</section>

<style>
  .coach-composer{padding:23px;max-width:830px;margin:0 auto 24px;border:1px solid var(--lf-line);border-radius:15px;background:var(--lf-surface);color:var(--lf-text);font-family:var(--lf-body)}
  .kicker{color:var(--lf-gold);font-size:11px;letter-spacing:.12em;font-weight:800}
  h1,h2{font-family:var(--lf-heading);line-height:1.16}h1{font-size:clamp(26px,6vw,38px);margin:6px 0 12px}h2{font-size:23px}
  .explain{font-size:13px;line-height:1.65;color:var(--lf-muted)}
  .fields{display:grid;gap:9px;margin:21px 0}
  label{font-weight:700;font-size:13px}
  select,textarea{width:100%;border:1px solid var(--lf-line);border-radius:9px;padding:12px;background:var(--lf-elevated);color:var(--lf-text);font:inherit;font-size:14px;min-height:44px}
  textarea{resize:vertical;line-height:1.6}
  button{font:inherit;border-radius:9px;min-height:44px;padding:11px 16px;cursor:pointer}
  button:disabled{cursor:default;opacity:.55}
  .primary{border:1px solid var(--lf-gold);background:var(--lf-gold);color:#17130a;font-weight:800}
  .review{margin-top:22px;padding:18px;border:1px solid var(--lf-brand-edge);border-radius:12px;background:var(--lf-elevated);display:grid;gap:12px}
  .review h2{margin:0}.review label{margin-top:4px}
  .confirmation{display:flex;gap:12px;align-items:start;font-weight:500;line-height:1.5}
  .confirmation input{width:20px;height:20px;flex-shrink:0;accent-color:var(--lf-gold)}
  .message{white-space:pre-wrap;font-size:14px;line-height:1.7}
  .recipient{font-size:13px;color:var(--lf-muted);margin:0}
  .success{color:#a8e7a8;font-weight:700}.error{color:#ffcdbe;font-size:13px}
  :global(button:focus-visible),:global(select:focus-visible),:global(textarea:focus-visible){outline:2px solid var(--lf-gold);outline-offset:2px}
</style>
