<script lang="ts">
  import {untrack} from 'svelte';
  import type {MobileRole,MobileTab} from '../navigation.ts';
  import {TAB_NAMES,ROLE_NAMES,fanPublicDestination} from '../navigation.ts';
  import type {MobileCalendar,MobileCalendarItem,AvailabilityStatus} from '../calendar.ts';
  import {clubToday,CALENDAR_KIND_LABELS,formatCalendarDate,isCalendarKind} from '../calendar.ts';
  import {ROLE_HOME_SHORTCUTS,trustedRoleHomeEvents,trustedNextRoleMatch,
    isTrustedRoleHomeContext,trustedRoleHomeCancelledEvents,
    trustedPlayerTrainingResponsesNeeded} from '../role-home.ts';
  import './mobile-theme.css';
  import { publicImages } from '../../public/images.ts';

  type HomeRole=Exclude<MobileRole,'parent'>;
  interface Props {
    role:HomeRole;identityId:string;userName:string;
    organizationId?:string|null;calendar:MobileCalendar|null;
    calendarStatus:'idle'|'loading'|'ready'|'error';calendarError:string|null;
    busy?:boolean;today?:string;
    onLoadCalendar:(month:string)=>Promise<boolean>|boolean;
    onNavigate:(tab:MobileTab)=>Promise<void>|void;
    onRespond?:(item:MobileCalendarItem,status:AvailabilityStatus)=>Promise<boolean>|boolean;
  }
  let {role,identityId,userName,organizationId=null,calendar,calendarStatus,
    calendarError,busy=false,today=clubToday(),onLoadCalendar,onNavigate,onRespond}:Props=$props();
  const id=$props.id();
  const needsOrganization=$derived(role==='management'&&!organizationId);
  const contextIsCurrent=$derived(calendarStatus==='ready'&&role!=='fan'&&
    isTrustedRoleHomeContext(calendar,role,identityId,organizationId,today));
  const upcoming=$derived(role==='fan'||!contextIsCurrent?[]:
    trustedRoleHomeEvents(calendar,role,identityId,organizationId,today));
  const shortcuts=$derived(ROLE_HOME_SHORTCUTS[role]);
  const cancellations=$derived(role==='fan'||!contextIsCurrent?[]:
    trustedRoleHomeCancelledEvents(calendar,role,identityId,organizationId,today));
  const nextMatch=$derived(role==='fan'||!contextIsCurrent?null:
    trustedNextRoleMatch(calendar,role,identityId,organizationId,today));
  const playerRsvp=$derived(role==='player'&&nextMatch&&calendar?.selected_player_id
    ?(nextMatch.responses.find(r=>r.player_id===calendar?.selected_player_id)?.status??null):null);
  const trainingResponsesNeeded=$derived(role==='player'&&contextIsCurrent&&onRespond
    ?trustedPlayerTrainingResponsesNeeded(calendar,identityId,today):[]);
  let requestedScope=$state('');
  $effect(()=>{
    const scope=identityId+':'+role+':'+(organizationId??'')+':'+today.slice(0,7);
    if(role==='fan'||needsOrganization||busy||contextIsCurrent||requestedScope===scope)return;
    requestedScope=scope;
    untrack(()=>{void onLoadCalendar(today.slice(0,7));});
  });
</script>
<section class="role-home" data-testid={role+'-dashboard-home'} data-home-role={role} aria-labelledby={id+'-title'}>
  <header class="hero" class:hero-photographic={role==='player'||role==='coach'}
    data-testid="mobile-role-photo-hero"
    style:background-image={role==='player'||role==='coach'
      ? `linear-gradient(90deg,#08090aef 0%,#08090abc 38%,#08090a36 100%),linear-gradient(0deg,#08090ad9,transparent 65%),url("${publicImages.hero}")`
      : undefined}>
    <p class="eyebrow">ΛΕΥΤΕΡΙΑ FC · {ROLE_NAMES[role]}</p>
    <h1 id={id+'-title'}>Καλώς ήρθες, <span>{userName}</span></h1>
    <p class="intro">
      {#if role==='player'}Το ποδόσφαιρό μου · πρόγραμμα και ενημερώσεις της ομάδας μου.
      {:else if role==='coach'}Οι προπονήσεις, οι αθλητές μου και η επικοινωνία με την ομάδα.
      {:else if role==='management'}Οι δραστηριότητες του επιλεγμένου οργανισμού και τα εργαλεία διοίκησης.
      {:else}Νέα, αγώνες και δραστηριότητες του συλλόγου. Η προσωπική σου ψήφος απαιτεί επαληθευμένη σύνδεση.{/if}
    </p>
  </header>
  <section class="panel" aria-labelledby={id+'-tools'}>
    <h2 id={id+'-tools'}>{role==='fan'?'Ο σύλλογός μου':'Γρήγορη πρόσβαση'}</h2>
    <div class="shortcuts">
      {#each shortcuts as tab (tab)}
        {@const publicHref = role==='fan'&&tab==='potm'?'/app?tab=potm':fanPublicDestination(tab, role)}
        {#if publicHref}
          <a class="shortcut" href={publicHref}
            data-testid={'fan-home-'+tab}><strong>{TAB_NAMES[tab]}</strong><span aria-hidden="true">↗</span></a>
        {:else}
          <button type="button" class="shortcut" disabled={busy||(needsOrganization&&role==='management')}
            onclick={()=>void onNavigate(tab)}><strong>{TAB_NAMES[tab]}</strong><span aria-hidden="true">›</span></button>
        {/if}
      {/each}
    </div>
  </section>
  {#if role==='fan'}
    <section class="panel" aria-labelledby={id+'-fan'}>
      <h2 id={id+'-fan'}>Η Λευτεριά κοντά σου</h2>
      <p>Εξερεύνησε τα επίσημα νέα και τους αγώνες. Το εκπαιδευτικό HUB για φιλάθλους απαιτεί ξεχωριστή επαληθευμένη συνδρομή· η επιλογή ενδιαφέροντος δεν δίνει πρόσβαση σε παιδικά δεδομένα.</p>
      <a class="standalone-link" href="/news">Διάβασε τα τελευταία δημοσιευμένα νέα ›</a>
    </section>
  {:else}
    {#if cancellations.length}
      <section class="panel cancellations" role="status"
        aria-labelledby={id+'-cancellations'} data-testid="role-home-cancellations">
        <div class="panel-heading">
          <h2 id={id+'-cancellations'}>⚠️ Αλλαγές προγράμματος · Ακυρώσεις</h2>
          <button type="button" disabled={busy}
            onclick={()=>void onNavigate('calendar')}>Ημερολόγιο ›</button>
        </div>
        <ul class="cancelled-events">
          {#each cancellations as item (item.kind+':'+item.id)}
            <li>
              <strong>{item.title}</strong>
              <small>{formatCalendarDate(item.date,{day:'numeric',month:'short'})}{item.start_time?' · '+item.start_time:''}</small>
              <p>{item.cancel_reason?.trim()||'Η δραστηριότητα ακυρώθηκε.'}</p>
            </li>
          {/each}
        </ul>
        <p class="cancel-note">Οι προηγούμενες απαντήσεις συμμετοχής διατηρούνται. Δεν γίνονται νέες δηλώσεις για ακυρωμένες δραστηριότητες.</p>
      </section>
    {/if}
    {#if role==='player' && trainingResponsesNeeded.length>0 && onRespond}
      <section class="panel player-training-queue" data-testid="player-training-response-queue"
        aria-labelledby={id+'-training-response-title'}>
        <div class="panel-heading">
          <div>
            <p class="eyebrow">Η ΣΥΜΜΕΤΟΧΗ ΜΟΥ</p>
            <h2 id={id+'-training-response-title'}>Απαντήσεις προπονήσεων</h2>
          </div>
          <button type="button" disabled={busy} onclick={()=>void onNavigate('calendar')}>
            Πρόγραμμα ›
          </button>
        </div>
        <p class="player-training-note">Μόνο οι επόμενες προπονήσεις σου με
          επιβεβαιωμένη ταυτότητα και χωρίς ήδη καταχωρισμένη απάντηση.</p>
        <ul class="player-training-items">
          {#each trainingResponsesNeeded as event (event.kind+':'+event.id)}
            <li data-testid="player-training-response-needed">
              <div class="player-training-copy">
                <strong>{event.title}</strong>
                <small>{formatCalendarDate(event.date,{weekday:'short',day:'numeric',month:'short'})}
                  {event.start_time?' · '+event.start_time:''}</small>
              </div>
              <div class="player-training-buttons" role="group"
                aria-label={'Διαθεσιμότητα προπόνησης '+event.title}>
                <button type="button" disabled={busy}
                  onclick={()=>void onRespond?.(event,'going')}>Θα έρθω</button>
                <button type="button" disabled={busy}
                  onclick={()=>void onRespond?.(event,'not_going')}>Δεν θα έρθω</button>
                <button type="button" disabled={busy}
                  onclick={()=>void onRespond?.(event,'maybe')}>Ίσως</button>
              </div>
            </li>
          {/each}
        </ul>
        <p class="player-training-note">Η απάντησή σου αποθηκεύεται μόνο μετά
          την επιβεβαίωση από το σύστημα.</p>
      </section>
    {/if}
    <section class="panel matchday" aria-labelledby={id+'-matchday'} data-testid="role-home-matchday">
      <p class="eyebrow">GAME DAY · ΑΓΩΝΙΣΤΙΚΟ</p>
      <h2 id={id+'-matchday'}>Ο επόμενος αγώνας</h2>
      {#if needsOrganization}
        <p class="notice">Επίλεξε τον οργανισμό για να δεις επιτρεπόμενους αγώνες.</p>
      {:else if !contextIsCurrent}
        <p class="notice" role="status">Επαλήθευση αγωνιστικού προγράμματος…</p>
      {:else if nextMatch}
        <p class="match-date">{formatCalendarDate(nextMatch.date,{weekday:'long',day:'numeric',month:'long'})}</p>
        <h3>{nextMatch.title}</h3>
        <p class="match-meta">{nextMatch.start_time||'Ώρα προς επιβεβαίωση'}{nextMatch.location?' · '+nextMatch.location:''}</p>
        {#if role==='player' && onRespond && !nextMatch.is_cancelled}
          <fieldset class="match-rsvp" disabled={busy} data-testid="player-matchday-rsvp">
            <legend>Η δήλωση συμμετοχής μου: {playerRsvp==='going'?'Θα έρθω':playerRsvp==='not_going'?'Δεν θα έρθω':playerRsvp==='maybe'?'Ίσως':'Χωρίς απάντηση'}</legend>
            {#each [['going','Θα έρθω'],['not_going','Δεν θα έρθω'],['maybe','Ίσως']] as choice}
              <button type="button" aria-pressed={playerRsvp===choice[0]}
                onclick={()=>void onRespond?.(nextMatch,choice[0] as AvailabilityStatus)}>{choice[1]}</button>
            {/each}
          </fieldset>
        {/if}
        <button type="button" class="match-open" disabled={busy}
          onclick={()=>void onNavigate('calendar')}>Άνοιγμα αγώνα & διαθεσιμότητας ›</button>
      {:else}
        <p class="notice">Δεν υπάρχει δημοσιευμένος επόμενος αγώνας στα φορτωμένα δεδομένα του μήνα.</p>
      {/if}
    </section>
    <section class="panel" aria-labelledby={id+'-schedule'} data-testid="role-home-schedule">
      <div class="panel-heading">
        <h2 id={id+'-schedule'}>Σήμερα & επόμενα</h2>
        <button type="button" disabled={busy||needsOrganization} onclick={()=>void onNavigate('calendar')}>Πλήρες πρόγραμμα ›</button>
      </div>
      {#if needsOrganization}
        <p class="notice" role="status">Επίλεξε οργανισμό από την επίσημη επιλογή επάνω πριν εμφανιστεί το πρόγραμμα.</p>
      {:else if calendarStatus==='error'}
        <p class="notice error" role="alert">{calendarError||'Το πρόγραμμα δεν είναι διαθέσιμο.'}</p>
        <button type="button" disabled={busy} onclick={()=>void onLoadCalendar(today.slice(0,7))}>Δοκιμή ξανά</button>
      {:else if !contextIsCurrent}
        <p class="notice" role="status">Έλεγχος του προσωπικού προγράμματος…</p>
      {:else if upcoming.length===0}
        <p class="notice">Δεν υπάρχουν επόμενες δημοσιευμένες δραστηριότητες στα φορτωμένα δεδομένα του μήνα.</p>
      {:else}
        <ol class="events">
          {#each upcoming as event (event.kind+':'+event.id)}
            <li>
              <span class="day">{formatCalendarDate(event.date,{day:'numeric',month:'short'})}</span>
              <span class="event-copy"><strong>{event.title}</strong><small>{(isCalendarKind(event.kind)?CALENDAR_KIND_LABELS[event.kind]:'Δραστηριότητα')}{event.start_time?' · '+event.start_time:''}</small></span>
            </li>
          {/each}
        </ol>
      {/if}
    </section>
  {/if}
</section>
<style>
  .role-home{padding:12px 14px 96px;color:var(--lf-text);display:grid;gap:16px}
  .hero{position:relative;overflow:hidden;background:linear-gradient(135deg,#221907,#101112 65%);border:1px solid #685022;border-radius:14px;padding:22px 18px;box-shadow:inset 0 1px #bf903740}
  .hero:after{content:'✦';position:absolute;right:-10px;top:-20px;color:#f6bb3820;font-size:160px;pointer-events:none}
  .hero.hero-photographic{isolation:isolate;min-height:clamp(235px,32vw,330px);
    display:flex;flex-direction:column;justify-content:flex-end;
    padding:clamp(22px,4vw,38px);
    background-size:cover;background-position:60% 38%;
    border:1px solid #aa8135;border-radius:10px;
    box-shadow:inset 0 1px #ffffff34,0 10px 30px #0008}
  .hero.hero-photographic:after{content:none}
  .hero.hero-photographic h1{
    max-width:650px;font-family:'Fira Sans Extra Condensed',sans-serif;
    font-size:clamp(32px,6.5vw,55px);line-height:1.04;
    font-style:italic;text-shadow:0 4px 15px #000b}
  .hero.hero-photographic h1 span{color:#ffca46}
  .hero.hero-photographic .intro{font-size:clamp(13px,2.5vw,16px);
    color:#f0ede8;text-shadow:0 3px 10px #000b;line-height:1.45}
  .eyebrow{font-size:13px;letter-spacing:.13em;color:var(--lf-gold-light);font-weight:700}
  h1{font-size:clamp(25px,7vw,34px);line-height:1.15;margin:10px 0;font-weight:800}
  .intro{color:#ddd5c6;font-size:15px;line-height:1.6;max-width:38rem}
  .panel{padding:17px 15px;border:1px solid #343331;border-radius:12px;background:linear-gradient(135deg,#191b1c,#111314)}
  .cancellations{border-color:#b36c42;background:linear-gradient(125deg,#2b1a13,#151719)}
  .cancellations h2{color:#ffc08c}
  .cancelled-events{list-style:none;margin:0;padding:0;display:grid;gap:8px}
  .cancelled-events li{padding:12px;display:grid;gap:5px;background:#241a16;border:1px solid #754c37;border-radius:9px}
  .cancelled-events strong{font-size:14px;overflow-wrap:anywhere}
  .cancelled-events small{color:#ffc596;font-size:13px}
  .cancelled-events p,.cancel-note{font-size:14px;color:#dbc6b9;line-height:1.55;margin:0;overflow-wrap:anywhere}
  .cancel-note{margin-top:10px}
  h2{font-size:19px;margin:0 0 14px;color:#fff}
  .shortcuts{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:9px}
  .shortcut{display:flex;align-items:center;justify-content:space-between;gap:6px;text-align:left;border:1px solid #4b412b;border-radius:10px;padding:14px 11px;min-height:63px;background:#28251f;color:#ffca46;text-decoration:none;font:inherit;cursor:pointer}
  .shortcut strong{font-size:15px;overflow-wrap:anywhere}.shortcut span{font-size:20px}
  .shortcut:disabled{opacity:.5;cursor:wait}
  .shortcut:focus-visible,.standalone-link:focus-visible{outline:2px solid var(--lf-gold-light);outline-offset:3px}
  .panel-heading{display:flex;justify-content:space-between;gap:12px;align-items:center}
  .panel-heading button,.panel>button{background:transparent;color:#ffca46;border:1px solid #574625;border-radius:8px;min-height:44px;padding:8px 10px;cursor:pointer}
  .events{list-style:none;padding:0;margin:0;display:grid;gap:9px}
  .events li{display:flex;gap:12px;align-items:center;padding:10px 0;border-bottom:1px solid #343331}
  .day{min-width:64px;color:#ffca46;font-size:14px;font-weight:700}
  .event-copy{display:grid;gap:3px;min-width:0}.event-copy strong{font-size:16px;overflow-wrap:anywhere}
  .event-copy small{font-size:14px;color:var(--lf-muted)}
  .notice{font-size:15px;color:var(--lf-muted)}.error{color:#ffa7a7}
  .standalone-link{display:inline-flex;color:#ffca46;min-height:44px;align-items:center}
  .player-training-queue{border-color:#7a602d;background:linear-gradient(125deg,#241d12,#141515 70%)}
  .player-training-queue .eyebrow{font-size:11px;margin:0 0 6px}
  .player-training-queue h2{margin:0 0 8px}
  .player-training-note{font-size:12px;color:#c9c0b0;line-height:1.5;margin:9px 0}
  .player-training-items{list-style:none;margin:12px 0;padding:0;display:grid;gap:10px}
  .player-training-items li{padding:12px;border-radius:8px;border:1px solid #5d4b2c;
    background:#171819;display:flex;flex-wrap:wrap;align-items:center;justify-content:space-between;gap:10px}
  .player-training-copy{display:grid;gap:5px;min-width:180px;flex:1}
  .player-training-copy strong{font-size:14px;line-height:1.4}
  .player-training-copy small{font-size:12px;color:#d3c49e}
  .player-training-buttons{display:flex;flex-wrap:wrap;gap:6px}
  .player-training-buttons button{min-height:44px;padding:8px 11px;border-radius:7px;
    background:#2b2418;border:1px solid #735c2e;color:#ffca46;
    font-size:13px;font-weight:700;cursor:pointer}
  .player-training-buttons button:disabled{opacity:.5;cursor:wait}
  .player-training-buttons button:focus-visible{outline:2px solid #ffca46;outline-offset:2px}
  .matchday{border-color:#765320;background:linear-gradient(135deg,#35250e,#121313 75%)}
  .matchday h3{font-family:var(--lf-heading);font-size:25px;line-height:1.2;margin:10px 0;color:#fff}
  .match-date{font-size:15px;color:#ffca46;font-weight:700}
  .match-meta{font-size:14px;color:#d0c4ae}
  .match-open{min-height:44px;border:1px solid #8b672a;border-radius:8px;background:#272011;color:#ffca46;padding:10px 13px;margin-top:9px;cursor:pointer}
  .match-rsvp{border:0;padding:0;display:flex;flex-wrap:wrap;gap:7px;margin:14px 0}
  .match-rsvp legend{font-size:14px;color:#ddd0b1;margin-bottom:7px}
  .match-rsvp button{min-height:44px;border:1px solid #6e5224;border-radius:7px;background:#2c2416;color:#ffca46;padding:8px 12px;font:inherit;cursor:pointer}
  .match-rsvp button[aria-pressed='true']{background:#6d4b17;border-color:#f6bb38;color:#fff;font-weight:700}
  .match-rsvp button:focus-visible{outline:2px solid #ffca46;outline-offset:3px}
  .match-rsvp:disabled button{opacity:.5;cursor:wait}
  .panel-heading button,.panel>button,.match-open{font-size:14px;line-height:1.4}
  .panel-heading button:focus-visible,.panel>button:focus-visible,.match-open:focus-visible{outline:2px solid #ffca46;outline-offset:3px}
  .panel-heading button:disabled,.match-open:disabled{opacity:.55;cursor:wait}
  .match-rsvp button{font-size:14px}
  .panel>p{font-size:15px;line-height:1.65}
  @media(max-width:430px){
    .hero.hero-photographic{min-height:285px;background-position:64% center;padding:20px}
    .hero.hero-photographic h1{font-size:clamp(32px,9vw,43px)}

    .role-home{padding:10px 0 96px}
    .panel-heading{align-items:flex-start;flex-wrap:wrap}
    .panel-heading button{flex:0 1 auto}
    .match-rsvp button{flex:1 1 95px}
    .events li{align-items:flex-start}
    .day{min-width:62px}
  }
  @media(min-width:650px){.role-home{max-width:960px;margin:auto}.shortcuts{grid-template-columns:repeat(3,minmax(0,1fr))}}
</style>
