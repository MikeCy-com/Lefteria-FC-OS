<script lang="ts">
  import { untrack } from 'svelte';
  import { CALENDAR_KIND_LABELS, clubToday, formatCalendarDate } from '../calendar.ts';
  import type { CalendarLoadStatus } from '../calendar.ts';
  import { matchingParentHomeCalendar, parentHomeCalendarMonth, parentHomeSchedule } from '../parent-home.ts';
  import type { Family, ParentCalendar } from '../parent-home.ts';
  import './mobile-theme.css';

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
  }

  let { identityId, parentName, family, familyStatus, familyError, calendar, calendarStatus, calendarError,
    busy = false, today = clubToday(), onLoadFamily, onLoadCalendar, onChildren, onCalendar, onEducation }: Props = $props();

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
  <header class="parent-greeting">
    <p class="parent-eyebrow">ΚΑΛΩΣ ΗΡΘΕΣ</p>
    <h1 id={id + '-home-heading'}>{parentName || 'Γονέας'}</h1>
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
      {#if firstChild}
        <button type="button" disabled={busy} onclick={() => void onCalendar(firstChild.id)} data-testid="parent-shortcut-calendar">
          <span aria-hidden="true">▦</span><strong>Πρόγραμμα</strong>
        </button>
      {/if}
      {#if onEducation}
        <button type="button" disabled={busy} onclick={() => void onEducation()} data-testid="parent-shortcut-education">
          <span aria-hidden="true">✦</span><strong>Εκπαίδευση</strong>
        </button>
      {:else}
        <div class="parent-shortcut-pending"><span aria-hidden="true">✉</span><strong>Μηνύματα</strong><small>Under Developments</small></div>
      {/if}
      <div class="parent-shortcut-pending"><span aria-hidden="true">€</span><strong>Πληρωμές</strong><small>Under Developments</small></div>
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
  .parent-greeting{position:relative;margin-bottom:28px}
  .parent-eyebrow{color:#f5a623;font-size:11px;font-weight:700;letter-spacing:.09em;margin:0 0 6px}
  .parent-greeting h1{font-size:clamp(32px,6vw,43px);margin:0 0 14px;line-height:1.1;overflow-wrap:anywhere}
  .parent-settings{min-height:44px;display:inline-flex;align-items:center;gap:10px;padding:10px 14px;border:1px solid #63502c;border-radius:8px;color:#ffca46;background:#211b12;font-size:12px;cursor:pointer}
  .parent-settings span,.parent-section-heading button span{font-size:20px;line-height:1}
  .parent-section{margin:0 0 26px}
  .parent-section-heading{display:flex;justify-content:space-between;align-items:center;gap:16px;margin-bottom:12px}
  .parent-section h2{font-size:21px;margin:0}
  .parent-section-heading button{display:flex;align-items:center;gap:5px;min-height:44px;color:#f5a623;border:0;background:transparent;font-size:12px;cursor:pointer}
  .parent-notice{margin:0;padding:19px;background:#131516;border:1px solid #2b2d2f;border-radius:10px;color:#b3b8bb;line-height:1.7;font-size:13px}
  .parent-error{border-color:#ad5d58}
  .parent-error p{margin:0 0 12px}
  .parent-error button{min-height:44px;padding:9px 14px;background:#261f11;border:1px solid #9c7c3c;color:#ffca46;border-radius:5px;cursor:pointer}
  .parent-child-list{display:grid;gap:10px}
  .parent-child-card{display:flex;align-items:center;gap:13px;min-width:0;padding:13px;background:linear-gradient(135deg,#191b1c,#101213);border:1px solid #303335;border-radius:12px}
  .parent-child-avatar{width:49px;height:49px;flex:0 0 49px;display:grid;place-items:center;border:1px solid #6c5122;border-radius:11px;background:#302311;color:#ffca46;font-size:20px;font-weight:700}
  .parent-child-copy{min-width:0;flex:1}
  .parent-child-copy h3{font-size:19px;margin:0 0 5px;overflow-wrap:anywhere}
  .parent-child-copy p{margin:0;color:#a7adb0;font-size:12px}
  .parent-child-card .parent-select-schedule{flex:0 0 auto;width:auto;min-width:76px;padding:7px;font-size:11px;border:1px solid #62502c;border-radius:7px;background:#201d15;color:#ffca46}
  .parent-child-card .parent-select-schedule[aria-pressed='true']{background:#6b4a18;border-color:#f5a623;color:#fff}
  .parent-child-card>button:not(.parent-select-schedule),.parent-event>button{display:grid;place-items:center;flex:0 0 44px;width:44px;height:44px;border:0;background:transparent;color:#ffca46;font-size:25px;cursor:pointer}
  .parent-scope{margin:0 0 12px;color:#aeb5b9;font-size:12px}
  .parent-schedule-list{border:1px solid #292d2e;border-radius:12px;overflow:hidden}
  .parent-event{display:flex;align-items:center;gap:12px;padding:15px 10px;background:#131516;border-bottom:1px solid #282b2d}
  .parent-event:last-child{border-bottom:0}
  .parent-event-date{flex:0 0 76px;text-align:center}
  .parent-event-date strong{display:block;color:#fff;font-size:15px}
  .parent-event-date span{display:block;margin-top:5px;color:#a7adb0;font-size:10px}
  .parent-event-icon{flex:0 0 37px;width:37px;height:37px;display:grid;place-items:center;border-radius:11px;background:#f5a62318;color:#ffca46;font-size:18px}
  .parent-event-copy{min-width:0;flex:1}
  .parent-event-copy>span{color:#f5a623;font-size:10px}
  .parent-event-copy h3{margin:4px 0;font-size:15px;line-height:1.3;overflow-wrap:anywhere}
  .parent-event-copy p{margin:0;color:#a2a7aa;font-size:11px}
  .parent-more-note{border:0;border-radius:0;padding:14px;font-size:11px}
  .parent-shortcuts h2{margin-bottom:12px}
  .parent-shortcut-grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:10px}
  .parent-shortcut-grid>button,.parent-shortcut-pending{min-height:103px;min-width:0;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:8px;padding:11px 6px;background:#141617;border:1px solid #2a2d2f;border-radius:11px;text-align:center}
  .parent-shortcut-grid>button{color:#f5f5f5;cursor:pointer}
  .parent-shortcut-grid>button span,.parent-shortcut-pending>span{font-size:24px;color:#ffca46}
  .parent-shortcut-grid strong{font-size:12px;overflow-wrap:anywhere}
  .parent-shortcut-pending{color:#a4abad}
  .parent-shortcut-pending small{font-size:9px;color:#b1a085}
  .parent-motto{margin-top:27px;padding:23px;border:1px solid #5b431b;border-radius:14px;background:linear-gradient(125deg,#31230f,#111313 70%)}
  .parent-motto span{display:block;color:#f5a623;font-size:11px;margin-bottom:9px}
  .parent-motto strong{font-family:'Fira Sans Extra Condensed',sans-serif;font-size:27px}
  .parent-motto p{font-size:11px;color:#b2b5b4;margin:10px 0 0}
  @media(max-width:640px){.parent-shortcut-grid{grid-template-columns:repeat(2,minmax(0,1fr))}.parent-event{gap:8px}.parent-event-date{flex-basis:65px}.parent-event-icon{width:32px;height:32px;flex-basis:32px}}
</style>
