<script lang="ts">
  import {getContext,untrack,onMount} from 'svelte';
  import {browser} from '$app/env';
  import {ADMIN_AUTH_CONTEXT,type AdminAuthController} from '../../../lib/admin/controller.ts';
  import {hasAdminPermission} from '../../../lib/admin/permissions.ts';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';

  type Group={id:string;name:string;kind:'academy_group';active_players:number};
  type GroupDirectory={
    organization_id:'lefteria_fc_academy';ready:true;groups:Group[];
  };
  type Session={id:string;day:string;title:string;is_cancelled:boolean;is_archived:boolean};
  type SessionList={
    organization_id:'lefteria_fc_academy';group_id:string;
    from:string;to:string;total:number;sessions:Session[];
  };
  type AttendanceStatus='present'|'absent'|null;
  type PlayerRow={
    player_id:string;name:string;rsvp:'going'|'not_going'|'maybe'|'no_response'|null;
    historical_attendance:AttendanceStatus;
    attendance:AttendanceStatus;
    attendance_source:'legacy_import'|'os_manager'|'not_recorded';
    version:number;
  };
  type Register={
    organization_id:'lefteria_fc_academy';event_id:string;title:string;day:string;
    is_cancelled:boolean;is_archived:boolean;writable:boolean;total:number;players:PlayerRow[];
  };
  const auth=getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  const permitted=$derived($auth.status==='authenticated'&&$auth.session &&
    ['manager','super_admin'].includes($auth.session.user.role)&&
    hasAdminPermission($auth.session.user.permissions,'attendance:read')&&
    $auth.session.user.organizations.some(org=>org.id==='lefteria_fc_academy'));
  const canWrite=$derived(!!permitted&&$auth.session&&
    hasAdminPermission($auth.session.user.permissions,'attendance:write'));

  let groups=$state<Group[]>([]);
  let groupId=$state('');
  let sessions=$state<SessionList|null>(null);
  let selected=$state<string|null>(null);
  let register=$state<Register|null>(null);
  let start=$state('');
  let end=$state('');
  let loadingGroups=$state(false);
  let loadingSessions=$state(false);
  let loadingRegister=$state(false);
  let saving=$state<string|null>(null);
  let error=$state('');
  let notice=$state('');
  let groupReload=$state(0);
  let sessionReload=$state(0);
  let registerReload=$state(0);
  const idValid=(value:unknown):value is string=>
    typeof value==='string'&&value.length>=1&&value.length<=128&&!/[\x00-\x1f\x7f]/.test(value);
  const dayValid=(value:unknown):value is string=>
    typeof value==='string'&&/^\d{4}-\d{2}-\d{2}$/.test(value);
  const statuses:Record<Exclude<AttendanceStatus,null>,string>={
    present:'Παρόν',absent:'Απών'
  };
  const rsvp:Record<string,string>={
    going:'Θα έρθει',not_going:'Δεν θα έρθει',
    maybe:'Ίσως',no_response:'Χωρίς απάντηση'
  };
  function localDate(days:number):string {
    const now=new Date();
    now.setUTCDate(now.getUTCDate()+days);
    const parts=new Intl.DateTimeFormat('en-GB',{
      timeZone:'Asia/Nicosia',year:'numeric',month:'2-digit',day:'2-digit'
    }).formatToParts(now);
    const field=(type:string)=>parts.find(p=>p.type===type)?.value??'';
    return field('year')+'-'+field('month')+'-'+field('day');
  }
  onMount(()=>{
    start=localDate(-90);
    end=localDate(0);
  });
  const base='/api/admin/academy/training';
  const scope='organization_id=lefteria_fc_academy';

  function validGroups(value:unknown):value is GroupDirectory{
    if(!value||typeof value!=='object'||Array.isArray(value))return false;
    const x=value as Record<string,unknown>;
    return x.organization_id==='lefteria_fc_academy'&&x.ready===true&&
      Array.isArray(x.groups)&&x.groups.length<=150&&x.groups.every((g:unknown)=>{
        if(!g||typeof g!=='object'||Array.isArray(g))return false;
        const row=g as Record<string,unknown>;
        return idValid(row.id)&&typeof row.name==='string'&&row.name.length<=200&&
          row.kind==='academy_group'&&Number.isInteger(row.active_players);
      });
  }
  function validSessions(value:unknown,group:string,from:string,to:string):
    value is SessionList {
    if(!value||typeof value!=='object'||Array.isArray(value))return false;
    const x=value as Record<string,unknown>;
    return x.organization_id==='lefteria_fc_academy'&&
      x.group_id===group&&x.from===from&&x.to===to&&
      Number.isSafeInteger(x.total)&&(x.total as number)>=0&&(x.total as number)<=500&&
      Array.isArray(x.sessions)&&x.sessions.length<=500&&x.sessions.every((s:unknown)=>{
        if(!s||typeof s!=='object'||Array.isArray(s))return false;
        const row=s as Record<string,unknown>;
        return idValid(row.id)&&dayValid(row.day)&&
          typeof row.title==='string'&&row.title.length<=250&&
          typeof row.is_cancelled==='boolean'&&typeof row.is_archived==='boolean';
      });
  }
  function validPlayer(value:unknown):value is PlayerRow {
    if(!value||typeof value!=='object'||Array.isArray(value))return false;
    const x=value as Record<string,unknown>;
    return idValid(x.player_id)&&typeof x.name==='string'&&x.name.length<=200&&
      (x.rsvp===null||typeof x.rsvp==='string'&&Object.hasOwn(rsvp,x.rsvp))&&
      (x.historical_attendance===null||x.historical_attendance==='present'||x.historical_attendance==='absent')&&
      (x.attendance===null||x.attendance==='present'||x.attendance==='absent')&&
      ['os_manager','legacy_import','not_recorded'].includes(String(x.attendance_source))&&
      Number.isSafeInteger(x.version)&&(x.version as number)>=0&&(x.version as number)<=10000;
  }
  function validRegister(value:unknown,event:string):value is Register{
    if(!value||typeof value!=='object'||Array.isArray(value))return false;
    const x=value as Record<string,unknown>;
    return x.organization_id==='lefteria_fc_academy'&&x.event_id===event&&
      typeof x.title==='string'&&x.title.length<=250&&dayValid(x.day)&&
      typeof x.is_cancelled==='boolean'&&typeof x.is_archived==='boolean'&&
      typeof x.writable==='boolean'&&
      Number.isSafeInteger(x.total)&&(x.total as number)>=0&&(x.total as number)<=200&&
      Array.isArray(x.players)&&x.players.length<=200&&
      x.players.every(validPlayer);
  }

  $effect(()=>{
    const actor=$auth.session?.user.id;
    const allowed=permitted;void groupReload;void actor;
    let active=true;
    if(!browser||!allowed){groups=[];groupId='';return;}
    loadingGroups=true;error='';
    untrack(()=>{void(async()=>{
      try{
        const response=await fetch('/api/admin/football/audiences?'+scope,
          {credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
        if(!active)return;
        if(response.status===401||response.status===403){
          void auth.bootstrap();throw new Error('Η συνεδρία άλλαξε.');
        }
        if(!response.ok)throw new Error('Δεν είναι διαθέσιμα τα τμήματα.');
        const payload:unknown=await response.json();
        if(!validGroups(payload))throw new Error('Μη επαληθευμένα τμήματα Ακαδημίας.');
        if(active){
          groups=payload.groups;
          if(!groups.some(g=>g.id===groupId))groupId=groups[0]?.id??'';
        }
      }catch(e){
        if(active){groups=[];groupId='';error=e instanceof Error?e.message:'Σφάλμα τμημάτων.';}
      }finally{if(active)loadingGroups=false;}
    })();});
    return()=>{active=false;};
  });

  $effect(()=>{
    const actor=$auth.session?.user.id;
    const allowed=permitted;const group=groupId;const from=start;const to=end;
    void sessionReload;void actor;
    let active=true;
    selected=null;register=null;sessions=null;loadingSessions=false;
    if(!browser||!allowed||!idValid(group)||!dayValid(from)||!dayValid(to)||
      from>to)return;
    loadingSessions=true;error='';
    untrack(()=>{void(async()=>{
      try{
        const params=new URLSearchParams({
          organization_id:'lefteria_fc_academy',
          group_id:group,start:from,end:to
        });
        const response=await fetch(base+'/?'+params,{
          credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}
        });
        if(!active)return;
        if(response.status===401||response.status===403){
          void auth.bootstrap();throw new Error('Η συνεδρία άλλαξε.');
        }
        if(!response.ok)throw new Error('Οι προπονήσεις δεν είναι ακόμη διαθέσιμες.');
        const payload:unknown=await response.json();
        if(!validSessions(payload,group,from,to))throw new Error('Μη επαληθευμένο ημερολόγιο.');
        if(active)sessions=payload;
      }catch(e){
        if(active){sessions=null;error=e instanceof Error?e.message:'Σφάλμα προπονήσεων.';}
      }finally{if(active)loadingSessions=false;}
    })();});
    return()=>{active=false;};
  });

  $effect(()=>{
    const actor=$auth.session?.user.id;
    const allowed=permitted;const event=selected;
    void registerReload;void actor;
    let active=true;
    register=null;loadingRegister=false;
    if(!browser||!allowed||!idValid(event))return;
    loadingRegister=true;error='';
    untrack(()=>{void(async()=>{
      try{
        const response=await fetch(base+'/'+encodeURIComponent(event)+
          '/attendance?'+scope,{
          credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}
        });
        if(!active)return;
        if(response.status===401||response.status===403){
          void auth.bootstrap();throw new Error('Τα δικαιώματα άλλαξαν.');
        }
        if(!response.ok)throw new Error('Το παρουσιολόγιο δεν είναι διαθέσιμο.');
        const payload:unknown=await response.json();
        if(!validRegister(payload,event))throw new Error('Μη επαληθευμένο παρουσιολόγιο.');
        if(active)register=payload;
      }catch(e){
        if(active){register=null;error=e instanceof Error?e.message:'Σφάλμα παρουσιολογίου.';}
      }finally{if(active)loadingRegister=false;}
    })();});
    return()=>{active=false;};
  });

  async function mark(player:PlayerRow,status:'present'|'absent'){
    if(!canWrite||!browser||!selected||!register?.writable||
      !register.players.some(p=>p.player_id===player.player_id)||saving)return;
    const actor=$auth.session?.user.id;
    const event=selected;const group=groupId;const expected=player.version;
    saving=player.player_id;notice='';error='';
    try{
      const response=await fetch(base+'/'+encodeURIComponent(event)+
        '/attendance?'+scope,{
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({
          player_id:player.player_id,status,expected_version:expected
        })
      });
      if($auth.session?.user.id!==actor||selected!==event||
         groupId!==group||!canWrite)return;
      if(response.status===401||response.status===403){
        void auth.bootstrap();throw new Error('Η συνεδρία ή οι αναθέσεις άλλαξαν.');
      }
      if(response.status===409){
        registerReload++;throw new Error('Άλλαξε ήδη η παρουσία. Ελέγξτε τα νέα στοιχεία.');
      }
      if(!response.ok)throw new Error('Η παρουσία δεν καταχωρίστηκε.');
      const payload:unknown=await response.json();
      if(!payload||typeof payload!=='object'||Array.isArray(payload)||
        !('event_id' in payload)||payload.event_id!==event||
        !('player_id' in payload)||payload.player_id!==player.player_id||
        !('status' in payload)||payload.status!==status||
        !('version' in payload)||!Number.isInteger(payload.version)||
        !('changed' in payload)||typeof payload.changed!=='boolean')
        throw new Error('Μη επαληθευμένη καταχώριση παρουσίας.');
      notice='Η παρουσία καταχωρίστηκε ή είχε ήδη επιβεβαιωθεί.';
      registerReload++;
    }catch(e){error=e instanceof Error?e.message:'Η παρουσία δεν καταχωρίστηκε.';}
    finally{saving=null;}
  }
</script>

{#if $auth.status==='authenticated'&&$auth.session}
  <AdminFrame user={$auth.session.user} active="attendance"
    busy={$auth.busy} onLogout={()=>void auth.logout()}>
    {#if permitted}
      <div class="academy-attendance" data-testid="admin-academy-attendance">
        <p class="admin-eyebrow">ΑΚΑΔΗΜΙΑ · ΠΡΟΠΟΝΗΣΕΙΣ · ΠΑΡΟΥΣΙΟΛΟΓΙΟ</p>
        <h1>Παρουσίες προπονήσεων</h1>
        <p class="admin-muted">Οι απαντήσεις συμμετοχής γονέων είναι διαφορετικές
          από την πραγματική παρουσία. Διατηρούμε τα αρχικά ιστορικά δεδομένα
          χωρίς τροποποίηση. Η καταχώριση παρουσίας απαιτεί ενεργό
          και εξουσιοδοτημένο διαχειριστή.</p>
        <section class="admin-panel attendance-filters">
          <label>Τμήμα
            <select value={groupId} disabled={loadingGroups}
              onchange={e=>groupId=e.currentTarget.value} data-testid="attendance-group">
              {#each groups as group (group.id)}
                <option value={group.id}>{group.name}</option>
              {/each}
            </select>
          </label>
          <label>Από <input type="date" bind:value={start}/></label>
          <label>Έως <input type="date" bind:value={end}/></label>
          <button class="admin-secondary" type="button"
            disabled={loadingGroups||loadingSessions}
            onclick={()=>{groupReload++;sessionReload++;}}>Ανανέωση</button>
        </section>
        {#if notice}<p class="attendance-notice" role="status">{notice}</p>{/if}
        {#if error}<p role="alert" class="attendance-error">{error}</p>{/if}
        {#if loadingGroups||loadingSessions}
          <section class="admin-panel" role="status">Φόρτωση προπονήσεων…</section>
        {:else if sessions}
          <section class="admin-panel">
            <h2>Ημερολόγιο προπονήσεων · {sessions.total}</h2>
            {#if sessions.sessions.length===0}
              <p>Δεν υπάρχουν επαληθευμένες προπονήσεις για αυτό το τμήμα.</p>
            {:else}
              <div class="attendance-events">
                {#each sessions.sessions as session (session.id)}
                  <button type="button"
                    class:selected={selected===session.id}
                    onclick={()=>selected=session.id}
                    data-testid="attendance-session">
                    <strong>{session.title}</strong>
                    <span>{session.day}</span>
                    {#if session.is_cancelled||session.is_archived}
                      <span>Ακυρωμένη ή αρχειοθετημένη</span>
                    {/if}
                  </button>
                {/each}
              </div>
            {/if}
          </section>
        {:else if !loadingGroups}
          <section class="admin-panel">Δεν υπάρχει διαθέσιμο επαληθευμένο πρόγραμμα.</section>
        {/if}
        {#if loadingRegister}
          <section class="admin-panel" role="status">Φόρτωση παρουσιολογίου…</section>
        {:else if register}
          <section class="admin-panel attendance-register">
            <div class="attendance-register-head">
              <div><h2>{register.title}</h2>
                <p>{register.day} · {register.total} επιτρεπόμενοι αθλητές</p></div>
              <button class="admin-secondary" onclick={()=>registerReload++}
                type="button">Ανανέωση παρουσιών</button>
            </div>
            {#if !register.writable}
              <p class="attendance-warning">Η προπόνηση είναι μελλοντική,
                ακυρωμένη ή αρχειοθετημένη. Η παρουσία δεν μπορεί
                να τροποποιηθεί.</p>
            {/if}
            <ul class="attendance-players">
              {#each register.players as player (player.player_id)}
                <li>
                  <div class="attendance-player">
                    <strong>{player.name}</strong>
                    <span>RSVP: {player.rsvp?rsvp[player.rsvp]:'—'} ·
                      Παρουσία: {player.attendance?statuses[player.attendance]:'Χωρίς καταχώριση'}</span>
                    {#if player.attendance_source==='legacy_import'}
                      <small>Ιστορική παρουσία από την αρχική εισαγωγή</small>
                    {:else if player.attendance_source==='os_manager'}
                      <small>Καταχώριση διαχείρισης · έκδοση {player.version}</small>
                    {/if}
                  </div>
                  {#if canWrite&&register.writable}
                    <div class="attendance-actions">
                      <button type="button" disabled={saving!==null}
                        class:chosen={player.attendance==='present'}
                        onclick={()=>void mark(player,'present')}
                        data-testid="attendance-present">Παρόν</button>
                      <button type="button" disabled={saving!==null}
                        class:chosen={player.attendance==='absent'}
                        onclick={()=>void mark(player,'absent')}
                        data-testid="attendance-absent">Απών</button>
                    </div>
                  {/if}
                </li>
              {/each}
            </ul>
          </section>
        {/if}
      </div>
    {:else}
      <section class="admin-panel" data-testid="attendance-denied">
        <h1>Δεν έχετε δικαίωμα πρόσβασης στο παρουσιολόγιο της Ακαδημίας.</h1>
        <a href="/admin">Επιστροφή στη διαχείριση</a>
      </section>
    {/if}
  </AdminFrame>
{:else}
  <AdminSessionStatus unavailable={$auth.status==='unavailable'}
    busy={$auth.busy} error={$auth.error}
    onRetry={()=>void auth.bootstrap()} onLogout={()=>void auth.logout()}/>
{/if}

<style>
  .academy-attendance{max-width:1150px}.academy-attendance h1{margin:0 0 12px}
  .academy-attendance>.admin-muted{max-width:85ch;margin-bottom:20px}
  .attendance-filters{display:flex;gap:12px;flex-wrap:wrap;align-items:end;margin-bottom:14px}
  .attendance-filters label{display:grid;gap:7px;color:#bbc0c3;font-size:12px}
  .attendance-filters select{min-width:min(310px,100%)}
  .attendance-filters input{max-width:180px}
  .attendance-notice{color:#b8e7bb}.attendance-error{color:#ffb1a4}
  .attendance-events{display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:10px}
  .attendance-events button{border:1px solid #3c3f41;background:#151719;color:inherit;
    border-radius:12px;display:grid;text-align:left;gap:9px;padding:14px;cursor:pointer}
  .attendance-events button.selected{border-color:#f5a623;box-shadow:0 0 0 1px #f5a623}
  .attendance-events button span{font-size:12px;color:#b8babb}
  .attendance-register{margin-top:16px}
  .attendance-register-head{display:flex;flex-wrap:wrap;justify-content:space-between;gap:12px}
  .attendance-register-head h2{margin:0}.attendance-register-head p{font-size:13px;color:#aaa}
  .attendance-warning{color:#ffc989}
  .attendance-players{list-style:none;margin:0;padding:0}
  .attendance-players li{display:flex;justify-content:space-between;flex-wrap:wrap;
    gap:12px;align-items:center;border-top:1px solid #303335;padding:16px 0}
  .attendance-player{display:grid;gap:6px;min-width:0;overflow-wrap:anywhere}
  .attendance-player span,.attendance-player small{font-size:12px;color:#b9bdc0}
  .attendance-actions{display:flex;gap:8px;flex-wrap:wrap}
  .attendance-actions button{border:1px solid #777;color:#eee;padding:9px 13px;
    border-radius:9px;background:transparent;cursor:pointer}
  .attendance-actions button.chosen{background:#423319;border-color:#f5a623;color:#ffd777}
  .attendance-actions button:disabled{opacity:.55;cursor:not-allowed}
  @media(max-width:620px){.attendance-filters label{width:100%}.attendance-filters select{width:100%}}
</style>
