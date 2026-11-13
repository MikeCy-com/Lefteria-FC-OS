<script lang="ts">
  import {onMount} from 'svelte';
  import type {MobileAuthController} from '../controller.ts';
  import type {WeeklyLearningInbox,WeeklyLearningOrganization,
    WeeklyLearningAudience} from '../weekly-learning.ts';
  import './mobile-theme.css';

  let {controller,viewerRole}:{
    controller:MobileAuthController,viewerRole:'parent'|'player'|'fan'
  }=$props();
  let organization=$state<WeeklyLearningOrganization>('lefteria_fc_academy');
  let fanInterest=$state<'fan_parent'|'fan_player'>('fan_parent');
  const audience=$derived<WeeklyLearningAudience>(viewerRole==='fan'
    ?fanInterest:viewerRole);
  let inbox=$state<WeeklyLearningInbox|null>(null);
  let busy=$state(true);
  let error=$state<string|null>(null);
  let alive=false,request=0;

  onMount(()=>{
    alive=true;void refresh();
    return()=>{alive=false;request++;inbox=null;};
  });

  async function refresh(){
    const index=++request,owner=organization,track=audience;
    busy=true;inbox=null;error=null;
    try{
      let result=await controller.getWeeklyLearningInbox(owner,track);
      if(!alive||index!==request||owner!==organization||track!==audience)return;
      if(!result.enrolled){
        // A verified academy member begins Week 1 when first opening HUB;
        // a fan must pass Payabl entitlement checks before this succeeds.
        await controller.enrollWeeklyLearning(owner,track);
        result=await controller.getWeeklyLearningInbox(owner,track);
      }
      if(alive&&index===request&&owner===organization&&track===audience)
        inbox=result;
    }catch{
      if(alive&&index===request)
        error=viewerRole==='fan'
          ?'Απαιτείται επιβεβαιωμένη ενεργή συνδρομή Payabl για αυτή την εκπαιδευτική σειρά.'
          :'Δεν μπόρεσε να φορτωθεί η προσωπική εβδομαδιαία εκπαίδευση.';
    }finally{
      if(alive&&index===request)busy=false;
    }
  }

  function selectOrganization(next:WeeklyLearningOrganization){
    if(organization===next||busy)return;
    organization=next;void refresh();
  }
  function selectFanInterest(next:'fan_parent'|'fan_player'){
    if(fanInterest===next||busy)return;
    fanInterest=next;void refresh();
  }
</script>

<section class="weekly-learning" aria-labelledby="weekly-learning-title"
  data-testid="hub-weekly-private-learning">
  <header class="top">
    <p class="kicker">ΛΕΥΤΕΡΙΑ FC · ΠΡΟΣΩΠΙΚΗ ΕΚΠΑΙΔΕΥΣΗ</p>
    <h2 id="weekly-learning-title">Τα εβδομαδιαία μαθήματά μου</h2>
    <p>Κάθε νέο μέλος ξεκινά από την πρώτη εγκεκριμένη ενότητα,
      ανεξάρτητα από το πότε γράφτηκαν οι υπόλοιποι.</p>
  </header>
  <div class="choices" role="group" aria-label="Οργανισμός">
    <button type="button" class:chosen={organization==='lefteria_fc_academy'}
      disabled={busy} onclick={()=>selectOrganization('lefteria_fc_academy')}>Ακαδημία</button>
    <button type="button" class:chosen={organization==='lefteria_fc'}
      disabled={busy} onclick={()=>selectOrganization('lefteria_fc')}>Lefteria FC</button>
  </div>
  {#if viewerRole==='fan'}
    <div class="choices" role="group" aria-label="Εκπαιδευτική συνδρομή">
      <button type="button" class:chosen={fanInterest==='fan_parent'}
        disabled={busy} onclick={()=>selectFanInterest('fan_parent')}>Για γονείς</button>
      <button type="button" class:chosen={fanInterest==='fan_player'}
        disabled={busy} onclick={()=>selectFanInterest('fan_player')}>Για αθλητές</button>
    </div>
    <p class="info">Η επιλογή εκπαιδευτικού ενδιαφέροντος δεν παρέχει πρόσβαση σε στοιχεία παιδιών ή ρόλο γονέα ή αθλητή.</p>
  {/if}
  <button type="button" disabled={busy} onclick={()=>void refresh()}>Ανανέωση μαθημάτων</button>
  {#if busy}
    <p class="info" role="status">Έλεγχος προσωπικού εκπαιδευτικού προγράμματος…</p>
  {:else if inbox}
    {#if inbox.enrolled && inbox.status==='active'}
      <p class="info" role="status">
        Επόμενη ενότητα: Εβδομάδα {inbox.delivery_week}.
        {#if inbox.next_due_at}
          Προγραμματισμένη ημερομηνία: {new Date(inbox.next_due_at).toLocaleDateString('el-CY')}.
        {/if}
      </p>
    {:else if inbox.enrolled}
      <p class="info" role="status">Η προσωπική διαδρομή είναι {inbox.status==='paused'?'σε παύση':'ανενεργή'}.</p>
    {/if}
    {#if inbox.deliveries.length}
      <div class="lessons">
        {#each inbox.deliveries as lesson (lesson.id)}
          <article class="lesson">
            <p class="kicker">ΕΒΔΟΜΑΔΑ {lesson.week}</p>
            <h3>{lesson.title}</h3>
            <p class="body">{lesson.body}</p>
          </article>
        {/each}
      </div>
    {:else}
      <p class="info" role="status">Δεν υπάρχει ακόμη διαθέσιμο εγκεκριμένο μάθημα. Η θέση σου στην προσωπική σειρά διατηρείται.</p>
    {/if}
  {/if}
  {#if error}<p class="error" role="alert">{error}</p>{/if}
</section>

<style>
  .weekly-learning{margin:17px 0;padding:22px;border-radius:14px;border:1px solid #655027;background:#191b1b;color:#f7f4ee}
  .top{margin-bottom:18px}
  .kicker{font-size:11px;letter-spacing:.13em;color:#eabc5d;font-weight:700}
  h2{font:700 clamp(24px,5vw,32px) 'Fira Sans Extra Condensed',sans-serif;margin:7px 0}
  h3{font:700 22px 'Fira Sans Extra Condensed',sans-serif;margin:7px 0}
  .top>p:last-child,.info{font-size:14px;line-height:1.7;color:#c1c4be}
  .choices{display:flex;flex-wrap:wrap;gap:8px;margin:11px 0}
  button{min-height:44px;padding:10px 14px;border:1px solid #8f6d36;border-radius:9px;background:#2d281e;color:#ffe0a2;font:inherit;cursor:pointer}
  button.chosen,button:focus-visible{outline:2px solid #eabc5d;outline-offset:2px}
  button:disabled{opacity:.55;cursor:default}
  .lessons{display:grid;gap:14px;margin-top:18px}
  .lesson{border-top:1px solid #4b4638;padding-top:16px}
  .body{white-space:pre-wrap;overflow-wrap:anywhere;font-size:15px;line-height:1.8}
  .error{font-size:13px;line-height:1.6;color:#ffcdbe}
</style>
