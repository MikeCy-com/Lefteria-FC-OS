<script lang="ts">
  import {browser} from '$app/env';
  import {untrack} from 'svelte';
  import {academyMonthWindow,isAcademyTrainingList} from './academy-schedule.ts';
  import {validTrainingPlanPage,validTrainingPublicationsPage} from './academy-training-plans.ts';
  import {calendarTraining,calendarFixtures,calendarMatchDrafts,sortCalendar,type CalendarOwner,type CalendarRow} from './group-calendar.ts';
  let {organization,groupId,actorId,training,matches,matchDrafts,month,onTraining,onMatches,onExpired,onRevoked}:{
    organization:CalendarOwner;groupId:string;actorId:string;training:boolean;matches:boolean;matchDrafts:boolean;
    month:string;onTraining:()=>void;onMatches:()=>void;onExpired:()=>void;onRevoked:()=>void;
  }=$props();
  let selectedMonth=$state('');
  $effect(()=>{selectedMonth=month;});
  let rows=$state<CalendarRow[]>([]),loading=$state(false),errors=$state<string[]>([]),refresh=$state(0);
  let filter=$state<'all'|'training'|'match'>('all');
  const range=$derived(academyMonthWindow(selectedMonth));
  const visible=$derived(sortCalendar(rows.filter(row=>filter==='all'||row.type===filter)));
  const dates=$derived([...new Set(visible.map(row=>row.day))]);
  const labels:Record<CalendarRow['state'],string>={draft:'Ιδιωτικό πρόχειρο',published:'Δημοσιευμένο',cancelled:'Ακυρωμένο',completed:'Ολοκληρωμένο',scheduled:'Προγραμματισμένο',recorded:'Καταχωρισμένο'};
  function stepMonth(amount:number){
    if(!range)return;
    const d=new Date(range.start+'T12:00:00Z');d.setUTCMonth(d.getUTCMonth()+amount);
    const next=d.toISOString().slice(0,7);if(next>='2024-01'&&next<='2032-12')selectedMonth=next;
  }
  $effect(()=>{
    const owner=organization,group=groupId,actor=actorId,w=range,canTraining=training&&organization==='lefteria_fc_academy',canMatches=matches,canDrafts=matches&&matchDrafts;
    void refresh;
    rows=[];errors=[];loading=false;
    if(!browser||!actor||!group||!w||(!canTraining&&!canMatches))return;
    const controller=new AbortController();
    const signal=controller.signal;
    let revoked=false;
    const stillCurrent=()=>!signal.aborted&&!revoked&&organization===owner&&groupId===group&&actorId===actor;
    async function read(url:string):Promise<unknown>{
      const response=await fetch(url,{credentials:'same-origin',cache:'no-store',signal,headers:{Accept:'application/json'}});
      if(response.status===401){revoked=true;onExpired();throw Error('expired');}
      if(response.status===403){revoked=true;onRevoked();throw Error('revoked');}
      if(!response.ok)throw Error('unavailable');
      return response.json();
    }
    const query=new URLSearchParams({organization_id:owner,group_id:group,start:w.start,end:w.end});
    loading=true;
    untrack(()=>void(async()=>{
      const work:Promise<CalendarRow[]>[]=[];
      const names:string[]=[];
      if(canTraining){
        names.push('Προπονήσεις');
        work.push((async()=>{
          const base='/api/admin/academy/training/';
          const [sessions,plans]=await Promise.all([read(base+'?'+query),read(base+'plans/?'+query)]);
          if(!isAcademyTrainingList(sessions,group,w.start,w.end)||!validTrainingPlanPage(plans,group,w.start,w.end))throw Error('invalid training scope');
          const publications=await read(base+'plans/publications?'+query);
          if(!validTrainingPublicationsPage(publications,group,w.start,w.end,plans.plans))throw Error('invalid publications');
          return calendarTraining(sessions.sessions,plans.plans,publications.publications);
        })());
      }
      if(canMatches){
        names.push('Δημοσιευμένοι αγώνες');
        work.push((async()=>{const result=calendarFixtures(await read('/api/public/site?organization_id='+encodeURIComponent(owner)),owner,group,w.start,w.end);
          if(result===null)throw Error('invalid fixtures');return result;})());
      }
      if(canDrafts){
        names.push('Πρόχειροι αγώνες');
        work.push((async()=>{const result=calendarMatchDrafts(await read('/api/admin/football/territories?organization_id='+encodeURIComponent(owner)),owner,group,w.start,w.end);
          if(result===null)throw Error('invalid drafts');return result;})());
      }
      const result=await Promise.allSettled(work);
      if(stillCurrent()){rows=result.flatMap(item=>item.status==='fulfilled'?item.value:[]);errors=names.filter((_,i)=>result[i].status==='rejected');}
      if(!signal.aborted)loading=false;
    })());
    return()=>controller.abort();
  });
</script>
<section class="group-calendar" data-testid="admin-group-calendar">
  <header><div><h3>Ημερολόγιο τμήματος</h3><p class="admin-muted">Προπονήσεις και αγώνες από τις υπάρχουσες επαληθευμένες πηγές. Οι δημοσιευμένοι αγώνες περιορίζονται στον διαθέσιμο κατάλογο ιστοσελίδας (έως 50 ανά οργανισμό).</p></div>
    <button class="admin-secondary" type="button" disabled={loading} onclick={()=>refresh++}>Ανανέωση</button></header>
  <div class="calendar-controls">
    <button type="button" class="admin-secondary" aria-label="Προηγούμενος μήνας" onclick={()=>stepMonth(-1)}>‹</button>
    <label>Μήνας <input type="month" bind:value={selectedMonth} min="2024-01" max="2032-12"/></label>
    <button type="button" class="admin-secondary" aria-label="Επόμενος μήνας" onclick={()=>stepMonth(1)}>›</button>
    <div role="group" aria-label="Είδος δραστηριότητας">
      <button type="button" aria-pressed={filter==='all'} onclick={()=>filter='all'}>Όλα</button>
      {#if training}<button type="button" aria-pressed={filter==='training'} onclick={()=>filter='training'}>Προπονήσεις</button>{/if}
      {#if matches}<button type="button" aria-pressed={filter==='match'} onclick={()=>filter='match'}>Αγώνες</button>{/if}
    </div>
  </div>
  {#if loading}<p role="status">Φόρτωση ημερολογίου…</p>
  {:else}
    {#if errors.length}<p role="alert">Δεν ήταν διαθέσιμες όλες οι πηγές: {errors.join(', ')}.</p>{/if}
    {#if !visible.length}<p class="admin-muted">{errors.length?'Δεν υπάρχουν επαληθευμένες διαθέσιμες καταχωρίσεις.':'Δεν βρέθηκαν καταχωρίσεις για τον μήνα και το φίλτρο.'}</p>{/if}
    {#each dates as day (day)}
      <section class="calendar-day"><h4>{new Intl.DateTimeFormat('el-CY',{weekday:'long',day:'numeric',month:'long',timeZone:'UTC'}).format(new Date(day+'T12:00:00Z'))}</h4>
        {#each visible.filter(row=>row.day===day) as row (row.id)}
          <article class="calendar-row">
            <time>{row.time??'—'}</time><div><strong>{row.title}</strong><small>{row.type==='training'?'Προπόνηση':'Αγώνας'} · {labels[row.state]}{row.location?' · '+row.location:''}</small></div>
            <button type="button" class="admin-secondary" onclick={row.type==='training'?onTraining:onMatches}>Διαχείριση ›</button>
          </article>
        {/each}
      </section>
    {/each}
  {/if}
</section>
<style>
.group-calendar{display:grid;gap:12px;min-width:0}
.group-calendar header,.calendar-controls,.calendar-controls>div{display:flex;flex-wrap:wrap;align-items:center;gap:8px}
.group-calendar header{justify-content:space-between}
.group-calendar h3,.group-calendar h4,.group-calendar p{margin:0}
.calendar-controls label{display:grid;gap:4px}
.calendar-controls button{min-height:44px}
.calendar-controls [aria-pressed=true]{color:var(--admin-accent,#f6bb38);border-color:var(--admin-accent,#f6bb38)}
.calendar-day{display:grid;gap:4px;border:1px solid var(--admin-border,#555);border-radius:8px;padding:12px}
.calendar-row{display:flex;flex-wrap:wrap;align-items:center;gap:12px;padding:10px 0;border-top:1px solid var(--admin-border,#555)}
.calendar-row time{min-width:55px;color:var(--admin-accent,#f6bb38);font-weight:700}
.calendar-row>div{display:grid;gap:4px;flex:1;min-width:120px;overflow-wrap:anywhere}
</style>
