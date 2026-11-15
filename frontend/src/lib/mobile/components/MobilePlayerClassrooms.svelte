<script lang="ts">
  import {onMount,untrack} from 'svelte';
  import type {MobileAuthController} from '../controller.ts';
  import {classroomLabels,playerClassroomPositionLabels} from '../player-classrooms.ts';
  import type {PlayerClassroomPage,PlayerClassroomOrganization} from '../player-classrooms.ts';
  import './mobile-theme.css';

  let {controller}:{controller:MobileAuthController}=$props();
  let organization=$state<PlayerClassroomOrganization>('lefteria_fc_academy');
  let page=$state<PlayerClassroomPage|null>(null);
  let busy=$state(true),error=$state<string|null>(null);
  let alive=false,generation=0;
  const completedTopics=$derived(page?.subjects.filter(x=>x.completed_quizzes>0).length??0);

  onMount(()=>{
    alive=true;void refresh(organization);
    return()=>{alive=false;generation++;page=null;};
  });
  $effect(()=>{
    const selected=organization;
    if(alive)untrack(()=>void refresh(selected));
  });
  async function refresh(owner:PlayerClassroomOrganization){
    const current=++generation;
    page=null;error=null;busy=true;
    try{
      const result=await controller.getPlayerClassrooms(owner);
      if(alive&&current===generation&&organization===owner)page=result;
    }catch{
      if(alive&&current===generation&&organization===owner)
        error='Δεν υπάρχουν διαθέσιμα στοιχεία τάξης ή δεν έχεις πρόσβαση σε αυτόν τον οργανισμό.';
    }finally{
      if(alive&&current===generation)busy=false;
    }
  }
</script>

<section class="classroom" data-testid="hub-player-classrooms" aria-labelledby="classroom-heading">
  <header>
    <p class="eyebrow">LEFTERIA FC · ΠΟΔΟΣΦΑΙΡΙΚΗ ΑΚΑΔΗΜΙΑ ΜΑΘΗΣΗΣ</p>
    <h2 id="classroom-heading">Η ποδοσφαιρική μου τάξη</h2>
    <p class="description">Μάθε τον ρόλο σου, την τοποθέτηση, την άμυνα,
      την επίθεση και τις μεταβάσεις. Η πρόοδός σου είναι προσωπική, όχι κατάταξη
      συμπαικτών. Κάθε νέος αθλητής ξεκινά από την αρχή.</p>
  </header>
  <div class="organizations" role="group" aria-label="Οργανισμός ποδοσφαιρικής τάξης">
    <button type="button" aria-pressed={organization==='lefteria_fc_academy'}
      class:chosen={organization==='lefteria_fc_academy'} disabled={busy}
      onclick={()=>organization='lefteria_fc_academy'}>Ακαδημία</button>
    <button type="button" aria-pressed={organization==='lefteria_fc'}
      class:chosen={organization==='lefteria_fc'} disabled={busy}
      onclick={()=>organization='lefteria_fc'}>Πρώτη Ομάδα</button>
    <button type="button" disabled={busy} onclick={()=>void refresh(organization)}>Ανανέωση</button>
  </div>
  {#if busy}
    <p role="status" class="description">Φόρτωση επαληθευμένης προσωπικής προόδου…</p>
  {:else if page}
    <div class="identity-note" role="status">
      <span class="identity-icon" aria-hidden="true">◈</span>
      <div>
        <strong>{playerClassroomPositionLabels[page.verified_position]}</strong>
        <p>Η θέση προκύπτει από το ενεργό ρόστερ και όχι από ελεύθερη επιλογή.</p>
      </div>
    </div>
    <div class="milestones" aria-label="Επαληθευμένη ατομική πρόοδος">
      <article class="milestone" data-testid="hub-classroom-lesson-milestone">
        <span class="milestone-tag">ΜΑΘΗΜΑΤΑ</span>
        <strong class="metric">{page.lessons.completed}<small> / {page.lessons.approved}</small></strong>
        <p>Εγκεκριμένα μαθήματα που ολοκλήρωσες</p>
        <progress value={page.lessons.completed} max={Math.max(1,page.lessons.approved)}
          aria-label="Ολοκλήρωση εγκεκριμένων μαθημάτων"></progress>
        {#if page.lessons.next_title}
          <span class="next-label">Επόμενο εγκεκριμένο μάθημα</span>
          <strong class="next-name">{page.lessons.next_title}</strong>
          <a class="action" href="#hub-player-lesson">Άνοιγμα μαθήματος</a>
        {:else}
          <span class="quiet">Δεν υπάρχει άλλο εγκεκριμένο μάθημα αυτή τη στιγμή.</span>
        {/if}
      </article>
      <article class="milestone" data-testid="hub-classroom-quiz-milestone">
        <span class="milestone-tag">ΕΒΔΟΜΑΔΙΑΙΑ ΚΟΥΙΖ</span>
        <strong class="metric">{page.quizzes.completed_weeks}<small> εβδομάδες</small></strong>
        {#if page.quizzes.started}
          <p>Προσωπική επόμενη εβδομάδα: {page.quizzes.current_week}</p>
        {:else}
          <p>Η πρώτη εβδομάδα αρχίζει όταν επιλέξεις Έναρξη.</p>
        {/if}
        {#if page.quizzes.next_quiz_title}
          <span class="next-label">Εγκεκριμένο κουίζ τώρα</span>
          <strong class="next-name">{page.quizzes.next_quiz_title}</strong>
        {:else}
          <span class="quiet">Δεν έχει ξεκλειδωθεί άλλο εγκεκριμένο κουίζ.</span>
        {/if}
        <a class="action" href="#hub-player-quiz">Άνοιγμα εβδομαδιαίων κουίζ</a>
      </article>
    </div>
    <div class="room-heading">
      <div>
        <h3>Αίθουσες ποδοσφαιρικών δεξιοτήτων</h3>
        <p class="description">Οι θεματικές είναι ο χάρτης μάθησης. Μόνο εγκεκριμένα
          και δημοσιευμένα κουίζ υπολογίζονται στην πρόοδο.</p>
      </div>
      <span class="room-count" aria-label="Θεματικές με προσωπική πρόοδο">
        {completedTopics} / {page.subjects.length}
      </span>
    </div>
    <div class="rooms" data-testid="hub-classroom-subjects">
      {#each page.subjects as subject (subject.topic)}
        <article class="room" data-topic={subject.topic}>
          <div class="room-top">
            <span class="symbol" aria-hidden="true">◇</span>
            {#if subject.completed_quizzes>0}
              <span class="mini-state">Έχεις προχωρήσει</span>
            {:else if subject.published_quizzes>0}
              <span class="mini-state">Εγκεκριμένο περιεχόμενο</span>
            {:else}
              <span class="mini-state awaiting">Σε προετοιμασία</span>
            {/if}
          </div>
          <h4>{classroomLabels[subject.topic].name}</h4>
          <p>{classroomLabels[subject.topic].description}</p>
          {#if subject.published_quizzes>0}
            <div class="room-progress">
              <strong>{subject.completed_quizzes} / {subject.published_quizzes} κουίζ</strong>
              <progress value={subject.completed_quizzes}
                max={Math.max(1,subject.published_quizzes)}
                aria-label={'Πρόοδος κουίζ: '+classroomLabels[subject.topic].name}></progress>
              {#if subject.total_answers>0}
                <span>Προσωπικές σωστές απαντήσεις:
                  {subject.correct_answers} από {subject.total_answers}</span>
              {/if}
            </div>
          {:else}
            <p class="quiet">Δεν έχει δημοσιευθεί εγκεκριμένο κουίζ για αυτή τη θεματική.</p>
          {/if}
        </article>
      {/each}
    </div>
    <p class="privacy">Τα μελλοντικά κουίζ δεν ξεκλειδώνουν από εδώ.
      Η ολοκλήρωση μαθημάτων και η βαθμολόγηση γίνονται μόνο στις προσωπικές
      ενότητές σου. Δεν εμφανίζονται άλλοι αθλητές ή κατατάξεις.</p>
  {/if}
  {#if error}
    <p role="alert" class="error">{error}</p>
    <button type="button" onclick={()=>void refresh(organization)}>Δοκίμασε ξανά</button>
  {/if}
</section>

<style>
  .classroom{padding:clamp(16px,3vw,28px);border-radius:17px;border:1px solid var(--lf-brand-edge);
    background:var(--lf-surface);color:var(--lf-text);margin:0 0 20px}
  .eyebrow,.milestone-tag{font-size:12px;letter-spacing:.1em;color:var(--lf-gold);font-weight:750}
  h2{font:800 clamp(26px,5vw,37px) var(--lf-heading);margin:6px 0 10px}
  h3{font:750 24px var(--lf-heading);margin:0}
  h4{font:750 20px var(--lf-heading);margin:10px 0}
  .description{color:var(--lf-muted);line-height:1.65;font-size:15px;max-width:70ch}
  .organizations{display:flex;gap:8px;flex-wrap:wrap;margin:16px 0}
  button,.action{min-height:44px;border:1px solid var(--lf-brand-edge);border-radius:10px;
    padding:10px 15px;background:var(--lf-elevated);color:var(--lf-gold-light);
    cursor:pointer;font-weight:700}
  button:disabled{opacity:.55;cursor:default}
  button.chosen{border-color:var(--lf-gold);outline:1px solid var(--lf-gold)}
  button:focus-visible,.action:focus-visible{outline:2px solid var(--lf-gold);outline-offset:3px}
  .identity-note{display:flex;gap:12px;align-items:center;border:1px solid var(--lf-line);
    border-radius:12px;padding:13px 16px;background:var(--lf-elevated);margin-bottom:14px}
  .identity-note strong{font-size:16px;color:var(--lf-gold-light)}
  .identity-note p{font-size:12px;margin:4px 0 0;color:var(--lf-muted);line-height:1.5}
  .identity-icon{font-size:26px;color:var(--lf-gold)}
  .milestones{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:13px}
  .milestone{background:var(--lf-elevated);padding:18px;border:1px solid var(--lf-line);
    border-radius:13px;display:flex;flex-direction:column;gap:8px}
  .metric{font:800 38px var(--lf-heading);color:var(--lf-gold-light)}
  .metric small{font:650 15px var(--lf-heading);color:var(--lf-muted)}
  .milestone p{margin:0;color:var(--lf-muted);font-size:14px;line-height:1.5}
  .next-label{font-size:12px;letter-spacing:.04em;color:var(--lf-gold)}
  .next-name{font-size:16px;overflow-wrap:anywhere;line-height:1.5}
  progress{width:100%;height:10px;accent-color:var(--lf-gold)}
  .action{display:inline-flex;align-items:center;justify-content:center;text-align:center;
    align-self:start;text-decoration:none;margin-top:auto}
  .quiet{color:var(--lf-muted);font-size:13px;line-height:1.5}
  .room-heading{display:flex;align-items:center;justify-content:space-between;
    gap:12px;margin:27px 0 15px}
  .room-heading .description{margin:6px 0 0;font-size:13px}
  .room-count{font-weight:800;color:var(--lf-gold-light);white-space:nowrap}
  .rooms{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,245px),1fr));gap:11px}
  .room{min-width:0;border:1px solid var(--lf-line);border-radius:12px;
    padding:16px;background:var(--lf-elevated)}
  .room-top{display:flex;align-items:center;justify-content:space-between;gap:8px}
  .symbol{color:var(--lf-gold);font-size:25px}
  .mini-state{font-size:11px;color:var(--lf-gold-light);text-align:right}
  .mini-state.awaiting{color:var(--lf-muted)}
  .room>p{font-size:13px;line-height:1.6;color:var(--lf-muted)}
  .room-progress{display:flex;flex-direction:column;gap:8px;margin-top:18px}
  .room-progress strong,.room-progress span{font-size:12px;color:var(--lf-gold-light)}
  .room-progress span{color:var(--lf-muted);line-height:1.5}
  .privacy{color:var(--lf-muted);font-size:12px;line-height:1.6;margin:19px 0 0}
  .error{color:#ffcdbe;line-height:1.55;font-size:14px}
  @media(max-width:600px){.milestones{grid-template-columns:1fr}.organizations button{flex:1 1 auto}}
</style>
