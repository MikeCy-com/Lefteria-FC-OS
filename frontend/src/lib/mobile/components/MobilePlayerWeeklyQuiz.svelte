<script lang="ts">
  import {onMount,untrack} from 'svelte';
  import type {MobileAuthController} from '../controller.ts';
  import type {PlayerQuizPage,PlayerQuizScore,PlayerQuizOrganization,
    PlayerQuizAnswer} from '../player-weekly-quiz.ts';
  import './mobile-theme.css';

  let {controller}:{controller:MobileAuthController}=$props();
  let organization=$state<PlayerQuizOrganization>('lefteria_fc_academy');
  let page=$state<PlayerQuizPage|null>(null);
  let score=$state<PlayerQuizScore|null>(null);
  let chosen=$state<Record<string,number>>({});
  let busy=$state(true);
  let error=$state<string|null>(null);
  let alive=false,revision=0;
  const complete=$derived(!!page?.quiz&&page.quiz.questions.every(
    question=>Number.isInteger(chosen[question.id])));

  onMount(()=>{
    alive=true;void refresh();
    return()=>{alive=false;revision++;page=null;score=null;chosen={};};
  });
  $effect(()=>{
    const owner=organization;
    if(alive)untrack(()=>void refresh(owner));
  });
  async function refresh(owner=organization){
    const current=++revision;
    page=null;score=null;chosen={};busy=true;error=null;
    try{
      const result=await controller.getPlayerWeeklyQuiz(owner);
      if(alive&&current===revision&&organization===owner)page=result;
    }catch{
      if(alive&&current===revision)error='Δεν ήταν δυνατή η φόρτωση του προσωπικού κουίζ.';
    }finally{
      if(alive&&current===revision)busy=false;
    }
  }
  async function start(){
    if(busy)return;
    const owner=organization,current=++revision;
    busy=true;error=null;page=null;score=null;chosen={};
    try{
      await controller.startPlayerWeeklyQuiz(owner);
      if(alive&&current===revision&&organization===owner)await refresh(owner);
    }catch{
      if(alive&&current===revision)error='Δεν ξεκίνησε η προσωπική σειρά κουίζ.';
    }finally{
      if(alive&&current===revision)busy=false;
    }
  }
  async function submit(){
    const quiz=page?.quiz;
    if(busy||!quiz||!complete)return;
    const current=++revision,owner=organization;
    busy=true;error=null;
    try{
      const answers:PlayerQuizAnswer[]=quiz.questions.map(question=>({
        question_id:question.id,selected_index:chosen[question.id]
      }));
      const result=await controller.submitPlayerWeeklyQuiz(owner,quiz,answers);
      if(alive&&current===revision&&organization===owner){
        score=result;
        page=null;
        chosen={};
      }
    }catch{
      if(alive&&current===revision){
        page=null;chosen={};
        error='Το κουίζ άλλαξε ή η υποβολή απορρίφθηκε. Ανανέωσε τη σειρά σου.';
      }
    }finally{
      if(alive&&current===revision)busy=false;
    }
  }
</script>

<section class="weekly" data-testid="player-weekly-quiz" aria-labelledby="player-quiz-title">
  <p class="eyebrow">LEFTERIA FC · ΠΟΔΟΣΦΑΙΡΙΚΗ ΕΚΠΑΙΔΕΥΣΗ</p>
  <h2 id="player-quiz-title">Το προσωπικό εβδομαδιαίο κουίζ μου</h2>
  <p class="description">Μαθαίνω για θέσεις, άμυνα, επίθεση, μεταβάσεις, κίνηση και σωστές αποφάσεις.
    Κάθε νέος αθλητής ξεκινά από την πρώτη εβδομάδα. Οι απαντήσεις μου παραμένουν ιδιωτικές.</p>
  <div class="organizations" role="group" aria-label="Οργανισμός κουίζ">
    <button type="button" class:chosen={organization==='lefteria_fc_academy'}
      disabled={busy} onclick={()=>{organization='lefteria_fc_academy';}}>Ακαδημία</button>
    <button type="button" class:chosen={organization==='lefteria_fc'}
      disabled={busy} onclick={()=>{organization='lefteria_fc';}}>Lefteria FC</button>
    <button type="button" disabled={busy} onclick={()=>void refresh()}>Ανανέωση</button>
  </div>
  {#if busy}
    <p role="status">Φόρτωση εγκεκριμένου κουίζ…</p>
  {:else if score}
    <div class="result" role="status">
      <strong>Το κουίζ καταχωρίστηκε!</strong>
      <p>Σωστές απαντήσεις: {score.score} από {score.total}.</p>
      <p>Συνεχίζεις με το επόμενο μάθημα και κουίζ, όταν είναι διαθέσιμο.</p>
      <button type="button" onclick={()=>void refresh()}>Δες το επόμενο κουίζ</button>
    </div>
  {:else if page?.started===false}
    <div class="result">
      <p>Ξεκίνησε την προσωπική σου ποδοσφαιρική διαδρομή από την Εβδομάδα 1.</p>
      <button type="button" onclick={()=>void start()}>Έναρξη Εβδομάδας 1</button>
    </div>
  {:else if page?.quiz}
    <form onsubmit={event=>{event.preventDefault();void submit();}}>
      <div class="quiz-title">
        <span>Εβδομάδα {page.quiz.week_number}</span>
        <h3>{page.quiz.title}</h3>
      </div>
      {#each page.quiz.questions as question (question.id)}
        <fieldset>
          <legend>{question.sequence}. {question.prompt}</legend>
          {#each question.choices as choice,index (index)}
            <label class="choice">
              <input type="radio" name={'quiz-'+question.id}
                checked={chosen[question.id]===index}
                onchange={()=>{chosen={...chosen,[question.id]:index};}}/>
              <span>{choice}</span>
            </label>
          {/each}
        </fieldset>
      {/each}
      <button class="submit" type="submit" disabled={!complete||busy}>
        Υποβολή απαντήσεων
      </button>
    </form>
  {:else if page}
    <p role="status" class="description">Δεν υπάρχει ακόμη άλλο εγκεκριμένο κουίζ για την προσωπική σου εβδομάδα.</p>
  {/if}
  {#if error}
    <p role="alert" class="error">{error}</p>
    <button type="button" onclick={()=>void refresh()}>Προσπάθησε ξανά</button>
  {/if}
</section>

<style>
  .weekly{padding:22px;border-radius:14px;margin:18px 0;border:1px solid var(--lf-brand-edge);background:var(--lf-surface);color:var(--lf-text)}
  .eyebrow{font-size:11px;font-weight:700;letter-spacing:.12em;color:var(--lf-gold)}
  h2{font:700 clamp(24px,5vw,32px) var(--lf-heading);margin:7px 0 12px}
  h3{font:700 23px var(--lf-heading);margin:10px 0}
  .description{font-size:14px;line-height:1.7;color:var(--lf-muted)}
  .organizations{display:flex;gap:8px;flex-wrap:wrap;margin:16px 0}
  button{min-height:44px;border-radius:9px;border:1px solid var(--lf-brand-edge);padding:10px 16px;background:var(--lf-elevated);color:var(--lf-gold-light);cursor:pointer;font:inherit}
  button:disabled{opacity:.55;cursor:default}
  button.chosen,button:focus-visible{outline:2px solid var(--lf-gold);outline-offset:3px}
  .quiz-title{margin:18px 0}.quiz-title span{font-size:12px;color:var(--lf-gold)}
  fieldset{border:1px solid var(--lf-line);border-radius:10px;margin:12px 0;padding:14px}
  legend{font-size:15px;font-weight:700;line-height:1.6;padding:0 8px}
  .choice{display:flex;gap:10px;align-items:flex-start;min-height:44px;padding:10px 6px;border-bottom:1px solid var(--lf-line);font-size:14px;line-height:1.5;cursor:pointer}
  .choice:last-child{border:0}
  .choice input{width:18px;height:18px;accent-color:var(--lf-gold);flex-shrink:0}
  .submit{margin-top:10px;border-color:var(--lf-gold);font-weight:700}
  .result{border-top:1px solid var(--lf-line);margin-top:14px;padding-top:14px}
  .error{color:#ffcdbe;font-size:13px}
</style>
