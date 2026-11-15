<script lang="ts">
  import {browser} from '$app/env';
  import {onMount,untrack,getContext} from 'svelte';
  import {ADMIN_AUTH_CONTEXT,type AdminAuthController} from './controller.ts';
  import {summarizeAcademyAttendance} from './attendance-summary.ts';

  type Session={id:string;day:string;title:string;is_cancelled:boolean;is_archived:boolean};
  type SessionList={organization_id:'lefteria_fc_academy';group_id:string;from:string;to:string;total:number;sessions:Session[]};
  type Status='present'|'absent'|null;
  type Player={player_id:string;name:string;rsvp:'going'|'not_going'|'maybe'|'no_response'|null;
    historical_attendance:Status;attendance:Status;attendance_source:'legacy_import'|'os_manager'|'not_recorded';version:number};
  type Register={organization_id:'lefteria_fc_academy';event_id:string;day:string;title:string;
    is_cancelled:boolean;is_archived:boolean;writable:boolean;total:number;players:Player[]};
  let {groupId,actorId,canRead=false,canWrite=false}:{
    groupId:string;actorId:string;canRead:boolean;canWrite:boolean
  }=$props();
  const auth=getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  const base='/api/admin/academy/training';
  const owner='organization_id=lefteria_fc_academy';
  const date=(days:number)=>{
    const value=new Date();value.setUTCDate(value.getUTCDate()+days);
    const parts=new Intl.DateTimeFormat('en-GB',{
      timeZone:'Asia/Nicosia',year:'numeric',month:'2-digit',day:'2-digit'
    }).formatToParts(value);
    const field=(type:string)=>parts.find(p=>p.type===type)?.value??'';
    return field('year')+'-'+field('month')+'-'+field('day');
  };
  let start=$state(''),end=$state('');
  let sessions=$state<SessionList|null>(null);
  let register=$state<Register|null>(null);
  let eventId=$state('');
  let loading=$state(false),loadingRegister=$state(false),saving=$state('');
  let message=$state(''),error=$state('');
  let refreshSessions=$state(0),refreshRegister=$state(0);
  const totals=$derived(register?summarizeAcademyAttendance(register.players):null);
  const idSafe=(s:unknown):s is string=>typeof s==='string'&&s.length>0&&s.length<=128&&!/[\x00-\x1f\x7f]/.test(s);
  const daySafe=(s:unknown):s is string=>typeof s==='string'&&/^\d{4}-\d\d-\d\d$/.test(s);
  const statuses:Record<'present'|'absent',string>={present:'Παρόν',absent:'Απών'};
  function validSessions(v:unknown,g:string,a:string,b:string):v is SessionList{
    if(!v||typeof v!=='object'||Array.isArray(v))return false;
    const x=v as Record<string,unknown>;
    return x.organization_id==='lefteria_fc_academy'&&x.group_id===g&&x.from===a&&x.to===b&&
      Number.isSafeInteger(x.total)&&(x.total as number)>=0&&(x.total as number)<=500&&
      Array.isArray(x.sessions)&&x.sessions.length<=500&&x.sessions.every((row:unknown)=>{
        if(!row||typeof row!=='object'||Array.isArray(row))return false;
        const p=row as Record<string,unknown>;
        return idSafe(p.id)&&daySafe(p.day)&&typeof p.title==='string'&&p.title.length<=250&&
          typeof p.is_cancelled==='boolean'&&typeof p.is_archived==='boolean';
      });
  }
  function validRegister(v:unknown,id:string):v is Register{
    if(!v||typeof v!=='object'||Array.isArray(v))return false;
    const x=v as Record<string,unknown>;
    return x.organization_id==='lefteria_fc_academy'&&x.event_id===id&&daySafe(x.day)&&
      typeof x.title==='string'&&typeof x.is_cancelled==='boolean'&&
      typeof x.is_archived==='boolean'&&typeof x.writable==='boolean'&&
      Number.isSafeInteger(x.total)&&(x.total as number)>=0&&(x.total as number)<=200&&
      Array.isArray(x.players)&&x.players.length<=200&&x.players.every((row:unknown)=>{
        if(!row||typeof row!=='object'||Array.isArray(row))return false;
        const p=row as Record<string,unknown>;
        return idSafe(p.player_id)&&typeof p.name==='string'&&p.name.length<=200&&
          (p.attendance===null||p.attendance==='present'||p.attendance==='absent')&&
          (p.historical_attendance===null||p.historical_attendance==='present'||p.historical_attendance==='absent')&&
          ['legacy_import','os_manager','not_recorded'].includes(String(p.attendance_source))&&
          (p.rsvp===null||['going','not_going','maybe','no_response'].includes(String(p.rsvp)))&&
          Number.isSafeInteger(p.version)&&(p.version as number)>=0;
      });
  }
  onMount(()=>{start=date(-90);end=date(0);});
  $effect(()=>{
    const group=groupId,actor=actorId,from=start,to=end,allowed=canRead;
    void refreshSessions;void actor;
    sessions=null;eventId='';register=null;message='';error='';loading=false;
    if(!browser||!allowed||!idSafe(group)||!daySafe(from)||!daySafe(to)||from>to)return;
    const controller=new AbortController();loading=true;
    untrack(()=>void(async()=>{
      try{
        const query=new URLSearchParams({organization_id:'lefteria_fc_academy',group_id:group,start:from,end:to});
        const response=await fetch(base+'/?'+query,{credentials:'same-origin',
          cache:'no-store',headers:{Accept:'application/json'},signal:controller.signal});
        if(controller.signal.aborted)return;
        if(response.status===401||response.status===403){void auth.bootstrap();throw Error('Η πρόσβαση άλλαξε.');}
        if(!response.ok)throw Error('Οι προπονήσεις δεν είναι διαθέσιμες.');
        const raw:unknown=await response.json();
        if(!validSessions(raw,group,from,to))throw Error('Μη επαληθευμένη λίστα προπονήσεων.');
        if(!controller.signal.aborted)sessions=raw;
      }catch(e){if(!controller.signal.aborted)error=e instanceof Error?e.message:'Σφάλμα φόρτωσης.';}
      finally{if(!controller.signal.aborted)loading=false;}
    })());
    return()=>controller.abort();
  });
  $effect(()=>{
    const selected=eventId,actor=actorId,allowed=canRead,group=groupId;
    void refreshRegister;void actor;void group;
    register=null;loadingRegister=false;
    if(!browser||!allowed||!idSafe(selected))return;
    const controller=new AbortController();loadingRegister=true;
    untrack(()=>void(async()=>{
      try{
        const response=await fetch(base+'/'+encodeURIComponent(selected)+'/attendance?'+owner,{
          credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'},signal:controller.signal});
        if(controller.signal.aborted)return;
        if(response.status===401||response.status===403){void auth.bootstrap();throw Error('Η πρόσβαση άλλαξε.');}
        if(!response.ok)throw Error('Το παρουσιολόγιο δεν είναι διαθέσιμο.');
        const raw:unknown=await response.json();
        if(!validRegister(raw,selected))throw Error('Μη επαληθευμένα στοιχεία παρουσίας.');
        if(!controller.signal.aborted)register=raw;
      }catch(e){if(!controller.signal.aborted)error=e instanceof Error?e.message:'Σφάλμα παρουσιών.';}
      finally{if(!controller.signal.aborted)loadingRegister=false;}
    })());
    return()=>controller.abort();
  });
  async function mark(row:Player,status:'present'|'absent'){
    if(!browser||!canRead||!canWrite||!register?.writable||!eventId||saving||
      !register.players.some(p=>p.player_id===row.player_id))return;
    const ownerActor=actorId,group=groupId,event=eventId,version=row.version;
    saving=row.player_id;error='';message='';
    try{
      const res=await fetch(base+'/'+encodeURIComponent(event)+'/attendance?'+owner,{
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({player_id:row.player_id,status,expected_version:version})
      });
      if(ownerActor!==actorId||group!==groupId||event!==eventId)return;
      if(res.status===401||res.status===403){void auth.bootstrap();throw Error('Η εξουσιοδότηση άλλαξε.');}
      if(res.status===409){refreshRegister++;throw Error('Η παρουσία άλλαξε. Ανανεώστε τα στοιχεία.');}
      if(!res.ok)throw Error('Η παρουσία δεν αποθηκεύτηκε.');
      const value:unknown=await res.json();
      if(!value||typeof value!=='object'||Array.isArray(value))throw Error('Μη επαληθευμένη απάντηση.');
      const x=value as Record<string,unknown>;
      if(x.event_id!==event||x.player_id!==row.player_id||x.status!==status||
        !Number.isSafeInteger(x.version)||typeof x.changed!=='boolean')
        throw Error('Μη επαληθευμένη καταχώριση.');
      message='Η παρουσία καταχωρίστηκε με επιβεβαίωση του συστήματος.';
      refreshRegister++;
    }catch(e){error=e instanceof Error?e.message:'Αποτυχία αποθήκευσης.';}
    finally{saving='';}
  }
</script>

<section class="admin-group-attendance-panel" aria-labelledby="group-attendance-title"
  data-testid="group-embedded-attendance">
  <h4 id="group-attendance-title">Παρουσίες ανά προπόνηση και αθλητή</h4>
  <p class="admin-muted">Οι παρουσίες καταγράφονται για κάθε συγκεκριμένη προπόνηση.
    Η δήλωση συμμετοχής (RSVP) δεν υποκαθιστά την πραγματική παρουσία.</p>
  {#if !canRead}
    <p role="alert">Δεν υπάρχει επαληθευμένο δικαίωμα παρουσιολογίου.</p>
  {:else}
    <div class="attendance-dates">
      <label>Από <input type="date" bind:value={start} /></label>
      <label>Έως <input type="date" bind:value={end} /></label>
      <button class="admin-secondary" type="button" disabled={loading}
        onclick={()=>refreshSessions++}>Ανανέωση προπονήσεων</button>
    </div>
    {#if loading}<p role="status">Φόρτωση προπονήσεων…</p>{/if}
    {#if error}<p role="alert" class="attendance-error">{error}</p>{/if}
    {#if message}<p role="status" class="attendance-notice">{message}</p>{/if}
    {#if sessions}
      {#if sessions.sessions.length===0}<p>Δεν υπάρχουν προπονήσεις σε αυτό το διάστημα.</p>{/if}
      <div class="attendance-events" aria-label="Προπονήσεις τμήματος">
        {#each sessions.sessions as item (item.id)}
          <button type="button" class:chosen={eventId===item.id}
            aria-pressed={eventId===item.id} onclick={()=>eventId=item.id}>
            <strong>{item.day}</strong><span>{item.title}</span>
            {#if item.is_cancelled||item.is_archived}<small>Ακυρωμένη / αρχειοθετημένη</small>{/if}
          </button>
        {/each}
      </div>
    {/if}
    {#if loadingRegister}<p role="status">Φόρτωση παρουσιολογίου…</p>{/if}
    {#if register}
      <div class="attendance-register-head">
        <h5>{register.title} · {register.day}</h5>
        <button type="button" class="admin-secondary" onclick={()=>refreshRegister++}>Ανανέωση παρουσιών</button>
      </div>
      {#if totals}<p class="admin-muted">Επαληθευμένες παρουσίες ανά αθλητή · Σύνολο {register.players.length}</p>{/if}
      <div class="attendance-register-list">
        {#each register.players as player (player.player_id)}
          <article class="attendance-player">
            <div><strong>{player.name}</strong><small>Δήλωση: {player.rsvp==='going'?'Θα έρθει':player.rsvp==='not_going'?'Δεν θα έρθει':player.rsvp==='maybe'?'Ίσως':'Χωρίς δήλωση'}</small>
              <span>Παρουσία: {player.attendance?statuses[player.attendance]:'Χωρίς καταχώριση'}</span></div>
            {#if canWrite && register.writable}
              <div class="attendance-actions">
                <button type="button" disabled={!!saving} class:chosen={player.attendance==='present'}
                  onclick={()=>void mark(player,'present')}>Παρόν</button>
                <button type="button" disabled={!!saving} class:chosen={player.attendance==='absent'}
                  onclick={()=>void mark(player,'absent')}>Απών</button>
              </div>
            {/if}
          </article>
        {/each}
      </div>
      {#if !register.writable}<p class="admin-muted">Η προπόνηση δεν δέχεται αλλαγές παρουσίας.</p>{/if}
    {/if}
  {/if}
</section>
<style>
  .admin-group-attendance-panel{display:grid;gap:16px;max-width:100%;min-width:0;padding:15px 0}
  .admin-group-attendance-panel h4,.admin-group-attendance-panel h5{margin:0;font-size:23px}
  .admin-group-attendance-panel p{margin:0}
  .attendance-dates{display:flex;flex-wrap:wrap;align-items:end;gap:12px}
  .attendance-dates label{display:grid;gap:6px;font-size:12px}
  .attendance-dates input{min-width:180px;min-height:42px}
  .attendance-events{display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:10px}
  .attendance-events button{display:grid;gap:5px;text-align:left;min-height:88px;color:#eee;
    background:#151719;border:1px solid #51555a;padding:12px;cursor:pointer}
  .attendance-events button.chosen{border-color:#ffcc61;background:#2f2415}
  .attendance-events span,.attendance-events small{font-size:12px}
  .attendance-register-head{display:flex;justify-content:space-between;gap:12px;align-items:center;flex-wrap:wrap}
  .attendance-register-list{display:grid;gap:9px}
  .attendance-player{display:flex;justify-content:space-between;align-items:center;gap:15px;
    border:1px solid #4b4b4b;padding:12px;background:#111}
  .attendance-player>div:first-child{display:grid;gap:5px}
  .attendance-player small,.attendance-player span{color:#c9c9c9;font-size:12px}
  .attendance-actions{display:flex;gap:6px;flex-wrap:wrap}
  .attendance-actions button{min-height:40px;min-width:70px;padding:8px;border:1px solid #837448;background:#171819;color:#eee}
  .attendance-actions button.chosen{background:#f6bb38;color:#111}
  .attendance-error{color:#ffb5a6}.attendance-notice{color:#b9e6b9}
  @media(max-width:650px){.attendance-player{flex-direction:column;align-items:flex-start}}
</style>
