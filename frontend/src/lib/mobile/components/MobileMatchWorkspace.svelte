<script lang="ts">
  import {onMount} from 'svelte';
  import type {MobileAuthController} from '../controller.ts';
  import type {MobileCalendarItem} from '../calendar.ts';
  import {planGrassrootsRotation,rotationClock} from '../match-timing.ts';
  import {recommendGrassrootsStarters,repeatedGrassrootsReserve,statisticFromInputs,
    type Assignment,type MatchAction,type MatchCommand,type MatchWorkspace,type SquadPlayer,type MatchStatistic} from '../match-workspace.ts';
  let {controller,item,role}:{controller:MobileAuthController;item:MobileCalendarItem;role:'coach'|'management'}=$props();
  type StatInput={minutes:string;goals:string;assists:string};
  let workspace=$state<MatchWorkspace|null>(null),squad=$state<SquadPlayer[]>([]);
  let statDraft=$state<Record<string,StatInput>>({});
  let attendanceDraft=$state<string[]>([]);
  let rotationNote=$state(''),reviewReason=$state(''),search=$state(''),startingCount=$state(0);
  let busy=$state(true),error=$state(''),notice=$state(''),reviewConfirmed=$state(false);
  let alive=false,request=0;
  const grassroots=$derived(workspace?.mode==='grassroots');
  const canEdit=$derived(role==='coach'&&workspace&&!workspace.completed&&['draft','selected'].includes(workspace.state)&&workspace.timing.attendance_ids===null);
  const canStats=$derived(role==='coach'&&workspace?.completed&&!workspace.roster_stale&&['selected','rejected'].includes(workspace.state));
  const canReview=$derived(role==='management'&&workspace?.completed&&!workspace.roster_stale&&workspace.state==='submitted');
  const configuredFormat=$derived(workspace?.timing.players_on_field??null);
  const targetStarters=$derived(configuredFormat===null?startingCount:Math.min(configuredFormat,squad.length));
  const gameDay=$derived(item.date===new Intl.DateTimeFormat('en-CA',{timeZone:'Asia/Nicosia',year:'numeric',month:'2-digit',day:'2-digit'}).format(new Date()));
  const canAttendance=$derived(role==='coach'&&grassroots&&gameDay&&workspace?.state==='selected'&&!workspace.completed&&
    !workspace.roster_stale&&workspace.timing.duration_minutes!==null&&configuredFormat!==null&&
    JSON.stringify(squad)===JSON.stringify(workspace.squad));
  const plan=$derived.by(()=>{
    if(!workspace||!grassroots||workspace.roster_stale)return null;
    const {attendance_ids,duration_minutes,players_on_field}=workspace.timing;
    if(!attendance_ids||duration_minutes===null||players_on_field===null)return null;
    const ordered=workspace.squad.filter(p=>attendance_ids.includes(p.player_id))
      .sort((a,b)=>Number(b.assignment==='starter')-Number(a.assignment==='starter')).map(p=>p.player_id);
    return planGrassrootsRotation(duration_minutes,players_on_field,ordered);
  });
  const starters=$derived(squad.filter(p=>p.assignment==='starter').length);
  const reserves=$derived(squad.length-starters);
  const repeatReserve=$derived(grassroots&&workspace&&repeatedGrassrootsReserve(workspace.roster,squad));
  const visible=$derived((workspace?.roster??[]).filter(p=>
    !search.trim()||p.name.toLocaleLowerCase('el-CY').includes(search.trim().toLocaleLowerCase('el-CY'))||
    String(p.number)===search.trim()));
  const states:Record<string,string>={draft:'Πρόχειρο',selected:'Επιβεβαιωμένη αποστολή',submitted:'Προς έλεγχο',approved:'Εγκρίθηκε',rejected:'Επιστροφή για διόρθωση'};

  function clear(){
    workspace=null;squad=[];statDraft={};rotationNote='';reviewReason='';reviewConfirmed=false;startingCount=0;search='';attendanceDraft=[];
  }
  function apply(value:MatchWorkspace){
    workspace=value;squad=value.squad.map(row=>({...row}));rotationNote=value.rotation_note;
    startingCount=value.squad.filter(row=>row.assignment==='starter').length;
    attendanceDraft=[...(value.timing.attendance_ids??[])];
    reviewReason='';reviewConfirmed=false;
    statDraft=Object.fromEntries(value.squad.map(row=>{
      const s=value.statistics.find(stat=>stat.player_id===row.player_id);
      return [row.player_id,{minutes:s?String(s.minutes_played):'',goals:s?String(s.goals):'',assists:s?String(s.assists):''}];
    }));
  }
  async function load(){
    if(!alive||busy&&workspace)return;
    const marker=++request;clear();busy=true;error='';notice='';
    try{
      const data=await controller.getMatchWorkspace(item);
      if(!alive||marker!==request)return;
      apply(data);
    }catch{if(alive&&marker===request){clear();error='Δεν ήταν δυνατή η επαλήθευση της αποστολής. Ελέγξτε την ανάθεση και δοκιμάστε ξανά.';}}
    finally{if(alive&&marker===request)busy=false;}
  }
  onMount(()=>{
    alive=true;void load();
    return()=>{alive=false;request++;clear();};
  });
  function choose(playerId:string,value:string){
    if(!canEdit||busy||!workspace)return;
    const player=workspace.roster.find(p=>p.player_id===playerId);
    if(!player)return;
    if(value==='none'){squad=squad.filter(p=>p.player_id!==playerId);return;}
    if(value!=='starter'&&value!=='reserve')return;
    const prior=squad.find(p=>p.player_id===playerId);
    if(!prior&&squad.length>=60){error='Η αποστολή επιτρέπει έως 60 παίκτες.';return;}
    if(value==='starter'&&prior?.assignment!=='starter'&&starters>=(configuredFormat??11)){error='Η αρχική ομάδα δεν μπορεί να ξεπερνά τη μορφή αγώνα.';return;}
    const position=grassroots?null:prior?.position??(player.position?.trim().slice(0,40)||null);
    const row:SquadPlayer={player_id:playerId,assignment:value,position};
    squad=prior?squad.map(p=>p.player_id===playerId?row:p):[...squad,row];error='';notice='';
  }
  function position(playerId:string,value:string){
    if(!canEdit||busy||grassroots)return;
    squad=squad.map(p=>p.player_id===playerId?{...p,position:value.trim()||null}:p);
  }
  function suggest(){
    if(!canEdit||busy||!workspace||!grassroots)return;
    try{
      squad=recommendGrassrootsStarters(workspace.roster,squad,targetStarters);
      error='';notice='Η πρόταση εναλλαγής είναι έτοιμη. Ελέγξτε και επιβεβαιώστε την αρχική ομάδα.';
    }catch{error='Επίλεξε πρώτα την αποστολή και τον αριθμό παικτών που ξεκινούν (1–11).';}
  }
  function toggleAttendance(id:string,checked:boolean){
    if(!canAttendance||busy||!workspace?.squad.some(p=>p.player_id===id))return;
    attendanceDraft=checked?[...new Set([...attendanceDraft,id])]:attendanceDraft.filter(value=>value!==id);
  }
  function stat(playerId:string,field:keyof StatInput,value:string){
    if(!canStats||busy||!statDraft[playerId])return;
    statDraft={...statDraft,[playerId]:{...statDraft[playerId],[field]:value}};
  }
  function build(action:MatchAction):MatchCommand|null{
    if(!workspace)return null;
    const command:MatchCommand={action,expected_version:workspace.version,squad:null,statistics:null,rotation_note:null,review_reason:null};
    if(action==='confirm_attendance'){
      if(!canAttendance||!attendanceDraft.length){error='Επιβεβαίωσε τους παρόντες παίκτες την ημέρα του αγώνα.';return null;}
      command.attendance_ids=attendanceDraft;
    }else if(action==='save_squad'||action==='select_squad'){
      if(!canEdit)return null;
      if(action==='select_squad'&&(!starters||starters>11||(!grassroots&&(starters!==11||
        squad.some(p=>p.assignment==='starter'&&!p.position))))){
        error=grassroots?'Επίλεξε τουλάχιστον έναν παίκτη στην αρχική ομάδα.':'Η Πρώτη Ομάδα απαιτεί 11 αρχικούς παίκτες και θέση για καθέναν.';return null;
      }
      if(action==='select_squad'&&grassroots&&configuredFormat!==null&&starters!==targetStarters){
        error='Η αρχική ομάδα πρέπει να αντιστοιχεί στη μορφή του τμήματος και το μέγεθος αποστολής.';return null;
      }
      if(action==='select_squad'&&repeatReserve&&!rotationNote.trim()){
        error='Καταχώρισε αιτιολογία για την επανάληψη της εναλλαγής ή άλλαξε την αρχική ομάδα.';return null;
      }
      command.squad=squad.map(p=>({...p,position:grassroots?null:p.position}));
      command.rotation_note=grassroots?rotationNote.trim():null;
    }else if(action==='submit_statistics'){
      if(!canStats)return null;
      const rows:MatchStatistic[]=[];
      for(const player of workspace.squad){
        const draft=statDraft[player.player_id];
        const row=draft&&statisticFromInputs(player.player_id,draft.minutes,draft.goals,draft.assists);
        if(row&&workspace.timing.attendance_ids&&(row.minutes_played>(workspace.timing.duration_minutes??180)||
          (row.minutes_played>0&&!workspace.timing.attendance_ids.includes(player.player_id)))){
          error='Τα λεπτά πρέπει να συμφωνούν με την επιβεβαιωμένη παρουσία και διάρκεια αγώνα.';return null;
        }
        if(!row){error='Συμπλήρωσε ρητά λεπτά, γκολ και ασίστ για κάθε παίκτη της αποστολής.';return null;}
        rows.push(row);
      }
      command.statistics=rows;
    }else{
      if(!canReview||!reviewConfirmed)return null;
      if(action==='reject'&&!reviewReason.trim()){error='Απαιτείται αιτιολογία διόρθωσης.';return null;}
      command.review_reason=reviewReason.trim()||null;
    }
    return command;
  }
  async function save(action:MatchAction){
    if(busy||!alive)return;
    const command=build(action);if(!command)return;
    const marker=++request;busy=true;error='';notice='';
    try{
      const data=await controller.getMatchWorkspace(item,command);
      if(!alive||marker!==request)return;
      apply(data);notice='Η ενέργεια αποθηκεύτηκε και επιβεβαιώθηκε από τον διακομιστή.';
    }catch{
      if(alive&&marker===request){
        clear();error='Η ενέργεια δεν επιβεβαιώθηκε. Ανανέωσε για να ελέγξεις την πραγματική αποθηκευμένη κατάσταση.';
      }
    }finally{if(alive&&marker===request)busy=false;}
  }
  function name(id:string){return workspace?.roster.find(p=>p.player_id===id)?.name??'Μη διαθέσιμος παίκτης';}
  function assignmentLabel(a:Assignment|null){return a==='starter'?(grassroots?'Αρχική ομάδα':'Βασικός'):
    a==='reserve'?(grassroots?'Επόμενη εναλλαγή':'Αναπληρωματικός'):'—';}
</script>
<section class="workspace" aria-label="Αποστολή και στατιστικά αγώνα" data-testid="mobile-match-workspace">
  <header><div><h4>{grassroots?'Αποστολή & εναλλαγή παικτών':'Αποστολή & στατιστικά αγώνα'}</h4>
    {#if workspace}<p>{states[workspace.state]} · Έκδοση {workspace.version}</p>{/if}</div>
    <button type="button" disabled={busy} onclick={()=>void load()}>Ανανέωση</button></header>
  {#if busy&&!workspace}<p role="status">Φόρτωση επαληθευμένης αποστολής…</p>{/if}
  {#if error}<p class="error" role="alert">{error}</p>{/if}
  {#if notice}<p role="status">{notice}</p>{/if}
  {#if workspace}
    {#if workspace.timing.duration_minutes!==null&&configuredFormat!==null}
      <p class="hint">Ρυθμίσεις τμήματος: <strong>{configuredFormat} vs {configuredFormat}</strong> · {workspace.timing.duration_minutes} λεπτά
        · {workspace.timing.frozen?'Οριστικοποιημένες για τον αγώνα':'Αυτόματα από το τμήμα'}</p>
    {:else}
      <p role="status" class="hint">{workspace.timing.frozen?'Δεν υπάρχει ιστορική ρύθμιση διάρκειας για αυτόν τον αγώνα.':'Δεν έχουν οριστεί διάρκεια και μορφή στις ρυθμίσεις τμήματος.'}</p>
    {/if}
    {#if grassroots}<p class="hint">Grassroots · Χωρίς μόνιμες θέσεις. Τα παιδιά που ξεκίνησαν στην εναλλαγή προηγούμενων αγώνων έχουν προτεραιότητα στην επόμενη αρχική ομάδα. Το ιστορικό αφορά επιβεβαιωμένες αποστολές, όχι καταγεγραμμένη φυσική παρουσία.</p>
    {:else}<p class="hint">Πρώτη Ομάδα · 11 βασικοί, αγωνιστικές θέσεις και αναπληρωματικοί.</p>{/if}
    {#if workspace.roster_stale}<p class="error" role="alert">Η προηγούμενη αποστολή περιέχει παίκτες εκτός του τρέχοντος καταλόγου. {canEdit?'Δημιούργησε νέα αποστολή από τους διαθέσιμους παίκτες.':'Απαιτείται ελεγχόμενη διόρθωση από τον σύλλογο.'}</p>{/if}
    {#if canEdit}
      <div class="totals"><span>Αποστολή <strong>{squad.length}</strong></span><span>{grassroots?'Αρχική ομάδα':'Βασικοί'} <strong>{starters}</strong></span><span>{grassroots?'Εναλλαγή':'Αναπληρωματικοί'} <strong>{reserves}</strong></span></div>
      {#if grassroots}
        <div class="rotation">
          {#if configuredFormat!==null}
            <p>Μορφή τμήματος: {configuredFormat} vs {configuredFormat} · {targetStarters} στην αρχική ομάδα της αποστολής</p>
          {:else}
            <label>Παιδιά στην αρχική ομάδα (χωρίς ρυθμίσεις τμήματος)
              <input type="number" min="1" max="11" step="1" value={startingCount||''} disabled={busy}
                oninput={e=>startingCount=Number(e.currentTarget.value)}/></label>
          {/if}
          <button type="button" disabled={busy||!squad.length} onclick={suggest}>Πρότεινε δίκαιη εναλλαγή</button>
          <p class="hint">Επίλεξε πρώτα τα παιδιά της αποστολής. Η πρόταση δεν προσθέτει παιδιά αυτόματα ούτε αποφασίζει τη συμμετοχή τους.</p>
        </div>
      {/if}
      <label>Αναζήτηση παικτών <input type="search" bind:value={search} maxlength="120"/></label>
      <ul class="roster">
        {#each visible as player (player.player_id)}
          {@const selected=squad.find(p=>p.player_id===player.player_id)}
          <li>
            <div class="player"><strong>{player.name}</strong>
              {#if grassroots}<small>Προηγούμενη επιλογή: {assignmentLabel(player.last_assignment)} · Αρχές {player.starts} · Εναλλαγές {player.reserves}</small>
              {:else}<small>{player.number>0?'#'+player.number+' · ':''}{player.position??'Χωρίς θέση'}</small>{/if}</div>
            <label class="sr">Επιλογή για {player.name}
              <select aria-label={'Επιλογή για '+player.name} value={selected?.assignment??'none'} disabled={busy}
                onchange={e=>choose(player.player_id,e.currentTarget.value)}>
                <option value="none">Εκτός αποστολής</option>
                <option value="starter">{grassroots?'Αρχική ομάδα':'Βασικός'}</option>
                <option value="reserve">{grassroots?'Εναλλαγή':'Αναπληρωματικός'}</option>
              </select>
            </label>
            {#if !grassroots&&selected}
              <label>Θέση αγώνα <input value={selected.position??''} maxlength="40" disabled={busy}
                oninput={e=>position(player.player_id,e.currentTarget.value)}/></label>
            {/if}
          </li>
        {/each}
      </ul>
      {#if grassroots}
        {#if repeatReserve}<p class="error" role="status">Παιδί της προηγούμενης εναλλαγής παραμένει εκτός αρχικής ομάδας ενώ υπάρχει προηγούμενος βασικός που ξεκινά ξανά. Άλλαξε την πρόταση ή αιτιολόγησε την εξαίρεση.</p>{/if}
        <label>Σημείωση εναλλαγής / αιτιολογία εξαίρεσης
          <textarea maxlength="500" rows="3" bind:value={rotationNote} disabled={busy}></textarea></label>
      {/if}
      <div class="actions">
        <button type="button" disabled={busy} onclick={()=>void save('save_squad')}>Αποθήκευση προχείρου</button>
        <button type="button" class="primary" disabled={busy||!starters} onclick={()=>void save('select_squad')}>Επιβεβαίωση αποστολής</button>
      </div>
    {:else if workspace.squad.length}
      <ul class="roster">{#each workspace.squad as row (row.player_id)}
        <li><div class="player"><strong>{name(row.player_id)}</strong><small>{assignmentLabel(row.assignment)}{!grassroots&&row.position?' · '+row.position:''}</small></div></li>
      {/each}</ul>
      {#if workspace.rotation_note}<p class="hint">Σημείωση εναλλαγής: {workspace.rotation_note}</p>{/if}
    {/if}
    {#if grassroots&&workspace.state==='selected'&&!workspace.completed&&!workspace.roster_stale}
      <section class="rotation" aria-label="Παρουσίες αγώνα">
        <h5>Παρουσίες ημέρας αγώνα</h5>
        {#if canAttendance}
          <p class="hint">Σημείωσε μόνο τα παιδιά που είναι πραγματικά παρόντα. Οι δηλώσεις συμμετοχής δεν αποτελούν παρουσία.</p>
          {#each workspace.squad as row (row.player_id)}
            <label class="confirm"><input type="checkbox" checked={attendanceDraft.includes(row.player_id)} disabled={busy}
              onchange={e=>toggleAttendance(row.player_id,e.currentTarget.checked)}/> {name(row.player_id)}</label>
          {/each}
          <p>Παρόντες: {attendanceDraft.length} / {workspace.squad.length}</p>
          <button type="button" class="primary" disabled={busy||!attendanceDraft.length} onclick={()=>void save('confirm_attendance')}>Επιβεβαίωση παρουσιών & υπολογισμός εναλλαγών</button>
        {:else if !workspace.timing.attendance_ids}
          <p class="hint">Ο υπολογισμός ενεργοποιείται για τον ανατεθειμένο προπονητή την ημέρα του αγώνα, αφού επιβεβαιωθούν η αποστολή και οι ρυθμίσεις τμήματος.</p>
        {/if}
      </section>
    {/if}
    {#if plan}
      <section class="rotation" aria-label="Πλάνο εναλλαγών" data-testid="grassroots-attendance-rotation-plan">
        <h5>Αυτόματο πλάνο εναλλαγών</h5>
        {#if plan.short_handed}<p role="alert" class="error">Οι παρόντες είναι λιγότεροι από τη μορφή αγώνα. Απαιτείται απόφαση προπονητή πριν την έναρξη.</p>{/if}
        <p>Παρόντες: {workspace.timing.attendance_ids?.length} · Στόχος ανά παιδί: {rotationClock(plan.target_seconds)}</p>
        <p class="hint">{plan.periods.length===1?'Δεν χρειάζεται προγραμματισμένη εναλλαγή.':'Διάστημα: '+rotationClock(plan.min_interval_seconds)+(plan.min_interval_seconds===plan.max_interval_seconds?'':'–'+rotationClock(plan.max_interval_seconds))}</p>
        <ol class="roster">{#each plan.periods as period (period.start_seconds)}
          <li><strong>{rotationClock(period.start_seconds)} – {rotationClock(period.end_seconds)}</strong>
            <span>Στο γήπεδο: {period.on_field.map(name).join(', ')}</span>
            {#if period.start_seconds>0}<small>Μπαίνουν: {period.enter.map(name).join(', ')||'—'} · Βγαίνουν: {period.leave.map(name).join(', ')||'—'}</small>{/if}
          </li>
        {/each}</ol>
        <p class="hint">Προτεινόμενος χρόνος, όχι πραγματικά λεπτά συμμετοχής. Ο προπονητής επιβεβαιώνει τις πραγματικές αλλαγές και τα στατιστικά χωριστά.</p>
      </section>
    {/if}
    {#if workspace.review_reason}<p>Αιτιολογία ελέγχου: {workspace.review_reason}</p>{/if}
    {#if canStats}
      <h5>Πραγματικά λεπτά & στατιστικά</h5>
      <p class="hint">Συμπλήρωσε όλα τα πεδία για κάθε παίκτη. Μηδέν μόνο όταν επιβεβαιώνεται. Οι δηλώσεις συμμετοχής δεν συμπληρώνουν τα στατιστικά.</p>
      {#each workspace.squad as player (player.player_id)}
        <fieldset><legend>{name(player.player_id)}</legend>
          <div class="stat-grid">
            <label>Λεπτά <input type="number" min="0" max="180" step="1" disabled={busy}
              value={statDraft[player.player_id]?.minutes??''} oninput={e=>stat(player.player_id,'minutes',e.currentTarget.value)}/></label>
            <label>Γκολ <input type="number" min="0" max="50" step="1" disabled={busy}
              value={statDraft[player.player_id]?.goals??''} oninput={e=>stat(player.player_id,'goals',e.currentTarget.value)}/></label>
            <label>Ασίστ <input type="number" min="0" max="50" step="1" disabled={busy}
              value={statDraft[player.player_id]?.assists??''} oninput={e=>stat(player.player_id,'assists',e.currentTarget.value)}/></label>
          </div>
        </fieldset>
      {/each}
      <button type="button" class="primary" disabled={busy} onclick={()=>void save('submit_statistics')}>Υποβολή στατιστικών προς έλεγχο</button>
    {:else if workspace.statistics.length}
      <h5>Υποβληθέντα στατιστικά</h5>
      <div class="stats-scroll"><table><thead><tr><th>Παίκτης</th><th>Λεπτά</th><th>Γκολ</th><th>Ασίστ</th></tr></thead>
        <tbody>{#each workspace.statistics as row (row.player_id)}
          <tr><th scope="row">{name(row.player_id)}</th><td>{row.minutes_played}</td><td>{row.goals}</td><td>{row.assists}</td></tr>
        {/each}</tbody></table></div>
    {/if}
    {#if canReview}
      <label>Σχόλιο / αιτιολογία επιστροφής <textarea bind:value={reviewReason} maxlength="500" rows="3" disabled={busy}></textarea></label>
      <label class="confirm"><input type="checkbox" bind:checked={reviewConfirmed} disabled={busy}/> Επιβεβαιώνω ότι έλεγξα την αποστολή και τα στατιστικά.</label>
      <div class="actions"><button type="button" disabled={busy||!reviewConfirmed||!reviewReason.trim()} onclick={()=>void save('reject')}>Επιστροφή για διόρθωση</button>
        <button type="button" class="primary" disabled={busy||!reviewConfirmed} onclick={()=>void save('approve')}>Έγκριση στατιστικών</button></div>
    {/if}
    {#if workspace.state==='approved'}<p role="status">Τα στατιστικά εγκρίθηκαν εσωτερικά. Δεν έχουν δημοσιευθεί αυτόματα.</p>{/if}
  {/if}
</section>
<style>
  .workspace{display:grid;gap:12px;min-width:0;margin-top:12px;padding:14px;border:1px solid var(--lf-line);border-radius:8px;font-size:13px;color:var(--lf-text);background:var(--lf-surface)}
  header,.actions{display:flex;align-items:center;justify-content:space-between;gap:8px;flex-wrap:wrap}
  h4{font-size:17px;margin:0}h5{font-size:15px;margin:4px 0}p{margin:0;line-height:1.55}.hint,small,header p{font-size:12px;color:var(--lf-muted)}
  button,input,select,textarea{min-height:44px;box-sizing:border-box;font:inherit;border:1px solid var(--lf-line);border-radius:7px;background:var(--lf-surface);color:var(--lf-text);padding:8px}
  button{cursor:pointer;color:var(--lf-gold-light)}button:disabled{opacity:.5;cursor:default}.primary{background:#f6bb38;color:#111;font-weight:700}
  :is(button,input,select,textarea):focus-visible{outline:2px solid #f6bb38;outline-offset:2px}
  label{display:grid;gap:5px;min-width:0}label input,label select,label textarea{width:100%;min-width:0}textarea{resize:vertical}
  .totals{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:5px}.totals span{display:grid;gap:4px;overflow-wrap:anywhere;border:1px solid var(--lf-line);padding:8px;border-radius:6px;color:var(--lf-muted);font-size:11px}.totals strong{color:#f6bb38;font-size:20px}
  .rotation{display:grid;gap:8px}.roster{display:grid;gap:8px;list-style:none;padding:0;margin:0}.roster li{display:grid;gap:6px;padding:10px 0;border-bottom:1px solid var(--lf-line);min-width:0}.player{display:grid;gap:4px;overflow-wrap:anywhere}.sr{font-size:11px;color:var(--lf-muted)}
  .error{color:#fca5a5}.stat-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:5px}fieldset{min-width:0;margin:0;padding:8px;border:1px solid var(--lf-line);border-radius:6px}legend{overflow-wrap:anywhere;max-width:100%}.confirm{display:flex;align-items:center;gap:8px}.confirm input{width:20px;min-height:20px}
  .stats-scroll{overflow-x:auto}table{width:100%;border-collapse:collapse;font-size:12px;text-align:left}td,th{padding:7px 5px;border-bottom:1px solid var(--lf-line)}
</style>
