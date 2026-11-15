<script lang="ts">
  import {onMount,untrack} from 'svelte';
  import type {MobileAuthController} from '../controller.ts';
  import type {PublishedHubNextPage,PublishedHubArticle} from '../published-education.ts';
  import type {PersonalLessonSchedule,PersonalLessonInbox} from '../learning-delivery.ts';
  import './mobile-theme.css';

  let {controller,viewerRole}:{
    controller:MobileAuthController,viewerRole:'parent'|'player'
  }=$props();
  let organization=$state<PublishedHubArticle['organization_id']>('lefteria_fc_academy');
  let page=$state<PublishedHubNextPage|null>(null);
  let schedule=$state<PersonalLessonSchedule|null>(null);
  let deliveries=$state<PersonalLessonInbox|null>(null);
  let busy=$state(true);
  let error=$state<string|null>(null);
  let alive=false,operation=0;
  const cyprusTime=new Intl.DateTimeFormat('el-CY',{
    timeZone:'Asia/Nicosia',day:'numeric',month:'long',year:'numeric',
    hour:'2-digit',minute:'2-digit',hour12:false
  });
  const cyprusDate=new Intl.DateTimeFormat('el-CY',{
    timeZone:'Asia/Nicosia',day:'numeric',month:'long',year:'numeric'
  });
  const notifications=$derived([...(deliveries?.items??[])].sort(
    (a,b)=>Date.parse(b.queued_at)-Date.parse(a.queued_at)||b.id.localeCompare(a.id)));

  onMount(()=>{
    alive=true;void refresh();
    return()=>{alive=false;operation++;page=null;};
  });
  // Never keep a lesson from a previously authenticated role on this screen.
  $effect(()=>{
    const role=viewerRole;
    if(alive&&(role==='parent'||role==='player'))untrack(()=>void refresh());
  });
  async function refresh(){
    const current=++operation;
    busy=true;error=null;page=null;schedule=null;deliveries=null;
    try{
      const result=await controller.getNextHubLesson(organization);
      if(alive&&current===operation&&result.audience===viewerRole){
        page=result;
        // Idempotent enrollment: each member retains their original
        // weekly schedule even if they log in again months later.
        try{
          const enrolled=await controller.enrollPersonalLessonSchedule(organization);
          const inbox=await controller.getPersonalLessonDeliveries(organization);
          if(alive&&current===operation&&inbox.audience===viewerRole){
            schedule=enrolled;deliveries=inbox;
          }
        }catch{
          // Reading approved content remains available if optional
          // in-app delivery enrollment is not operational yet.
          if(alive&&current===operation)deliveries=null;
        }
      }
    }catch{
      if(alive&&current===operation)error='Δεν ήταν δυνατή η ασφαλής φόρτωση του επόμενου μαθήματος.';
    }finally{
      if(alive&&current===operation)busy=false;
    }
  }
  async function complete(){
    if(busy||!page?.next)return;
    const current=++operation,submitted=page;
    busy=true;error=null;
    try{
      await controller.completeNextHubLesson(submitted);
      if(alive&&current===operation)await refresh();
    }catch{
      if(alive&&current===operation){
        // A changed, revoked or reapproved lesson cannot remain actionable.
        page=null;
        error='Το μάθημα άλλαξε ή δεν είναι διαθέσιμο. Ανανέωσε τη διαδρομή σου.';
      }
    }finally{
      if(alive&&current===operation)busy=false;
    }
  }
</script>

<section class="journey" data-testid="hub-individual-learning" aria-labelledby="journey-title">
  <header>
    <p class="eyebrow">ΛΕΥΤΕΡΙΑ FC · ΠΡΟΣΩΠΙΚΗ ΜΑΘΗΣΙΑΚΗ ΔΙΑΔΡΟΜΗ</p>
    <h2 id="journey-title">Το επόμενο εγκεκριμένο μάθημά σου</h2>
    <p class="description">Κάθε νέο μέλος ξεκινά από το πρώτο εγκεκριμένο μάθημα.
      Προχωράς με τον δικό σου ρυθμό, ανεξάρτητα από το πότε γράφτηκαν άλλοι.</p>
  </header>
  <div class="organizations" role="group" aria-label="Οργανισμός μαθημάτων">
    <button type="button" aria-pressed={organization==='lefteria_fc_academy'} class:chosen={organization==='lefteria_fc_academy'}
      disabled={busy} onclick={()=>{organization='lefteria_fc_academy';void refresh();}}>Ακαδημία</button>
    <button type="button" aria-pressed={organization==='lefteria_fc'} class:chosen={organization==='lefteria_fc'}
      disabled={busy} onclick={()=>{organization='lefteria_fc';void refresh();}}>Lefteria FC</button>
    <button type="button" disabled={busy} onclick={()=>void refresh()}>Ανανέωση</button>
  </div>
  {#if busy}
    <p role="status" class="description">Φόρτωση προσωπικής διαδρομής…</p>
  {:else if page?.next}
    <article class="lesson" id="hub-next-approved-lesson" data-testid="hub-next-approved-lesson">
      <p class="eyebrow">{viewerRole==='parent'?'ΕΚΠΑΙΔΕΥΣΗ ΓΟΝΕΩΝ':'ΕΚΠΑΙΔΕΥΣΗ ΑΘΛΗΤΩΝ'}</p>
      <p class="lesson-step" role="status">Το επόμενο μάθημα στη δική σου σειρά · εγκεκριμένο περιεχόμενο</p>
      <h3>{page.next.title}</h3>
      <div class="body">{page.next.body}</div>
      <button type="button" class="finish" disabled={busy} onclick={()=>void complete()}
        aria-describedby="hub-completion-guidance">
        Ολοκλήρωσα το μάθημα · Συνέχεια
      </button>
      <p class="description" id="hub-completion-guidance">Η ολοκλήρωση αποθηκεύεται στην προσωπική σου διαδρομή. Η επόμενη ενότητα εμφανίζεται μόνο αν έχει εγκριθεί και είναι διαθέσιμη.</p>
    </article>
  {:else if page}
    <p class="description" role="status">Έχεις ολοκληρώσει όλα τα διαθέσιμα εγκεκριμένα μαθήματα. Θα εμφανιστεί το επόμενο όταν δημοσιευθεί.</p>
  {/if}
  {#if schedule}
    <section class="schedule" aria-label="Προσωπική εβδομαδιαία ενημέρωση"
      data-testid="hub-personal-delivery-schedule">
      <strong>Εβδομαδιαία ενημέρωση μαθημάτων</strong>
      <p class="description">Η προσωπική σου σειρά διατηρείται από την αρχή, ακόμη και αν εγγράφηκες αργότερα.</p>
      <p class="description">Επόμενος προγραμματισμένος έλεγχος:
        <time datetime={schedule.next_due_at}>{cyprusDate.format(new Date(schedule.next_due_at))}</time>.
      </p>
    </section>
  {/if}
  {#if notifications.length}
    <section class="delivery-inbox" aria-labelledby="hub-lesson-notifications-title"
      data-testid="hub-individual-learning-delivery">
      <div class="delivery-heading">
        <h3 id="hub-lesson-notifications-title">Εβδομαδιαίες ειδοποιήσεις μαθημάτων</h3>
        <span aria-label={`${notifications.length} προσωπικές ειδοποιήσεις`}>{notifications.length}</span>
      </div>
      <p class="description">Μόνο οι δικές σου επαληθευμένες ειδοποιήσεις. Δεν σημαίνει ότι
        το μήνυμα διαβάστηκε ή ότι το παλιότερο υλικό εξακολουθεί να είναι διαθέσιμο.</p>
      <ol class="delivery-list">
        {#each notifications as notification (notification.id)}
          <li class="delivery-item" data-testid="hub-lesson-delivery-item">
            <span class="delivery-icon" aria-hidden="true">✦</span>
            <div>
              <strong>Προσωπική υπενθύμιση μαθήματος</strong>
              <time datetime={notification.queued_at}>
                {cyprusTime.format(new Date(notification.queued_at))}
              </time>
              {#if page?.next && notification.content_id===page.next.id}
                <a href="#hub-next-approved-lesson" class="delivery-cta">
                  Άνοιγμα του εγκεκριμένου μαθήματος ↑
                </a>
              {:else}
                <p class="delivery-previous">Η ειδοποίηση αφορά προηγούμενο μάθημα
                  της προσωπικής σου διαδρομής.</p>
              {/if}
            </div>
          </li>
        {/each}
      </ol>
    </section>
  {/if}
  {#if error}<p class="failure" role="alert">{error}</p><button type="button" onclick={()=>void refresh()}>Δοκίμασε ξανά</button>{/if}
</section>

<style>
  .journey{margin:0 0 18px;padding:22px;border-radius:14px;background:var(--lf-surface);color:var(--lf-text);border:1px solid var(--lf-brand-edge)}
  .eyebrow{color:var(--lf-gold);font-weight:700;letter-spacing:.1em;font-size:13px}
  h2{font:700 clamp(24px,5vw,31px) var(--lf-heading);margin:6px 0}
  h3{font:700 23px var(--lf-heading);margin:8px 0 14px}
  .description{color:var(--lf-muted);font-size:15px;line-height:1.7}
  .organizations{display:flex;gap:9px;flex-wrap:wrap;margin:16px 0}
  button{min-height:44px;padding:10px 15px;border:1px solid var(--lf-brand-edge);border-radius:10px;background:var(--lf-elevated);color:var(--lf-gold-light);cursor:pointer}
  button:disabled{opacity:.5;cursor:default}
  button.chosen,button:focus-visible{outline:2px solid var(--lf-gold);outline-offset:2px}
  .lesson{border-top:1px solid var(--lf-line);padding-top:12px}
  .body{white-space:pre-wrap;overflow-wrap:anywhere;line-height:1.8;font-size:15px}
  .finish{margin-top:18px;border-color:var(--lf-gold);font-weight:700}
  .failure{color:#ffcdbe;font-size:15px;line-height:1.6}
  .lesson-step{color:var(--lf-gold-light);font-size:14px;font-weight:650;line-height:1.55}
  .schedule{border:1px solid var(--lf-line);border-radius:10px;padding:14px 16px;margin-top:18px;background:var(--lf-elevated)}
  .schedule strong{font-size:16px;color:var(--lf-text)}
  .schedule p{margin:8px 0 0}
  .schedule time{color:var(--lf-gold-light);font-weight:700}
  .delivery-inbox{margin-top:18px;padding:16px;border:1px solid var(--lf-brand-edge);
    border-radius:11px;background:var(--lf-elevated)}
  .delivery-heading{display:flex;align-items:center;justify-content:space-between;gap:12px}
  .delivery-heading h3{font-size:21px;margin:0}
  .delivery-heading span{display:grid;place-items:center;min-width:31px;height:31px;
    border:1px solid var(--lf-gold);border-radius:50%;color:var(--lf-gold-light);font-weight:800}
  .delivery-inbox>.description{font-size:12px;margin:9px 0 12px}
  .delivery-list{margin:0;padding:0;list-style:none;display:grid;gap:9px}
  .delivery-item{display:grid;grid-template-columns:30px minmax(0,1fr);
    gap:12px;align-items:start;padding:13px 12px;background:var(--lf-surface);
    border:1px solid var(--lf-line);border-radius:9px}
  .delivery-icon{display:grid;place-items:center;width:30px;height:30px;
    border-radius:9px;color:var(--lf-gold);background:var(--lf-elevated)}
  .delivery-item strong{display:block;font-size:14px;line-height:1.5}
  .delivery-item time{display:block;margin-top:3px;color:var(--lf-muted);font-size:12px}
  .delivery-cta{display:inline-flex;align-items:center;min-height:44px;
    margin-top:7px;color:var(--lf-gold-light);text-decoration:underline;
    text-underline-offset:3px;font-weight:700;font-size:13px}
  .delivery-cta:focus-visible{outline:2px solid var(--lf-gold);outline-offset:3px}
  .delivery-previous{font-size:12px;color:var(--lf-muted);line-height:1.5;margin:7px 0 0}
  .finish{width:min(100%,400px);font-size:16px;line-height:1.4}
  @media(max-width:420px){.journey{padding:16px}.organizations button{flex:1 1 auto}.body{font-size:16px}.finish{width:100%}}
</style>
