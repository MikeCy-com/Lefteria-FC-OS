<script lang="ts">
  import {onMount} from 'svelte';
  import type {MobileAuthController} from '../controller.ts';
  import type {MobileCalendar} from '../calendar.ts';
  import {clubToday,formatCalendarDate,CALENDAR_KIND_LABELS,isCalendarKind} from '../calendar.ts';
  import {validParentTrainingFees} from '../finance.ts';
  import {calendarActions,feeActions} from '../member-actions.ts';

  let {controller,role,onCalendar,onFinancials}:{
    controller:MobileAuthController;
    role:'parent'|'player'|'coach'|'management';
    onCalendar:(childId?:string)=>void|Promise<void>;
    onFinancials:()=>void|Promise<void>;
  }=$props();
  let calendar=$state<MobileCalendar|null>(null);
  let fees=$state<ReturnType<typeof feeActions>|null>(null);
  let calendarStatus=$state<'loading'|'ready'|'error'>('loading');
  let feeStatus=$state<'loading'|'ready'|'error'>('loading');
  let today=$state(clubToday());
  let selectedChild=$state<string|undefined>(undefined);
  let request=0,feeRequest=0,alive=false;
  const actions=$derived(calendar?calendarActions(calendar,today):null);
  const children=$derived(calendar?.players??[]);
  const busy=$derived(calendarStatus==='loading'||(role==='parent'&&feeStatus==='loading'));

  async function loadCalendar(childId?:string){
    const marker=++request;
    const day=clubToday();
    calendar=null;calendarStatus='loading';today=day;
    try{
      const data=await controller.getMemberActionCalendar(day.slice(0,7),childId);
      if(!alive||marker!==request)return;
      calendar=data;
      selectedChild=role==='parent'?data.selected_player_id??undefined:undefined;
      calendarStatus='ready';
    }catch{
      if(alive&&marker===request){calendar=null;calendarStatus='error';}
    }
  }
  async function loadFees(){
    const marker=++feeRequest;
    fees=null;feeStatus='loading';
    if(role!=='parent')return;
    try{
      const raw=await controller.getTrainingFees();
      if(!alive||marker!==feeRequest)return;
      if(!validParentTrainingFees(raw))throw new Error('Unverified fees.');
      fees=feeActions(raw);feeStatus='ready';
    }catch{if(alive&&marker===feeRequest){fees=null;feeStatus='error';}}
  }
  function refresh(){
    if(!alive)return;
    void loadCalendar(selectedChild);
    if(role==='parent')void loadFees();
  }
  onMount(()=>{
    alive=true;refresh();
    function visibility(){
      if(document.visibilityState==='hidden'){
        // Do not keep private snapshots visible on return before rechecking.
        request++;feeRequest++;calendar=null;fees=null;
        calendarStatus='loading';feeStatus='loading';
      }else refresh();
    }
    document.addEventListener('visibilitychange',visibility);
    return()=>{alive=false;request++;feeRequest++;
      document.removeEventListener('visibilitychange',visibility);};
  });
</script>

<section class="actions-panel" aria-label="Ενέργειες και ενημερώσεις" data-testid="mobile-member-actions">
  <header>
    <div><p class="eyebrow">LEFTERIA FC · APP</p><h2>Ενέργειες & ενημερώσεις</h2></div>
    <button type="button" class="outline" disabled={busy} onclick={refresh}>Ανανέωση</button>
  </header>
  <p class="hint">Τρέχον πρόγραμμα και εκκρεμότητες από τις αντίστοιχες υπηρεσίες.
    Δεν προσμετρώνται στις μη αναγνωσμένες ανακοινώσεις.</p>
  <section class="action-card" aria-label="Πρόγραμμα">
    <div class="section-head"><h3>Πρόγραμμα</h3>
      <button type="button" class="outline" disabled={calendarStatus!=='ready'}
        onclick={()=>void onCalendar(selectedChild)}>Ημερολόγιο ›</button></div>
    {#if calendarStatus==='loading'}
      <p role="status">Έλεγχος προγράμματος…</p>
    {:else if calendarStatus==='error'}
      <p role="alert">Το πρόγραμμα δεν επαληθεύτηκε.</p>
      <button type="button" class="outline" onclick={()=>void loadCalendar()}>Δοκίμασε ξανά</button>
    {:else if actions&&calendar}
      {#if role==='parent'&&children.length}
        <label>Παιδί
          <select value={selectedChild} onchange={event=>{
            const id=event.currentTarget.value;
            if(children.some(child=>child.id===id))void loadCalendar(id);
          }}>
            {#each children as child (child.id)}<option value={child.id}>{child.name}</option>{/each}
          </select>
        </label>
      {/if}
      <p class="hint">Από σήμερα έως το τέλος του τρέχοντος μήνα.</p>
      {#if role==='parent'&&!calendar.selected_player_id}
        <p>Δεν υπάρχει επαληθευμένη σύνδεση παιδιού.</p>
      {:else if role==='player'&&!calendar.selected_player_id}
        <p>Δεν υπάρχει επαληθευμένη σύνδεση αθλητή.</p>
      {:else}
        {#if actions.cancelled.length}
          <h4>Ακυρωμένες δραστηριότητες</h4>
          <ul>{#each actions.cancelled as item (item.kind+':'+item.id)}
            <li><strong>{item.title}</strong>
              <span>{formatCalendarDate(item.date,{day:'numeric',month:'short'})} · {isCalendarKind(item.kind)?CALENDAR_KIND_LABELS[item.kind]:'Δραστηριότητα'}</span>
              <span class="cancelled">Ακυρώθηκε</span>
            </li>
          {/each}</ul>
        {/if}
        {#if actions.unanswered.length}
          <h4>Χωρίς δήλωση συμμετοχής</h4>
          <ul>{#each actions.unanswered as item (item.kind+':'+item.id)}
            <li><strong>{item.title}</strong>
              <span>{formatCalendarDate(item.date,{day:'numeric',month:'short'})}{item.start_time?' · '+item.start_time:''}</span>
            </li>
          {/each}</ul>
        {/if}
        {#if !actions.cancelled.length&&!actions.unanswered.length}
          <p>Δεν εμφανίζονται ακυρώσεις ή εκκρεμείς δηλώσεις στην ελεγμένη σελίδα.</p>
        {/if}
        {#if actions.limited}<p class="hint">Περισσότερες καταχωρίσεις μπορεί να υπάρχουν στο ημερολόγιο.</p>{/if}
      {/if}
    {/if}
  </section>
  {#if role==='parent'}
    <section class="action-card" aria-label="Οικονομικά Ακαδημίας">
      <div class="section-head"><h3>Οικονομικά Ακαδημίας</h3>
        <button type="button" class="outline" onclick={()=>void onFinancials()}>Χρεώσεις ›</button></div>
      {#if feeStatus==='loading'}<p role="status">Έλεγχος χρεώσεων…</p>
      {:else if feeStatus==='error'}<p role="alert">Οι χρεώσεις δεν επαληθεύτηκαν.</p>
        <button type="button" class="outline" onclick={()=>void loadFees()}>Δοκίμασε ξανά</button>
      {:else if fees}
        <dl class="fee-counts">
          <div><dt>Εκκρεμείς χρεώσεις</dt><dd>{fees.outstanding}</dd></div>
          <div><dt>Αναμονή εκκαθάρισης</dt><dd>{fees.settling}</dd></div>
          <div><dt>Χρειάζονται έλεγχο</dt><dd>{fees.review}</dd></div>
        </dl>
        <p class="hint">Μόνο εκδοθείσες χρεώσεις του λογαριασμού. Η εκκαθάριση ή ο έλεγχος δεν αποτελεί αίτημα νέας πληρωμής.</p>
      {/if}
    </section>
  {/if}
</section>

<style>
  .actions-panel{padding:16px 14px;color:var(--lf-text);font-family:var(--lf-body);font-size:var(--lf-type-body)}
  header,.section-head{display:flex;justify-content:space-between;align-items:center;gap:8px;flex-wrap:wrap}
  .eyebrow{font-size:10px;letter-spacing:.12em;color:var(--lf-gold);margin:0 0 4px}
  h2,h3,h4{margin:0;font-family:var(--lf-heading)}h2{font-size:23px}h3{font-size:19px}h4{font-size:15px;margin:12px 0 6px}
  .hint{color:var(--lf-muted);font-size:12px;line-height:1.5}
  .action-card{padding:12px;margin-top:12px;border:1px solid var(--lf-line);border-radius:12px;background:var(--lf-surface)}
  .outline,select{min-height:44px;border:1px solid var(--lf-brand-edge);background:var(--lf-elevated);color:var(--lf-text);border-radius:9px;padding:8px 10px}
  .outline{color:var(--lf-gold-light);cursor:pointer}.outline:disabled{opacity:.5;cursor:default}
  button:focus-visible,select:focus-visible{outline:2px solid var(--lf-gold);outline-offset:2px}
  label{display:grid;gap:5px;margin:10px 0;color:var(--lf-muted)}select{width:100%;min-width:0}
  ul{list-style:none;margin:0;padding:0;display:grid;gap:7px}li{display:grid;gap:4px;border-bottom:1px solid var(--lf-line);padding:8px 0;overflow-wrap:anywhere}
  li span{font-size:12px;color:var(--lf-muted)}li .cancelled{color:var(--lf-gold-light)}
  .fee-counts{margin:10px 0;display:grid;gap:5px}.fee-counts div{display:flex;justify-content:space-between;gap:10px;padding:6px 0}
  dd{margin:0;font-weight:700;font-variant-numeric:tabular-nums}
</style>
