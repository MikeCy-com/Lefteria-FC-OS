<script lang="ts">
  import {browser} from '$app/env';
  import {playerClassroomPositionLabels} from '../mobile/player-classrooms.ts';
  import type {QuizOrg} from './player-quiz-editorial.ts';
  import type {PlayerClassroomPosition} from '../mobile/player-classrooms.ts';

  type RosterPosition='Goalkeeper'|'Defender'|'Midfielder'|'Forward';
  type Player={id:string;name:string};
  type Roster={organization_id:QuizOrg;ready:true;offset:number;limit:number;total:number;players:Player[]};
  type Assignment={organization_id:QuizOrg;player_id:string;roster_position:RosterPosition;
    specialization:Exclude<PlayerClassroomPosition,'all'>|null;
    effective_position:PlayerClassroomPosition;needs_reverification:boolean;version:number};
  const specialties:Record<RosterPosition,PlayerClassroomPosition[]>={
    Goalkeeper:['all','goalkeeper'],Defender:['all','defender','fullback'],
    Midfielder:['all','midfielder','winger'],Forward:['all','winger','striker']
  };
  const rosterPositions=new Set<string>(Object.keys(specialties));
  const learningPositions=new Set<string>(Object.keys(playerClassroomPositionLabels));
  const isObject=(value:unknown):value is Record<string,unknown>=>!!value&&
    typeof value==='object'&&!Array.isArray(value);
  const validRoster=(value:unknown,owner:QuizOrg,at:number):value is Roster=>{
    if(!isObject(value)||value.organization_id!==owner||value.ready!==true||
      value.offset!==at||value.limit!==20||
      !Number.isSafeInteger(value.total)||(value.total as number)<0||
      (value.total as number)>25000||!Array.isArray(value.players)||
      value.players.length>20)return false;
    return value.players.every((player:unknown)=>isObject(player)&&
      typeof player.id==='string'&&player.id.length>0&&player.id.length<=128&&
      typeof player.name==='string'&&player.name.length>0&&player.name.length<=200);
  };
  const validAssignment=(value:unknown,owner:QuizOrg,id:string):value is Assignment=>
    isObject(value)&&value.organization_id===owner&&value.player_id===id&&
    rosterPositions.has(String(value.roster_position))&&
    (value.specialization===null||learningPositions.has(String(value.specialization)))&&
    learningPositions.has(String(value.effective_position))&&
    typeof value.needs_reverification==='boolean'&&
    Number.isSafeInteger(value.version)&&(value.version as number)>=0&&
    (value.version as number)<Number.MAX_SAFE_INTEGER&&
    (value.specialization===null||
      specialties[value.roster_position as RosterPosition].includes(
        value.specialization as PlayerClassroomPosition));
  const url=(player:string)=>'/api/admin/hub/players/'+encodeURIComponent(player)+
    '/learning-position?'+new URLSearchParams({organization_id:organization}).toString();

  let {organization,actorId}:{organization:QuizOrg;actorId:string}=$props();
  let offset=$state(0),page=$state<Roster|null>(null);
  let selected=$state<string|null>(null),assignment=$state<Assignment|null>(null);
  let next=$state<PlayerClassroomPosition>('all'),reason=$state('');
  let busy=$state(false),loading=$state(true);
  let error=$state<string|null>(null),success=$state<string|null>(null);
  let revision=0;

  $effect(()=>{
    const owner=organization,actor=actorId,at=offset;
    if(!browser||!owner||!actor)return;
    const current=++revision,abort=new AbortController();
    page=null;selected=null;assignment=null;error=null;success=null;loading=true;
    void(async()=>{
      try{
        const response=await fetch('/api/admin/football/players?'+
          new URLSearchParams({organization_id:owner,offset:String(at),limit:'20'}),{
          credentials:'same-origin',cache:'no-store',signal:abort.signal});
        if(!response.ok)throw new Error();
        const data:unknown=await response.json();
        if(!validRoster(data,owner,at))throw new Error();
        if(current===revision&&!abort.signal.aborted)page=data;
      }catch{
        if(current===revision&&!abort.signal.aborted)
          error='Δεν φορτώθηκε ο επαληθευμένος κατάλογος αθλητών.';
      }finally{if(current===revision&&!abort.signal.aborted)loading=false;}
    })();
    return()=>{revision++;abort.abort();};
  });

  async function pick(player:string){
    if(busy||loading||!page?.players.some(p=>p.id===player))return;
    const current=++revision,owner=organization,actor=actorId;
    selected=player;assignment=null;reason='';error=null;success=null;loading=true;
    try{
      const response=await fetch(url(player),{credentials:'same-origin',cache:'no-store'});
      if(!response.ok)throw new Error();
      const data:unknown=await response.json();
      if(!validAssignment(data,owner,player))throw new Error();
      if(current===revision&&organization===owner&&actorId===actor){
        assignment=data;next=data.specialization??'all';
      }
    }catch{if(current===revision)error='Δεν επαληθεύτηκε η εκπαιδευτική θέση.';}
    finally{if(current===revision)loading=false;}
  }

  async function save(){
    const details=assignment,owner=organization,actor=actorId,player=selected;
    if(!details||!player||busy||loading||owner!==details.organization_id||
       !specialties[details.roster_position].includes(next)||
       ((next==='all'?null:next)===details.specialization&&!details.needs_reverification)||
       reason.trim()!==reason||reason.length<8||reason.length>250||
       /[\x00-\x1f\x7f]/.test(reason))return;
    if(!confirm('Να καταχωριστεί η επαληθευμένη εκπαιδευτική θέση για αυτόν τον αθλητή;'))return;
    const current=++revision;
    busy=true;error=null;success=null;
    try{
      const response=await fetch(url(player),{
        method:'PUT',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({expected_roster_position:details.roster_position,
          expected_version:details.version,specialization:next,reason})
      });
      if(response.status===409){
        assignment=null;
        throw new Error('Άλλαξε το ρόστερ ή η θέση. Ανάκτησε ξανά τον αθλητή πριν αποθηκεύσεις.');
      }
      if(!response.ok)throw new Error('Η έγκριση δεν αποθηκεύτηκε.');
      const data:unknown=await response.json();
      if(!validAssignment(data,owner,player)||data.version<details.version)
        throw new Error('Η επιβεβαίωση της αποθήκευσης δεν επαληθεύτηκε.');
      if(current===revision&&organization===owner&&actorId===actor&&selected===player){
        assignment=data;next=data.specialization??'all';reason='';
        success='Η εξειδίκευση αποθηκεύτηκε με έλεγχο και ιστορικό. Δεν δημοσιεύθηκε νέο κουίζ.';
      }
    }catch(err){
      if(current===revision&&organization===owner&&actorId===actor)
        error=err instanceof Error?err.message:'Η έγκριση δεν ολοκληρώθηκε.';
    }finally{if(current===revision)busy=false;}
  }
</script>

<section class="learning-positions" data-testid="hub-verified-player-specialization" aria-labelledby="learning-positions-title">
  <h2 id="learning-positions-title">Επαλήθευση εκπαιδευτικής θέσης αθλητή</h2>
  <p class="description">Μόνο Super Admin. Το ρόστερ διατηρεί την αρχική γενική θέση.
    Εδώ μπορείς να επαληθεύσεις ειδικότερο ρόλο για τα ήδη εγκεκριμένα
    εβδομαδιαία κουίζ. Δεν δημιουργεί ούτε δημοσιεύει υλικό.</p>
  <div class="toolbar">
    <button type="button" disabled={loading||busy||offset===0}
      onclick={()=>offset=Math.max(0,offset-20)}>Προηγούμενοι αθλητές</button>
    <span>Αθλητές {page?offset+1:0}–{page?offset+page.players.length:0}
      {#if page}από {page.total}{/if}</span>
    <button type="button" disabled={loading||busy||!page||offset+20>=page.total}
      onclick={()=>offset+=20}>Επόμενοι αθλητές</button>
  </div>
  {#if page?.players.length}
    <label for="hub-position-player">Αθλητής από επαληθευμένο ρόστερ</label>
    <select id="hub-position-player" value={selected??''}
      disabled={loading||busy} onchange={e=>void pick(e.currentTarget.value)}>
      <option value="">Επίλεξε αθλητή</option>
      {#each page.players as player (player.id)}
        <option value={player.id}>{player.name}</option>
      {/each}
    </select>
  {:else if !loading}
    <p class="description">Δεν υπάρχουν αθλητές σε αυτή τη σελίδα του ρόστερ.</p>
  {/if}
  {#if loading}<p role="status">Επαλήθευση στοιχείων…</p>{/if}
  {#if assignment}
    <div class="verified">
      <p>Κανονική θέση ρόστερ: <strong>{assignment.roster_position}</strong></p>
      <p>Τρέχουσα εκπαίδευση:
        <strong>{playerClassroomPositionLabels[assignment.effective_position]}</strong></p>
      {#if assignment.needs_reverification}
        <p role="alert">Η βασική θέση άλλαξε. Η προηγούμενη εξειδίκευση δεν εφαρμόζεται
          πλέον μέχρι να εγκριθεί ξανά.</p>
      {/if}
      <label for="hub-position-specialty">Εγκεκριμένη εξειδίκευση</label>
      <select id="hub-position-specialty" bind:value={next} disabled={busy}>
        {#each specialties[assignment.roster_position] as specialty}
          <option value={specialty}>
            {specialty==='all'?'Χωρίς ειδική ανάθεση (βασική θέση)':playerClassroomPositionLabels[specialty]}
          </option>
        {/each}
      </select>
      <label for="hub-position-reason">Αιτιολογία επαλήθευσης (8–250 χαρακτήρες)</label>
      <textarea id="hub-position-reason" rows="2" maxlength="250" bind:value={reason}
        placeholder="Π.χ. επιβεβαιώθηκε από το τεχνικό επιτελείο στην τελευταία αξιολόγηση"
        disabled={busy}></textarea>
      <button type="button" class="save" disabled={busy||loading||
        ((next==='all'?null:next)===assignment.specialization&&!assignment.needs_reverification)||
        reason.length<8||reason.length>250||reason.trim()!==reason}
        onclick={()=>void save()}>
        Αποθήκευση επαληθευμένης θέσης
      </button>
    </div>
  {/if}
  {#if error}<p class="error" role="alert">{error}</p>{/if}
  {#if success}<p class="success" role="status">{success}</p>{/if}
  <p class="description small">Οι αλλαγές θέσης κρατούν ιστορικό και ελέγχονται
    ανά οργανισμό. Δεν επαναφέρουν παλιά κουίζ ούτε παρακάμπτουν την προσωπική Εβδομάδα 1.</p>
</section>
<style>
  .learning-positions{padding:22px;border:1px solid var(--lf-line);border-radius:14px;
    background:var(--lf-surface);color:var(--lf-text);margin:20px 0}
  h2{font:700 24px var(--lf-heading);margin:0 0 10px}
  label{display:block;font-size:14px;font-weight:700;margin:12px 0 7px;color:var(--lf-text)}
  select,textarea{display:block;width:100%;max-width:640px;min-height:44px;padding:10px;
    border-radius:8px;background:var(--lf-elevated);color:var(--lf-text);
    border:1px solid var(--lf-line);font:inherit}
  select:focus-visible,textarea:focus-visible,button:focus-visible{outline:2px solid var(--lf-gold);outline-offset:2px}
  .toolbar{display:flex;gap:12px;flex-wrap:wrap;align-items:center;margin:15px 0}
  button{min-height:44px;padding:10px 14px;border-radius:9px;border:1px solid var(--lf-brand-edge);
    color:var(--lf-gold-light);background:var(--lf-elevated);cursor:pointer}
  button:disabled{opacity:.5;cursor:default}
  .verified{border-top:1px solid var(--lf-line);padding-top:13px;margin-top:17px}
  .verified p{margin:7px 0;font-size:14px}
  .description{color:var(--lf-muted);font-size:14px;line-height:1.65}
  .small{font-size:12px}
  .save{margin-top:14px;font-weight:800;border-color:var(--lf-gold)}
  .error{color:#efac9e}.success{color:var(--lf-gold-light)}
</style>
