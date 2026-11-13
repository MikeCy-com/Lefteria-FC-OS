<script lang="ts">
  import {untrack} from 'svelte';
  import type {MobileRole,MobileTab} from '../navigation.ts';
  import {TAB_NAMES,ROLE_NAMES} from '../navigation.ts';
  import type {MobileCalendar} from '../calendar.ts';
  import {clubToday,CALENDAR_KIND_LABELS,formatCalendarDate,isCalendarKind} from '../calendar.ts';
  import {ROLE_HOME_SHORTCUTS,trustedRoleHomeEvents,trustedNextRoleMatch,isTrustedRoleHomeContext} from '../role-home.ts';
  import './mobile-theme.css';

  type HomeRole=Exclude<MobileRole,'parent'>;
  interface Props {
    role:HomeRole;identityId:string;userName:string;
    organizationId?:string|null;calendar:MobileCalendar|null;
    calendarStatus:'idle'|'loading'|'ready'|'error';calendarError:string|null;
    busy?:boolean;today?:string;
    onLoadCalendar:(month:string)=>Promise<boolean>|boolean;
    onNavigate:(tab:MobileTab)=>Promise<void>|void;
  }
  let {role,identityId,userName,organizationId=null,calendar,calendarStatus,
    calendarError,busy=false,today=clubToday(),onLoadCalendar,onNavigate}:Props=$props();
  const id=$props.id();
  const needsOrganization=$derived(role==='management'&&!organizationId);
  const contextIsCurrent=$derived(calendarStatus==='ready'&&role!=='fan'&&
    isTrustedRoleHomeContext(calendar,role,identityId,organizationId,today));
  const upcoming=$derived(role==='fan'||!contextIsCurrent?[]:
    trustedRoleHomeEvents(calendar,role,identityId,organizationId,today));
  const shortcuts=$derived(ROLE_HOME_SHORTCUTS[role]);
  const nextMatch=$derived(role==='fan'||!contextIsCurrent?null:
    trustedNextRoleMatch(calendar,role,identityId,organizationId,today));
  let requestedScope=$state('');
  $effect(()=>{
    const scope=identityId+':'+role+':'+(organizationId??'')+':'+today.slice(0,7);
    if(role==='fan'||needsOrganization||busy||contextIsCurrent||requestedScope===scope)return;
    requestedScope=scope;
    untrack(()=>{void onLoadCalendar(today.slice(0,7));});
  });
</script>
<section class="role-home" data-testid={role+'-dashboard-home'} data-home-role={role} aria-labelledby={id+'-title'}>
  <header class="hero">
    <p class="eyebrow">ΛΕΥΤΕΡΙΑ FC · {ROLE_NAMES[role]}</p>
    <h1 id={id+'-title'}>Καλώς ήρθες, {userName}</h1>
    <p class="intro">
      {#if role==='player'}Το ποδόσφαιρό μου · πρόγραμμα και ενημερώσεις της ομάδας μου.
      {:else if role==='coach'}Οι προπονήσεις, οι αθλητές μου και η επικοινωνία με την ομάδα.
      {:else if role==='management'}Οι δραστηριότητες του επιλεγμένου οργανισμού και τα εργαλεία διοίκησης.
      {:else}Νέα, αγώνες και δραστηριότητες του συλλόγου, μόνο από δημόσιες πηγές.{/if}
    </p>
  </header>
  <section class="panel" aria-labelledby={id+'-tools'}>
    <h2 id={id+'-tools'}>{role==='fan'?'Ο σύλλογός μου':'Γρήγορη πρόσβαση'}</h2>
    <div class="shortcuts">
      {#each shortcuts as tab (tab)}
        {#if role==='fan' && ['matches','news','team','shop'].includes(tab)}
          <a class="shortcut" href={tab==='matches'?'/team?tab=results':tab==='news'?'/news':tab==='team'?'/team':'/shop'}
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
  .hero:after{content:'✦';position:absolute;right:-10px;top:-20px;color:#f5a62320;font-size:160px;pointer-events:none}
  .eyebrow{font-size:11px;letter-spacing:.13em;color:var(--lf-gold-light);font-weight:700}
  h1{font-size:clamp(25px,7vw,34px);line-height:1.15;margin:10px 0;font-weight:800}
  .intro{color:#ddd5c6;font-size:13px;line-height:1.6;max-width:38rem}
  .panel{padding:17px 15px;border:1px solid #343331;border-radius:12px;background:linear-gradient(135deg,#191b1c,#111314)}
  h2{font-size:19px;margin:0 0 14px;color:#fff}
  .shortcuts{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:9px}
  .shortcut{display:flex;align-items:center;justify-content:space-between;gap:6px;text-align:left;border:1px solid #4b412b;border-radius:10px;padding:14px 11px;min-height:63px;background:#28251f;color:#ffca46;text-decoration:none;font:inherit;cursor:pointer}
  .shortcut strong{font-size:13px;overflow-wrap:anywhere}.shortcut span{font-size:20px}
  .shortcut:disabled{opacity:.5;cursor:wait}
  .shortcut:focus-visible,.standalone-link:focus-visible{outline:2px solid var(--lf-gold-light);outline-offset:3px}
  .panel-heading{display:flex;justify-content:space-between;gap:12px;align-items:center}
  .panel-heading button,.panel>button{background:transparent;color:#ffca46;border:1px solid #574625;border-radius:8px;min-height:44px;padding:8px 10px;cursor:pointer}
  .events{list-style:none;padding:0;margin:0;display:grid;gap:9px}
  .events li{display:flex;gap:12px;align-items:center;padding:10px 0;border-bottom:1px solid #343331}
  .day{min-width:58px;color:#ffca46;font-size:12px;font-weight:700}
  .event-copy{display:grid;gap:3px;min-width:0}.event-copy strong{font-size:14px;overflow-wrap:anywhere}
  .event-copy small{font-size:12px;color:var(--lf-muted)}
  .notice{font-size:13px;color:var(--lf-muted)}.error{color:#ffa7a7}
  .standalone-link{display:inline-flex;color:#ffca46;min-height:44px;align-items:center}
  .matchday{border-color:#765320;background:linear-gradient(135deg,#35250e,#121313 75%)}
  .matchday h3{font-family:var(--lf-heading);font-size:25px;line-height:1.2;margin:10px 0;color:#fff}
  .match-date{font-size:13px;color:#ffca46;font-weight:700}
  .match-meta{font-size:12px;color:#d0c4ae}
  .match-open{min-height:44px;border:1px solid #8b672a;border-radius:8px;background:#272011;color:#ffca46;padding:10px 13px;margin-top:9px;cursor:pointer}
  @media(min-width:650px){.role-home{max-width:960px;margin:auto}.shortcuts{grid-template-columns:repeat(3,minmax(0,1fr))}}
</style>
