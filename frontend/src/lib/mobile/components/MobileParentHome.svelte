<script lang="ts">
  import { untrack } from 'svelte';
  import { CALENDAR_KIND_LABELS, clubToday, formatCalendarDate } from '../calendar.ts';
  import type { CalendarLoadStatus,AvailabilityStatus,MobileCalendarItem } from '../calendar.ts';
  import { matchingParentHomeCalendar, parentHomeCalendarMonth, parentHomeSchedule,
    parentHomeRsvp, parentHomeCancelledEvents, parentHomePendingResponses } from '../parent-home.ts';
  import type { Family, ParentCalendar } from '../parent-home.ts';
  import './mobile-theme.css';
  import { publicImages } from '../../public/images.ts';

  interface Props {
    identityId: string;
    parentName: string;
    family: Family | null;
    familyStatus: 'idle' | 'loading' | 'ready' | 'error';
    familyError: string | null;
    calendar: ParentCalendar | null;
    calendarStatus: CalendarLoadStatus;
    calendarError: string | null;
    busy?: boolean;
    today?: string;
    onLoadFamily: () => boolean | Promise<boolean>;
    onLoadCalendar: (month: string, childId: string) => boolean | Promise<boolean>;
    onChildren: () => void | Promise<void>;
    onCalendar: (childId: string) => void | Promise<void>;
    onEducation?: () => void | Promise<void>;
    onMessages?: () => void | Promise<void>;
    onFinancials?: () => void | Promise<void>;
    onRespond?: (event:MobileCalendarItem,status:AvailabilityStatus) => boolean | Promise<boolean>;
  }

  let { identityId, parentName, family, familyStatus, familyError, calendar, calendarStatus, calendarError,
    busy = false, today = clubToday(), onLoadFamily, onLoadCalendar, onChildren, onCalendar, onEducation, onMessages, onFinancials, onRespond }: Props = $props();

  const id = $props.id();
  let familyRequested = $state(false);
  let calendarRequestedFor = $state<string | null>(null);
  let selectedChildId = $state<string | null>(null);
  const firstChild = $derived(familyStatus === 'ready' ? family?.children[0] ?? null : null);
  // Only linked children in the latest server-verified family may be selected.
  const activeChild = $derived(familyStatus === 'ready'
    ? family?.children.find(child=>child.id===selectedChildId) ?? firstChild : null);
  const trustworthyCalendar = $derived(familyStatus === 'ready' &&
    matchingParentHomeCalendar(family, calendar, identityId, today, activeChild?.id) && calendarStatus === 'ready');
  const upcoming = $derived(trustworthyCalendar ? parentHomeSchedule(family, calendar, identityId, today, activeChild?.id) : []);
  const cancellations = $derived(trustworthyCalendar ? parentHomeCancelledEvents(family,calendar,identityId,today,activeChild?.id) : []);
  const pendingResponses = $derived(trustworthyCalendar && onRespond
    ? parentHomePendingResponses(family,calendar,identityId,today,activeChild?.id):[]);

  // Every new dashboard mount rechecks guardian bindings. Cached sibling
  // selections and calendar data are not treated as lasting authorization.
  $effect(() => {
    if (!familyRequested && !busy) {
      familyRequested = true;
      untrack(() => { void onLoadFamily(); });
    }
  });

  $effect(() => {
    if (familyStatus === 'ready' && activeChild && !busy && calendarRequestedFor !== activeChild.id) {
      calendarRequestedFor = activeChild.id;
      untrack(() => { void onLoadCalendar(parentHomeCalendarMonth(today), activeChild.id); });
    }
  });

  function organizationLabel(id: string): string {
    return id === 'lefteria_fc_academy' ? 'ΑΚΑΔΗΜΙΑ' : 'ΛΕΥΤΕΡΙΑ FC';
  }
  function calendarDateLabel(day: string): string {
    return formatCalendarDate(day, { weekday: 'short', day: 'numeric', month: 'short' });
  }
</script>

<section class="parent-home" aria-labelledby={id + '-home-heading'} data-testid="parent-dashboard-home">
  <header class="parent-greeting" data-testid="parent-photo-hero"
    style:background-image={`linear-gradient(90deg,#080909f4 0%,#080909e3 34%,#08090962 75%,#08090927),linear-gradient(0deg,#090a0bdd,transparent 40%),url("${publicImages.hero}")`}>
    <p class="parent-eyebrow">ΚΑΛΩΣ ΗΡΘΕΣ</p>
    <h1 id={id + '-home-heading'}>{parentName || 'Γονέας'}</h1>
    <p class="parent-hero-description">Το πρόγραμμα, οι αγώνες και η πορεία των παιδιών σου,
      όλα σε ένα μέρος με επαληθευμένη οικογενειακή πρόσβαση.</p>
    <button class="parent-settings" type="button" onclick={() => void onChildren()} disabled={busy}
      data-testid="parent-home-family-link">Οικογενειακές ρυθμίσεις <span aria-hidden="true">›</span></button>
  </header>

  <section class="parent-section" aria-labelledby={id + '-children-heading'} data-testid="parent-children-overview">
    <div class="parent-section-heading">
      <h2 id={id + '-children-heading'}>Τα παιδιά μου</h2>
      <button type="button" disabled={busy} onclick={() => void onChildren()}>Όλα τα παιδιά <span aria-hidden="true">›</span></button>
    </div>
    {#if familyStatus === 'idle' || familyStatus === 'loading'}
      <p class="parent-notice" role="status" data-testid="parent-home-family-loading">Έλεγχος επαληθευμένων συνδέσεων…</p>
    {:else if familyStatus === 'error'}
      <div class="parent-notice parent-error" role="alert" data-testid="parent-home-family-error">
        <p>{familyError || 'Τα στοιχεία της οικογένειας δεν είναι διαθέσιμα.'}</p>
        <button type="button" disabled={busy} onclick={() => void onLoadFamily()}>Δοκιμή ξανά</button>
      </div>
    {:else if family}
      {#if family.children.length === 0}
        <p class="parent-notice" data-testid="parent-home-no-children">Δεν υπάρχουν επαληθευμένες συνδέσεις παιδιών για αυτόν τον λογαριασμό.</p>
      {:else}
        <div class="parent-child-list" data-testid="parent-home-child-list">
          {#each family.children as child (child.id)}
            <article class="parent-child-card" data-testid="parent-home-child">
              <span class="parent-child-avatar" aria-hidden="true">{child.name.trim().charAt(0).toLocaleUpperCase('el-GR')}</span>
              <div class="parent-child-copy">
                <h3>{child.name}</h3>
                <p>{organizationLabel(child.organization_id)} · {child.team_type === 'Academy' ? 'Ακαδημία' : 'Πρώτη ομάδα'}</p>
              </div>
              <button type="button" class="parent-select-schedule"
                aria-label={'Εμφάνιση προγράμματος για '+child.name}
                aria-pressed={activeChild?.id===child.id}
                disabled={busy}
                onclick={()=>{selectedChildId=child.id;}}
                data-testid="parent-home-select-child">Πρόγραμμα</button>
              <button type="button" aria-label={'Πρόγραμμα για ' + child.name} disabled={busy}
                onclick={() => void onCalendar(child.id)} data-testid="parent-home-child-calendar"><span aria-hidden="true">›</span></button>
            </article>
          {/each}
        </div>
      {/if}
    {/if}
  </section>

  {#if cancellations.length && activeChild}
    <section class="parent-section parent-cancellations" role="status"
      aria-labelledby={id+'-cancellations-heading'} data-testid="parent-home-cancellations">
      <div class="parent-section-heading">
        <h2 id={id+'-cancellations-heading'}>⚠️ Ακυρώσεις προγράμματος</h2>
        <button type="button" disabled={busy}
          onclick={()=>void onCalendar(activeChild.id)}>Ημερολόγιο <span aria-hidden="true">›</span></button>
      </div>
      <p class="parent-scope">Για {activeChild.name} · επαληθευμένες αλλαγές τρέχοντος μήνα</p>
      <ul class="cancelled-list">
        {#each cancellations as event (event.kind+':'+event.id)}
          <li>
            <strong>{event.title}</strong>
            <span>{calendarDateLabel(event.date)}{event.start_time?' · '+event.start_time:''}</span>
            <p>{event.cancel_reason?.trim()||'Η δραστηριότητα ακυρώθηκε από τη διοίκηση.'}</p>
          </li>
        {/each}
      </ul>
      <p class="parent-cancel-note">Οι παλιές δηλώσεις συμμετοχής διατηρούνται για το ιστορικό. Δεν επιτρέπονται νέες απαντήσεις σε ακυρωμένη δραστηριότητα.</p>
    </section>
  {/if}

  {#if activeChild && pendingResponses.length>0 && onRespond}
    <section class="parent-section parent-response-queue"
      aria-labelledby={id+'-responses-heading'} data-testid="parent-home-response-queue">
      <div class="parent-section-heading">
        <div>
          <p class="parent-response-eyebrow">ΧΡΕΙΑΖΕΤΑΙ ΑΠΑΝΤΗΣΗ</p>
          <h2 id={id+'-responses-heading'}>Δηλώσεις συμμετοχής</h2>
        </div>
        <button type="button" disabled={busy} onclick={()=>void onCalendar(activeChild.id)}>
          Πλήρες ημερολόγιο <span aria-hidden="true">›</span>
        </button>
      </div>
      <p class="parent-scope">Για {activeChild.name} · δραστηριότητες χωρίς
        καταχωρισμένη απάντηση στο επιβεβαιωμένο πρόγραμμα.</p>
      <ul class="parent-response-list">
        {#each pendingResponses as event (event.kind+':'+event.id)}
          <li data-testid="parent-home-response-needed">
            <div>
              <strong>{event.title}</strong>
              <span>{calendarDateLabel(event.date)}
                {event.start_time?' · '+event.start_time:''}</span>
            </div>
            <div class="parent-response-buttons" role="group"
              aria-label={'Συμμετοχή '+activeChild.name+' · '+event.title}>
              <button type="button" disabled={busy}
                onclick={()=>void onRespond?.(event,'going')}>Θα έρθει</button>
              <button type="button" disabled={busy}
                onclick={()=>void onRespond?.(event,'not_going')}>Δεν θα έρθει</button>
              <button type="button" disabled={busy}
                onclick={()=>void onRespond?.(event,'maybe')}>Ίσως</button>
            </div>
          </li>
        {/each}
      </ul>
      <p class="parent-response-note">Εμφανίζονται μόνο οι επόμενες διαθέσιμες
        δραστηριότητες αυτού του παιδιού. Η απάντηση ενημερώνεται όταν επιβεβαιωθεί από το σύστημα.</p>
    </section>
  {/if}

  <section class="parent-section" aria-labelledby={id + '-schedule-heading'} data-testid="parent-upcoming-schedule">
    <div class="parent-section-heading">
      <h2 id={id + '-schedule-heading'}>Σήμερα & επόμενα</h2>
      {#if activeChild}
        <button type="button" disabled={busy} onclick={() => void onCalendar(activeChild.id)}
          data-testid="parent-home-open-calendar">Ημερολόγιο <span aria-hidden="true">›</span></button>
      {/if}
    </div>
    {#if activeChild}
      <p class="parent-scope" data-testid="parent-home-schedule-scope">Πρόγραμμα: {activeChild.name} · {formatCalendarDate(today, { month: 'long', year: 'numeric' })}</p>
      {#if calendarStatus === 'error'}
        <div class="parent-notice parent-error" role="alert" data-testid="parent-home-calendar-error">
          <p>{calendarError || 'Το πρόγραμμα δεν είναι διαθέσιμο.'}</p>
          <button type="button" disabled={busy} onclick={() => void onLoadCalendar(parentHomeCalendarMonth(today), activeChild.id)}>Δοκιμή ξανά</button>
        </div>
      {:else if !trustworthyCalendar}
        <p class="parent-notice" role="status" data-testid="parent-home-calendar-loading">Φόρτωση προγράμματος…</p>
      {:else if upcoming.length === 0}
        <p class="parent-notice" data-testid="parent-home-calendar-empty">
          {calendar?.next_offset != null ? 'Υπάρχουν περισσότερες καταχωρίσεις στο ημερολόγιο.' : 'Δεν υπάρχουν άλλες δημοσιευμένες δραστηριότητες αυτόν τον μήνα.'}
        </p>
      {:else}
        <div class="parent-schedule-list" data-testid="parent-home-events">
          {#each upcoming as event (event.kind + ':' + event.id)}
            {@const ownRsvp=parentHomeRsvp(event,activeChild.id)}
            <article class="parent-event" data-testid="parent-home-event">
              <div class="parent-event-date">
                <strong>{event.start_time || '—'}</strong>
                <span>{calendarDateLabel(event.date)}</span>
              </div>
              <div class="parent-event-icon" aria-hidden="true">{event.kind === 'match' ? '⚽' : event.kind === 'training' ? '◈' : '▦'}</div>
              <div class="parent-event-copy">
                <span>{CALENDAR_KIND_LABELS[event.kind as keyof typeof CALENDAR_KIND_LABELS] ?? 'Δραστηριότητα'}</span>
                <h3>{event.title}</h3>
                {#if event.location}<p>{event.location}</p>{/if}
                <p class="parent-rsvp" data-testid="parent-home-event-rsvp">
                  Δήλωση συμμετοχής: <strong>{ownRsvp==='going'?'Θα έρθει':ownRsvp==='not_going'?'Δεν θα έρθει':ownRsvp==='maybe'?'Ίσως':'Χωρίς απάντηση'}</strong>
                </p>
                {#if onRespond && !event.is_cancelled}
                  <fieldset class="parent-home-rsvp-actions" disabled={busy} data-testid="parent-home-rsvp-actions">
                    <legend>Απάντηση συμμετοχής για {activeChild.name}</legend>
                    {#each [['going','Θα έρθει'],['not_going','Δεν θα έρθει'],['maybe','Ίσως']] as choice}
                      <button type="button" aria-pressed={ownRsvp===choice[0]}
                        onclick={()=>void onRespond?.(event,choice[0] as AvailabilityStatus)}>{choice[1]}</button>
                    {/each}
                  </fieldset>
                {/if}
              </div>
              <button type="button" aria-label={'Ημερολόγιο για ' + activeChild.name} disabled={busy}
                onclick={() => void onCalendar(activeChild.id)}><span aria-hidden="true">›</span></button>
            </article>
          {/each}
          {#if calendar?.next_offset != null}
            <p class="parent-notice parent-more-note">Δες το πλήρες ημερολόγιο για επιπλέον δραστηριότητες.</p>
          {/if}
        </div>
      {/if}
    {:else if familyStatus === 'ready'}
      <p class="parent-notice" data-testid="parent-home-calendar-no-child">Δεν υπάρχει συνδεδεμένο παιδί με διαθέσιμο πρόγραμμα.</p>
    {:else}
      <p class="parent-notice" role="status">Το πρόγραμμα εμφανίζεται μετά τον έλεγχο των οικογενειακών συνδέσεων.</p>
    {/if}
  </section>

  <section class="parent-section parent-shortcuts" aria-labelledby={id + '-shortcuts-heading'} data-testid="parent-home-shortcuts">
    <h2 id={id + '-shortcuts-heading'}>Γρήγορη πρόσβαση</h2>
    <div class="parent-shortcut-grid">
      <button type="button" disabled={busy} onclick={() => void onChildren()} data-testid="parent-shortcut-children">
        <span aria-hidden="true">♧</span><strong>Τα παιδιά μου</strong>
      </button>
      {#if activeChild}
        <button type="button" disabled={busy} onclick={() => void onCalendar(activeChild.id)} data-testid="parent-shortcut-calendar">
          <span aria-hidden="true">▦</span><strong>Πρόγραμμα</strong>
        </button>
      {/if}
      {#if onEducation}
        <button type="button" disabled={busy} onclick={() => void onEducation()} data-testid="parent-shortcut-education">
          <span aria-hidden="true">✦</span><strong>Εκπαίδευση</strong>
        </button>
      {:else}
        <div class="parent-shortcut-pending"><span aria-hidden="true">✦</span><strong>Εκπαίδευση</strong><small>Under Developments</small></div>
      {/if}
      {#if onMessages}
        <button type="button" disabled={busy} onclick={()=>void onMessages()} data-testid="parent-shortcut-messages">
          <span aria-hidden="true">✉</span><strong>Μηνύματα</strong>
        </button>
      {:else}
        <div class="parent-shortcut-pending"><span aria-hidden="true">✉</span><strong>Μηνύματα</strong><small>Under Developments</small></div>
      {/if}
      {#if onFinancials}
        <button type="button" disabled={busy} onclick={()=>void onFinancials()} data-testid="parent-shortcut-financials">
          <span aria-hidden="true">€</span><strong>Πληρωμές</strong>
        </button>
      {:else}
        <div class="parent-shortcut-pending"><span aria-hidden="true">€</span><strong>Πληρωμές</strong><small>Under Developments</small></div>
      {/if}
    </div>
  </section>

  <aside class="parent-motto" data-testid="parent-home-motto">
    <span>ΛΕΥΤΕΡΙΑ FC · ΑΚΑΔΗΜΙΑ</span>
    <strong>Μαζί για το μέλλον τους.</strong>
    <p>ΜΙΑ ΟΜΑΔΑ. ΜΙΑ ΟΙΚΟΓΕΝΕΙΑ.</p>
  </aside>
</section>

<style>
  .parent-home{width:min(100%,920px);margin:0 auto;padding:2px 0 18px;color:#f5f5f5}
  .parent-greeting{position:relative;isolation:isolate;overflow:hidden;
    min-height:clamp(210px,26vw,310px);display:flex;flex-direction:column;
    align-items:flex-start;justify-content:flex-end;
    margin:0 0 22px;padding:clamp(20px,4vw,36px);
    border:1px solid #ae883c;border-radius:11px;
    background-size:cover;background-position:65% 43%;
    box-shadow:inset 0 0 0 1px #ffffff18,0 8px 30px #0005}
  .parent-eyebrow{color:#f6bb38;font-size:13px;font-weight:700;letter-spacing:.09em;margin:0 0 6px}
  .parent-greeting h1{font-family:'Fira Sans Extra Condensed',sans-serif;
    font-size:clamp(34px,7vw,56px);font-style:italic;font-weight:850;
    margin:0 0 10px;line-height:1.04;overflow-wrap:anywhere;
    text-shadow:0 3px 16px #000b}
  .parent-hero-description{max-width:40ch;font-size:clamp(13px,2.7vw,16px);
    line-height:1.5;margin:0 0 15px;color:#f0efea;text-shadow:0 2px 9px #000d}
  .parent-settings{min-height:44px;display:inline-flex;align-items:center;gap:10px;padding:10px 14px;border:1px solid #63502c;border-radius:8px;color:#ffca46;background:#211b12;font-size:15px;cursor:pointer}
  .parent-settings span,.parent-section-heading button span{font-size:20px;line-height:1}
  .parent-section{margin:0 0 26px}
  .parent-section-heading{display:flex;justify-content:space-between;align-items:center;gap:16px;margin-bottom:12px}
  .parent-section h2{font-size:21px;margin:0}
  .parent-section-heading button{display:flex;align-items:center;gap:5px;min-height:44px;color:#f6bb38;border:0;background:transparent;font-size:15px;cursor:pointer}
  .parent-notice{margin:0;padding:19px;background:#131516;border:1px solid #2b2d2f;border-radius:10px;color:#b3b8bb;line-height:1.65;font-size:15px}
  .parent-error{border-color:#ad5d58}
  .parent-error p{margin:0 0 12px}
  .parent-error button{min-height:44px;padding:9px 14px;background:#261f11;border:1px solid #9c7c3c;color:#ffca46;border-radius:5px;cursor:pointer}
  .parent-child-list{display:grid;gap:10px}
  .parent-child-card{display:flex;align-items:center;gap:13px;min-width:0;padding:13px;background:linear-gradient(135deg,#191b1c,#101213);border:1px solid #303335;border-radius:12px}
  .parent-child-avatar{width:49px;height:49px;flex:0 0 49px;display:grid;place-items:center;border:1px solid #6c5122;border-radius:11px;background:#302311;color:#ffca46;font-size:20px;font-weight:700}
  .parent-child-copy{min-width:0;flex:1}
  .parent-child-copy h3{font-size:19px;margin:0 0 5px;overflow-wrap:anywhere}
  .parent-child-copy p{margin:0;color:#c0c6c8;font-size:14px}
  .parent-child-card .parent-select-schedule{flex:0 0 auto;width:auto;min-width:76px;padding:10px;font-size:14px;min-height:44px;border:1px solid #62502c;border-radius:7px;background:#201d15;color:#ffca46}
  .parent-child-card .parent-select-schedule[aria-pressed='true']{background:#6b4a18;border-color:#f6bb38;color:#fff}
  .parent-child-card>button:not(.parent-select-schedule),.parent-event>button{display:grid;place-items:center;flex:0 0 44px;width:44px;height:44px;border:0;background:transparent;color:#ffca46;font-size:25px;cursor:pointer}
  .parent-scope{margin:0 0 12px;color:#c0c6c8;font-size:14px}
  .parent-response-queue{border:1px solid #5d4b2b;border-radius:12px;padding:18px;
    background:linear-gradient(125deg,#211c13,#131515 70%)}
  .parent-response-eyebrow{margin:0 0 5px;color:#f6bb38;font-size:11px;
    font-weight:800;letter-spacing:.12em}
  .parent-response-list{list-style:none;display:grid;gap:10px;margin:0;padding:0}
  .parent-response-list li{padding:13px;display:flex;flex-wrap:wrap;gap:12px;
    justify-content:space-between;align-items:center;border:1px solid #594628;border-radius:9px;
    background:#131516}
  .parent-response-list li>div:first-child{display:grid;gap:5px;flex:1;min-width:180px}
  .parent-response-list strong{font-size:15px;line-height:1.3}
  .parent-response-list span{font-size:12px;color:#d4bf96}
  .parent-response-buttons{display:flex;flex-wrap:wrap;gap:6px}
  .parent-response-buttons button{min-height:44px;border:1px solid #68522b;
    color:#ffca46;background:#292116;border-radius:7px;padding:8px 12px;
    font-size:13px;font-weight:700;cursor:pointer}
  .parent-response-buttons button:disabled{opacity:.5;cursor:wait}
  .parent-response-buttons button:focus-visible{outline:2px solid #ffca46;outline-offset:2px}
  .parent-response-note{font-size:12px;color:#c3b9aa;margin:12px 0 0;line-height:1.5}
  .parent-cancellations{border-color:#b36832;background:linear-gradient(130deg,#2b1b11,#17191a)}
  .parent-cancellations h2{color:#ffbf86}
  .cancelled-list{list-style:none;margin:0;padding:0;display:grid;gap:8px}
  .cancelled-list li{display:grid;gap:5px;padding:11px;border:1px solid #825334;border-radius:9px;background:#241b17}
  .cancelled-list strong{font-size:14px;color:#fff;overflow-wrap:anywhere}
  .cancelled-list span{font-size:13px;color:#ffc698}
  .cancelled-list p{font-size:14px;color:#e6d4c4;margin:0;overflow-wrap:anywhere}
  .parent-cancel-note{margin:10px 0 0;color:#ceb9a9;font-size:14px;line-height:1.6}
  .parent-schedule-list{border:1px solid #292d2e;border-radius:12px;overflow:hidden}
  .parent-event{display:flex;align-items:center;gap:12px;padding:15px 10px;background:#131516;border-bottom:1px solid #282b2d}
  .parent-event:last-child{border-bottom:0}
  .parent-event-date{flex:0 0 76px;text-align:center}
  .parent-event-date strong{display:block;color:#fff;font-size:15px}
  .parent-event-date span{display:block;margin-top:5px;color:#c0c6c8;font-size:13px}
  .parent-event-icon{flex:0 0 37px;width:37px;height:37px;display:grid;place-items:center;border-radius:11px;background:#f6bb3818;color:#ffca46;font-size:18px}
  .parent-event-copy{min-width:0;flex:1}
  .parent-event-copy>span{color:#f6bb38;font-size:13px}
  .parent-event-copy h3{margin:4px 0;font-size:17px;line-height:1.3;overflow-wrap:anywhere}
  .parent-event-copy p{margin:0;color:#c0c6c8;font-size:14px}
  .parent-event-copy .parent-rsvp{margin-top:6px;color:#d6cebf;font-size:14px}
  .parent-rsvp strong{color:#ffca46}
  .parent-home-rsvp-actions{border:0;padding:0;margin:8px 0 0;display:flex;flex-wrap:wrap;gap:5px}
  .parent-home-rsvp-actions legend{font-size:14px;color:#aab0b2;margin-bottom:5px}
  .parent-home-rsvp-actions button{min-height:44px;border:1px solid #514731;border-radius:7px;background:#23201a;color:#e6d5b2;padding:9px 11px;font-size:14px;cursor:pointer}
  .parent-home-rsvp-actions button[aria-pressed='true']{border-color:#f6bb38;background:#493515;color:#fff;font-weight:700}
  .parent-home-rsvp-actions button:focus-visible{outline:2px solid #ffca46;outline-offset:2px}
  .parent-home-rsvp-actions:disabled button{opacity:.5;cursor:wait}
  .parent-more-note{border:0;border-radius:0;padding:14px;font-size:14px}
  .parent-shortcuts h2{margin-bottom:12px}
  .parent-shortcut-grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:10px}
  .parent-shortcut-grid>button,.parent-shortcut-pending{min-height:103px;min-width:0;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:8px;padding:11px 6px;background:#141617;border:1px solid #2a2d2f;border-radius:11px;text-align:center}
  .parent-shortcut-grid>button{color:#f5f5f5;cursor:pointer}
  .parent-shortcut-grid>button span,.parent-shortcut-pending>span{font-size:24px;color:#ffca46}
  .parent-shortcut-grid strong{font-size:15px;overflow-wrap:anywhere}
  .parent-shortcut-pending{color:#a4abad}
  .parent-shortcut-pending small{font-size:12px;color:#b1a085}
  .parent-motto{margin-top:27px;padding:23px;border:1px solid #5b431b;border-radius:14px;background:linear-gradient(125deg,#31230f,#111313 70%)}
  .parent-motto span{display:block;color:#f6bb38;font-size:13px;margin-bottom:9px}
  .parent-motto strong{font-family:'Fira Sans Extra Condensed',sans-serif;font-size:27px}
  .parent-motto p{font-size:14px;color:#b2b5b4;margin:10px 0 0}
  .parent-child-card>button:focus-visible,.parent-event>button:focus-visible,.parent-shortcut-grid>button:focus-visible,
  .parent-settings:focus-visible,.parent-section-heading button:focus-visible,.parent-error button:focus-visible,
  .parent-select-schedule:focus-visible{outline:2px solid #ffca46;outline-offset:3px}
  .parent-shortcut-grid>button:disabled,.parent-child-card button:disabled{opacity:.55;cursor:wait}
  @media(max-width:640px){
    .parent-greeting{min-height:265px;padding:19px;border-radius:8px;
      background-position:61% center;}
    .parent-greeting h1{font-size:clamp(31px,9vw,46px);}
    .parent-hero-description{font-size:13px;max-width:29ch}

    .parent-shortcut-grid{grid-template-columns:repeat(2,minmax(0,1fr))}
    .parent-section-heading{flex-wrap:wrap;gap:8px}
    .parent-child-card{flex-wrap:wrap}
    .parent-child-copy{flex-basis:calc(100% - 115px)}
    .parent-event{gap:9px;align-items:flex-start;flex-wrap:wrap}
    .parent-event-date{flex-basis:64px}
    .parent-event-icon{width:32px;height:32px;flex-basis:32px}
    .parent-event-copy{flex-basis:calc(100% - 126px)}
    .parent-home-rsvp-actions{gap:8px}
    .parent-home-rsvp-actions button{flex:1 1 auto}
  }
  @media(max-width:359px){
    .parent-child-card .parent-select-schedule{min-width:88px}
    .parent-event-copy{flex-basis:calc(100% - 111px)}
    .parent-event-icon{display:none}
  }
</style>
