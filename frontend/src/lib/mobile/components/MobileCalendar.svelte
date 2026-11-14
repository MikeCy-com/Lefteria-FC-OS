<script lang="ts">
  import { untrack } from 'svelte';
  import type { MobileRole } from '../navigation.ts';
  import { AVAILABILITY_LABELS, CALENDAR_KIND_LABELS, WEEKDAY_LABELS, calendarItemKey, calendarItemOnDate,
    calendarMonthDays, calendarOrganizationLabel, calendarWeekDays, clubToday, formatCalendarDate, safeCalendarLink, shiftCalendarMonth } from '../calendar.ts';
  import type { AvailabilityStatus, CalendarKind, CalendarLoadStatus, MobileCalendar, MobileCalendarItem, MobileCalendarResponses } from '../calendar.ts';
  import './mobile-theme.css';

  interface Props {
    role: Exclude<MobileRole, 'fan'>;
    initialPlayerId?: string | null;
    calendar: MobileCalendar | null;
    status: CalendarLoadStatus;
    error: string | null;
    loadingMore?: boolean;
    busy?: boolean;
    responses: MobileCalendarResponses | null;
    responsesKey: string | null;
    responsesStatus: CalendarLoadStatus;
    responsesError: string | null;
    responsesLoadingMore?: boolean;
    onLoad: (month: string, playerId?: string) => boolean | Promise<boolean>;
    onLoadMore: () => boolean | Promise<boolean>;
    onRespond: (item: MobileCalendarItem, status: AvailabilityStatus) => boolean | Promise<boolean>;
    onLoadResponses: (item: MobileCalendarItem, append?: boolean) => boolean | Promise<boolean>;
    onCloseResponses: () => void;
  }

  let { role, initialPlayerId = null, calendar, status, error, loadingMore = false, busy = false, responses, responsesKey, responsesStatus,
    responsesError, responsesLoadingMore = false, onLoad, onLoadMore, onRespond, onLoadResponses, onCloseResponses }: Props = $props();
  const id = $props.id();
  let month = $state(untrack(() => calendar?.month ?? clubToday().slice(0, 7)));
  let selectedDate = $state(untrack(() => calendar?.month ? `${calendar.month}-01` : clubToday()));
  let playerId = $state<string | undefined>(untrack(() => calendar?.selected_player_id ?? (role === 'parent' ? initialPlayerId ?? undefined : undefined)));
  let showMonth = $state(false);
  let filter = $state<'all' | CalendarKind>('all');
  const staff = $derived(role === 'coach' || role === 'management');
  const timeZone = $derived(calendar?.time_zone ?? 'Asia/Nicosia');
  const visibleItems = $derived((calendar?.items ?? []).filter((item) => filter === 'all' || item.kind === filter));
  const dayItems = $derived(visibleItems.filter((item) => calendarItemOnDate(item, selectedDate))
    .toSorted((a, b) => (a.start_time ?? '').localeCompare(b.start_time ?? '') || a.title.localeCompare(b.title, 'el-GR')));
  const weekDays = $derived(calendarWeekDays(selectedDate));
  const monthDays = $derived(calendarMonthDays(month));
  const selectedPlayer = $derived(calendar?.players.find((player) => player.id === calendar.selected_player_id));
  const responseChoices = Object.entries(AVAILABILITY_LABELS) as [AvailabilityStatus, string][];
  const filters: { value: 'all' | CalendarKind; label: string }[] = [
    { value: 'all', label: 'Όλα' }, { value: 'training', label: 'Προπονήσεις' },
    { value: 'match', label: 'Αγώνες' }, { value: 'event', label: 'Εκδηλώσεις' }
  ];

  $effect(() => { if (status === 'idle' && !busy) untrack(() => { void onLoad(month, playerId); }); });
  $effect(() => { if (calendar) playerId = calendar.selected_player_id ?? undefined; });

  async function changeMonth(offset: number) {
    month = shiftCalendarMonth(month, offset);
    selectedDate = `${month}-01`;
    await onLoad(month, playerId);
  }

  async function selectDate(date: string) {
    selectedDate = date;
    if (date.slice(0, 7) !== month) { month = date.slice(0, 7); await onLoad(month, playerId); }
  }

  async function selectPlayer(event: Event) {
    playerId = (event.currentTarget as HTMLSelectElement).value;
    await onLoad(month, playerId);
  }

  async function retryCalendar() {
    // Resolve a current allowed child again if the previous selection was revoked.
    playerId = undefined;
    await onLoad(month);
  }

  function hasItems(date: string) { return visibleItems.some((item) => calendarItemOnDate(item, date)); }
  function responseLabel(value: string | null | undefined) {
    return value === 'going' || value === 'not_going' || value === 'maybe' ? AVAILABILITY_LABELS[value] : 'Χωρίς απάντηση';
  }
</script>

<section class="calendar-page" aria-labelledby={`${id}-title`} data-testid="mobile-calendar" data-calendar-role={role}>
  <header class="page-heading"><p>ΗΜΕΡΟΛΟΓΙΟ</p><h1 id={`${id}-title`}>Το πρόγραμμά μας</h1><span>Προπονήσεις, αγώνες & εκδηλώσεις</span></header>
  {#if role === 'parent' && calendar && calendar.players.length > 0}
    <div class="player-picker"><label for={`${id}-player`}>Πρόγραμμα παιδιού</label>
      <select id={`${id}-player`} value={calendar.selected_player_id ?? ''} disabled={busy} onchange={(event) => void selectPlayer(event)} data-testid="calendar-player-select">
        {#each calendar.players as player (player.id)}<option value={player.id}>{player.name}</option>{/each}
      </select>
    </div>
  {:else if role === 'player' && selectedPlayer}<p class="player-name">{selectedPlayer.name}</p>{/if}

  <nav class="category-tabs" aria-label="Κατηγορίες προγράμματος">
    {#each filters as category (category.value)}<button type="button" aria-pressed={filter === category.value} onclick={() => { filter = category.value; }} data-testid={`calendar-filter-${category.value}`}>{category.label}</button>{/each}
  </nav>
  <div class="month-heading">
    <button class="month-arrow" type="button" disabled={busy || month === '0001-01'} aria-label="Προηγούμενος μήνας" onclick={() => void changeMonth(-1)}>‹</button>
    <h2>{formatCalendarDate(`${month}-01`, { month: 'long', year: 'numeric' })}</h2>
    <button class="month-arrow" type="button" disabled={busy || month === '9999-12'} aria-label="Επόμενος μήνας" onclick={() => void changeMonth(1)}>›</button>
    <button class="month-toggle" type="button" aria-expanded={showMonth} aria-controls={`${id}-month`} onclick={() => { showMonth = !showMonth; }}>{showMonth ? 'Κλείσιμο' : 'Μήνας'}</button>
  </div>
  <nav class="week-strip" aria-label="Ημέρες εβδομάδας" data-testid="calendar-week-strip">
    {#each weekDays as date (date)}
      <button type="button" aria-pressed={selectedDate === date} aria-label={formatCalendarDate(date)} disabled={busy} onclick={() => void selectDate(date)}>
        <small>{formatCalendarDate(date, { weekday: 'short' })}</small><strong>{Number(date.slice(8))}</strong><small>{formatCalendarDate(date, { month: 'short' })}</small>
        <i class:has-items={hasItems(date)} aria-label={hasItems(date) ? 'Υπάρχει πρόγραμμα' : undefined}></i>
      </button>
    {/each}
  </nav>
  {#if showMonth}
    <div class="month-grid" id={`${id}-month`} aria-label="Ημερολόγιο μήνα" data-testid="calendar-month-grid">
      {#each WEEKDAY_LABELS as day}<span class="weekday">{day}</span>{/each}
      {#each monthDays as date, index (date ?? `blank-${index}`)}
        {#if date}<button type="button" aria-pressed={selectedDate === date} aria-label={formatCalendarDate(date)} disabled={busy} onclick={() => void selectDate(date)}>
          {Number(date.slice(8))}<i class:has-items={hasItems(date)}></i>
        </button>{:else}<span></span>{/if}
      {/each}
    </div>
  {/if}
  <p class="time-zone">Ημερομηνίες και ώρες συλλόγου · {timeZone}</p>

  {#if status === 'loading' || status === 'idle'}
    <p class="notice" role="status" data-testid="calendar-loading">Φόρτωση προγράμματος…</p>
  {:else if status === 'error'}
    <div class="notice failure" role="alert" data-testid="calendar-unavailable"><p>{error ?? 'Το πρόγραμμα δεν είναι διαθέσιμο.'}</p>
      <button type="button" disabled={busy} onclick={() => void retryCalendar()}>Δοκιμή ξανά</button></div>
  {:else if calendar}
    <h2 class="date-heading" aria-live="polite" aria-atomic="true"><time datetime={selectedDate}>{formatCalendarDate(selectedDate)}</time></h2>
    {#if dayItems.length === 0}
      <p class="notice" data-testid="calendar-empty">{calendar.next_offset !== null ? 'Δεν εμφανίζεται πρόγραμμα για αυτή την ημέρα. Φόρτωσε και την υπόλοιπη λίστα.' : 'Δεν υπάρχουν προγραμματισμένα γεγονότα για αυτή την ημέρα.'}</p>
    {:else}
      <div class="event-list">
        {#each dayItems as item (calendarItemKey(item))}
          {@const key = calendarItemKey(item)}
          {@const ownResponse = item.responses.find((response) => response.player_id === calendar.selected_player_id)}
          {@const mapUrl = safeCalendarLink(item.location_url)}
          <article class="event-card" class:cancelled={item.is_cancelled} data-testid={`calendar-item-${key}`}>
            <div class="event-type"><span>{CALENDAR_KIND_LABELS[item.kind as CalendarKind]}</span>{#if role === 'coach'}<span>{calendarOrganizationLabel(item.organization_id)}</span>{/if}</div>
            <h3>{item.title}</h3>
            <div class="event-meta">
              <time datetime={item.date}>{formatCalendarDate(item.date, { day: 'numeric', month: 'short' })}{#if item.end_date !== item.date} – {formatCalendarDate(item.end_date, { day: 'numeric', month: 'short' })}{/if}</time>
              {#if item.start_time}<span>{item.start_time}</span>{/if}
              {#if item.arrival_time}<span>Άφιξη: {item.arrival_time}</span>{/if}
              {#if item.duration_minutes != null}<span>{item.duration_minutes} λεπτά</span>{/if}
              {#if item.location}<span>{item.location}</span>{/if}
              {#if mapUrl}<a href={mapUrl} target="_blank" rel="noopener noreferrer">Χάρτης ↗</a>{/if}
            </div>
            {#if item.is_cancelled}<p class="cancel-label">Ακυρώθηκε{item.cancel_reason ? ` · ${item.cancel_reason}` : ''}</p>{/if}
            {#if item.member_summary}<p class="event-text">{item.member_summary}</p>{/if}
            {#if item.description && item.description !== item.member_summary}<p class="event-text">{item.description}</p>{/if}
            {#if item.session_journey}<div class="member-detail"><h4>Η προπόνηση</h4><p>{item.session_journey}</p></div>{/if}
            {#if item.preparation}<div class="member-detail"><h4>Προετοιμασία</h4><p>{item.preparation}</p></div>{/if}
            {#if !staff && calendar.selected_player_id}
              <fieldset class="availability" disabled={busy || item.is_cancelled} data-testid={`calendar-availability-${key}`}>
                <legend>Η απάντησή {role === 'parent' ? `του παιδιού · ${selectedPlayer?.name ?? ''}` : 'μου'}</legend>
                {#each responseChoices as [value, label]}<button type="button" aria-pressed={ownResponse?.status === value} onclick={() => void onRespond(item, value)} data-testid={`calendar-rsvp-${value}-${key}`}>{label}</button>{/each}
              </fieldset>
              <p class="reply-state">{ownResponse ? `Αποθηκευμένη απάντηση: ${responseLabel(ownResponse.status)}` : 'Δεν έχει δοθεί απάντηση.'}</p>
            {:else if staff}
              <button type="button" class="replies-toggle" aria-expanded={responsesKey === key} disabled={busy}
                onclick={() => { if (responsesKey === key) onCloseResponses(); else void onLoadResponses(item); }}>Απαντήσεις συμμετοχής {responsesKey === key ? '−' : '+'}</button>
              {#if responsesKey === key}
                <div class="replies" data-testid="calendar-replies"><p class="reply-context">Δηλώσεις συμμετοχής παικτών</p>
                  {#if responsesStatus === 'loading'}<p role="status">Φόρτωση απαντήσεων…</p>
                  {:else if responsesStatus === 'error'}<p class="failure" role="alert">{responsesError}</p><button type="button" disabled={busy} onclick={() => void onLoadResponses(item)}>Δοκιμή ξανά</button>
                  {:else if responsesStatus === 'ready' && responses}
                    {#if responses.players.length === 0}<p>Δεν υπάρχουν διαθέσιμες απαντήσεις.</p>{:else}
                      <ul>{#each responses.players as player (player.id)}<li><span>{player.name}</span><strong>{responseLabel(player.status)}</strong></li>{/each}</ul>
                    {/if}
                    {#if responsesError}<p class="failure" role="alert">{responsesError}</p>{/if}
                    {#if responses.next_offset !== null}<button type="button" disabled={busy || responsesLoadingMore} onclick={() => void onLoadResponses(item, true)}>{responsesLoadingMore ? 'Φόρτωση…' : 'Περισσότερες απαντήσεις'}</button>{/if}
                  {/if}
                </div>
              {/if}
            {/if}
          </article>
        {/each}
      </div>
    {/if}
    {#if error}<p class="notice failure" role="alert">{error}</p>{/if}
    {#if calendar.next_offset !== null}<div class="more-events"><p>Εμφανίζεται μέρος του προγράμματος του μήνα.</p><button type="button" disabled={busy || loadingMore} onclick={() => void onLoadMore()} data-testid="calendar-load-more">{loadingMore ? 'Φόρτωση…' : 'Φόρτωση περισσότερων'}</button></div>{/if}
  {/if}
</section>

<style>
  .calendar-page { max-width:760px; margin:0 auto; }
  .page-heading { margin-bottom:20px; }
  .page-heading p { margin:0 0 4px; color:var(--lf-gold); font-size:10px; font-weight:600; letter-spacing:.15em; }
  .page-heading h1 { margin:0 0 5px; font-size:30px; line-height:1.1; }
  .page-heading > span,.time-zone,.player-name { color:var(--lf-muted); font-size:12px; }
  .player-picker { display:grid; gap:7px; margin-bottom:16px; }
  .player-picker label { color:var(--lf-muted); font-size:12px; }
  select { width:100%; min-height:44px; padding:10px; border:1px solid #44381f; border-radius:6px; background:var(--lf-surface); color:var(--lf-gold-light); }
  button { min-height:44px; border:1px solid var(--lf-line); border-radius:6px; background:var(--lf-surface); color:#d6d8da; cursor:pointer; }
  button[aria-pressed='true'] { border-color:var(--lf-gold); background:#f5a62319; color:var(--lf-gold-light); }
  .category-tabs { display:flex; gap:5px; overflow-x:auto; padding-bottom:4px; }
  .category-tabs button { flex:1; padding:8px 10px; font-size:13px; line-height:1.4; white-space:nowrap; }
  .month-heading { display:flex; align-items:center; gap:6px; margin:16px 0 10px; }
  .month-heading h2 { flex:1; margin:0; font-size:22px; text-align:center; text-transform:capitalize; }
  .month-arrow { width:44px; min-width:44px; border:0; background:transparent; font-size:29px; }
  .month-toggle { margin-left:4px; padding:7px 10px; color:var(--lf-gold-light); font-size:13px; }
  .week-strip { display:grid; grid-template-columns:repeat(7,minmax(0,1fr)); gap:4px; }
  .week-strip button { display:flex; align-items:center; flex-direction:column; gap:3px; padding:9px 1px 5px; }
  .week-strip small { font-size:11px; line-height:1.25; }
  .week-strip strong { font-family:var(--lf-heading); font-size:23px; font-weight:600; line-height:1.2; }
  i { display:block; width:4px; height:4px; margin:3px auto 0; border-radius:50%; background:transparent; }
  i.has-items { background:var(--lf-gold-light); }
  .month-grid { display:grid; grid-template-columns:repeat(7,minmax(0,1fr)); gap:4px; margin-top:12px; padding:12px 5px; border:1px solid var(--lf-line); border-radius:8px; }
  .month-grid .weekday { padding:5px 0; color:var(--lf-muted); font-size:10px; text-align:center; }
  .month-grid button { padding:5px 1px; font-size:12px; }
  .time-zone { margin:12px 0 20px; font-size:12px; line-height:1.5; overflow-wrap:anywhere; }
  .date-heading { margin:0 0 12px; font-size:19px; text-transform:capitalize; }
  .notice { margin:0 0 15px; padding:24px 16px; border:1px solid var(--lf-line); border-radius:8px; background:var(--lf-surface); color:var(--lf-muted); font-size:12px; text-align:center; }
  .notice p { margin:0 0 12px; }
  .notice button,.more-events button,.replies button { padding:9px 14px; color:var(--lf-gold-light); }
  .failure { color:#fca5a5; overflow-wrap:anywhere; }
  .event-list { display:grid; gap:12px; }
  .event-card { padding:15px; border:1px solid var(--lf-line); border-left:3px solid var(--lf-gold); border-radius:7px; background:var(--lf-surface); overflow-wrap:anywhere; }
  .event-card.cancelled { border-left-color:#b65b5b; }
  .event-type { display:flex; justify-content:space-between; gap:8px; margin-bottom:7px; color:var(--lf-gold-light); font-size:10px; }
  .event-card h3 { margin:0 0 8px; font-size:23px; line-height:1.15; }
  .event-meta { display:flex; flex-wrap:wrap; gap:7px 12px; color:#d5d7d8; font-size:13px; line-height:1.5; }
  .event-meta a { color:var(--lf-gold-light); }
  .event-text,.member-detail p { margin:12px 0 0; color:#d5d7d8; font-size:14px; line-height:1.6; white-space:pre-wrap; }
  .member-detail h4 { margin:14px 0 0; color:var(--lf-gold-light); font-size:11px; font-weight:500; }
  .member-detail p { margin-top:5px; }
  .cancel-label { color:#fca5a5; font-size:12px; }
  .availability { display:flex; gap:6px; margin:16px 0 0; padding:0; border:0; }
  .availability legend { margin-bottom:8px; color:#d5d7d8; font-size:13px; line-height:1.5; }
  .availability button { flex:1; padding:8px 4px; font-size:13px; line-height:1.35; }
  .reply-state { margin:7px 0 0; color:#d5d7d8; font-size:12px; line-height:1.5; }
  .replies-toggle { width:100%; margin-top:14px; color:var(--lf-gold-light); text-align:left; font-size:12px; }
  .replies { margin-top:10px; padding-top:8px; border-top:1px solid var(--lf-line); font-size:12px; }
  .reply-context { color:var(--lf-muted); font-size:10px; }
  .replies ul { margin:0; padding:0; list-style:none; }
  .replies li { display:flex; justify-content:space-between; gap:12px; padding:9px 0; border-bottom:1px solid #ffffff08; }
  .replies strong { flex-shrink:0; color:var(--lf-gold-light); font-size:11px; font-weight:500; }
  .more-events { margin:16px 0; color:var(--lf-muted); font-size:11px; text-align:center; }
  @media(min-width:620px) { .page-heading h1 { font-size:34px; } .category-tabs button { font-size:12px; } .week-strip small { font-size:11px; } }
</style>
