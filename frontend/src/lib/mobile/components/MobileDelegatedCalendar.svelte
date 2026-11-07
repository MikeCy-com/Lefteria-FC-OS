<script lang="ts">
  import { untrack } from 'svelte';
  import { AVAILABILITY_LABELS, CALENDAR_KIND_LABELS, clubToday,
    formatCalendarDate, safeCalendarLink, shiftCalendarMonth } from '../calendar.ts';
  import type { CalendarKind, CalendarLoadStatus, MobileCalendar, MobileCalendarItem } from '../calendar.ts';
  import './mobile-theme.css';

  interface Props {
    calendar: MobileCalendar | null;
    status: CalendarLoadStatus;
    error: string | null;
    busy?: boolean;
    loadingMore?: boolean;
    onLoad: (month: string, append?: boolean) => boolean | Promise<boolean>;
  }

  let { calendar,status,error,busy=false,loadingMore=false,onLoad }: Props=$props();
  const id=$props.id();
  let month=$state(untrack(()=>calendar?.month ?? clubToday().slice(0,7)));
  let requested=$state(false);
  const items=$derived((calendar?.items??[]).toSorted((a,b)=>
    a.date.localeCompare(b.date) ||
    (a.start_time??'').localeCompare(b.start_time??'') ||
    a.title.localeCompare(b.title,'el-GR')));
  const child=$derived(calendar?.players.find(p=>p.id===calendar.selected_player_id));
  const response=(item:MobileCalendarItem)=>
    item.responses.find(r=>r.player_id===calendar?.selected_player_id)?.status;
  const responseLabel=(value:string|null|undefined)=>
    value==='going'||value==='not_going'||value==='maybe'
      ?AVAILABILITY_LABELS[value]:'Χωρίς απάντηση';

  // No SSR/API side effects; the controller validates current fan identity,
  // live delegation, role, token, and response scope at every load.
  $effect(()=>{
    if(!requested && status==='idle' && !busy) {
      requested=true;
      untrack(()=>{void onLoad(month);});
    }
  });

  async function changeMonth(delta:number) {
    const next=shiftCalendarMonth(month,delta);
    if(next===month)return;
    month=next;
    await onLoad(month);
  }
</script>

<section class="delegated-calendar" aria-labelledby={id+'-title'} data-testid="family-delegated-calendar">
  <header>
    <p class="eyebrow">ΟΙΚΟΓΕΝΕΙΑΚΗ ΣΥΝΔΕΣΗ</p>
    <h3 id={id+'-title'}>Πρόγραμμα αθλητή</h3>
    <p>Προβολή μόνο. Η οικογενειακή σύνδεση δεν επιτρέπει δηλώσεις συμμετοχής ή αλλαγή ρόλου.</p>
    {#if child}<p class="child-name">{child.name}</p>{/if}
  </header>
  <nav aria-label="Μήνας οικογενειακού προγράμματος" class="month-nav">
    <button type="button" aria-label="Προηγούμενος μήνας"
      disabled={busy||status==='loading'||month==='0001-01'}
      onclick={()=>void changeMonth(-1)}>‹</button>
    <strong>{formatCalendarDate(month+'-01',{month:'long',year:'numeric'})}</strong>
    <button type="button" aria-label="Επόμενος μήνας"
      disabled={busy||status==='loading'||month==='9999-12'}
      onclick={()=>void changeMonth(1)}>›</button>
  </nav>

  {#if status==='idle'||status==='loading'}
    <p role="status" data-testid="delegated-calendar-loading">Φόρτωση προγράμματος…</p>
  {:else if status==='error'}
    <div role="alert" data-testid="delegated-calendar-error">
      <p>{error??'Το πρόγραμμα δεν είναι διαθέσιμο.'}</p>
      <button type="button" disabled={busy} onclick={()=>void onLoad(month)}>Δοκιμή ξανά</button>
    </div>
  {:else if calendar}
    <p class="time-zone">Ώρες συλλόγου · {calendar.time_zone}</p>
    {#if items.length===0}
      <p data-testid="delegated-calendar-empty">Δεν υπάρχουν γεγονότα για αυτόν τον μήνα.</p>
    {:else}
      <div class="events">
        {#each items as item (`${item.kind}:${item.id}`)}
          {@const mapUrl=safeCalendarLink(item.location_url)}
          <article class="event" data-testid="delegated-calendar-event">
            <p class="eyebrow">{CALENDAR_KIND_LABELS[item.kind as CalendarKind]}</p>
            <h4>{item.title}</h4>
            <time datetime={item.date}>{formatCalendarDate(item.date)}</time>
            {#if item.start_time}<p>{item.start_time}</p>{/if}
            {#if item.location}<p>{item.location}</p>{/if}
            {#if mapUrl}<a href={mapUrl} rel="noopener noreferrer" target="_blank">Χάρτης</a>{/if}
            {#if item.is_cancelled}<p>Ακυρώθηκε</p>{/if}
            {#if item.member_summary}<p>{item.member_summary}</p>{/if}
            {#if item.preparation}<p>{item.preparation}</p>{/if}
            <p class="response">Δήλωση συμμετοχής: {responseLabel(response(item))}</p>
          </article>
        {/each}
      </div>
    {/if}
    {#if error}<p role="alert">{error}</p>{/if}
    {#if calendar.next_offset!==null}
      <button type="button" disabled={busy||loadingMore}
        data-testid="delegated-calendar-load-more" onclick={()=>void onLoad(month,true)}>
        {loadingMore?'Φόρτωση…':'Περισσότερα γεγονότα'}
      </button>
    {/if}
  {/if}
</section>

<style>
  .delegated-calendar { margin-top:20px; padding:17px; border:1px solid #4c5649; border-radius:10px; }
  .delegated-calendar h3 { margin:0 0 8px; font-size:20px; }
  .delegated-calendar header p,.delegated-calendar .time-zone { font-size:13px; line-height:1.6; color:var(--lf-muted); }
  .delegated-calendar .eyebrow { font-size:10px; font-weight:700; letter-spacing:.09em; color:var(--lf-gold); }
  .delegated-calendar .child-name { font-weight:700; color:inherit; }
  .month-nav { display:flex; align-items:center; justify-content:space-between; gap:12px; margin:16px 0; }
  .month-nav strong { font-size:14px; text-align:center; }
  .delegated-calendar button { border:1px solid #81704f; border-radius:7px; padding:9px 12px; min-height:42px; }
  .events { display:grid; gap:10px; margin-top:14px; }
  .event { padding:13px; border:1px solid #454c44; border-radius:8px; }
  .event h4 { font-size:16px; margin:0 0 8px; }
  .event p,.event time,.event a { font-size:12px; line-height:1.7; }
  .event .response { margin:9px 0 0; color:var(--lf-muted); }
</style>
