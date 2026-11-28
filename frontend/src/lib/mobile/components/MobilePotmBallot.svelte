<script lang="ts">
  import {onMount} from 'svelte';
  import type {MobileAuthController} from '../controller.ts';
  import type {PotmBallot,PotmBallotCandidate} from '../potm-ballot.ts';
  import {potmMonthLabel} from '../../public/potm.ts';
  import PublicImage from '../../public/PublicImage.svelte';
  import './mobile-theme.css';

  let {controller}:{controller:MobileAuthController}=$props();
  let ballot=$state<PotmBallot|null>(null);
  let candidates=$state<PotmBallotCandidate[]>([]);
  let draft=$state<string|null>(null);
  let confirmation=$state<'save'|'withdraw'|null>(null);
  let busy=$state(true),error=$state<string|null>(null),success=$state<string|null>(null);
  let alive=false,epoch=0;
  const id=$props.id();
  const currentName=$derived(candidates.find(p=>p.id===ballot?.my_vote_player_id)?.name??null);
  const selected=$derived(candidates.find(p=>p.id===draft)??null);
  const canSave=$derived(!!ballot?.voting_open&&!!selected&&draft!==ballot.my_vote_player_id&&!busy&&!confirmation);
  const canWithdraw=$derived(!!ballot?.voting_open&&ballot.my_vote_player_id!==null&&!busy&&!confirmation);

  onMount(()=>{
    alive=true;void refresh();
    return()=>{alive=false;epoch++;ballot=null;candidates=[];draft=null;confirmation=null;};
  });
  async function refresh(){
    const op=++epoch;
    busy=true;error=null;success=null;ballot=null;candidates=[];draft=null;confirmation=null;
    try{
      const data=await controller.getPotmBallot();
      if(!alive||epoch!==op)return;
      const options=data.voting_open?await controller.getPotmCandidates(data.month_key):[];
      if(!alive||epoch!==op)return;
      ballot=data;candidates=options;draft=data.my_vote_player_id;
    }catch{
      if(alive&&epoch===op)error='Δεν επαληθεύτηκε η ψηφοφορία. Ανανέωσε για να προσπαθήσεις ξανά.';
    }finally{if(alive&&epoch===op)busy=false;}
  }
  async function submit(){
    if(!ballot?.voting_open||busy||!confirmation)return;
    const previous=ballot,choice=confirmation==='withdraw'?null:selected?.id;
    if(choice===undefined||choice===previous.my_vote_player_id)return;
    const op=++epoch;
    busy=true;confirmation=null;error=null;success=null;
    try{
      const updated=await controller.savePotmBallot(previous,choice);
      if(!alive||epoch!==op)return;
      ballot=updated;draft=updated.my_vote_player_id;
      success=choice===null?'Η ψήφος σου αποσύρθηκε.':'Η ψήφος σου αποθηκεύτηκε.';
    }catch{
      if(!alive||epoch!==op)return;
      // The response may have been lost after a committed write. Do not retry
      // automatically or keep a stale revision available for a second submit.
      ballot=null;candidates=[];draft=null;
      error='Δεν επιβεβαιώθηκε η αλλαγή. Ανανέωσε την κατάσταση της ψήφου σου πριν ξαναπροσπαθήσεις.';
    }finally{if(alive&&epoch===op)busy=false;}
  }
</script>

<section class="ballot-panel" data-testid="mobile-potm-ballot" aria-labelledby={id+'-title'}>
  <header>
    <p class="eyebrow">ΛΕΥΤΕΡΙΑ FC · ΠΡΩΤΗ ΟΜΑΔΑ</p>
    <h1 id={id+'-title'}>Παίκτης του μήνα</h1>
    <p>Μία ψήφος ανά επαληθευμένο λογαριασμό φιλάθλου και μήνα, με δυνατότητα αλλαγής ή απόσυρσης όσο η ψηφοφορία είναι ανοικτή.</p>
    {#if ballot}<p class="month" data-testid="mobile-potm-month">{potmMonthLabel(ballot.month_key)}</p>{/if}
  </header>
  <div class="toolbar">
    <a href="/vote" data-testid="mobile-potm-results">Δημοσιευμένα αποτελέσματα ↗</a>
    <button type="button" class="secondary" data-testid="mobile-potm-refresh" disabled={busy} onclick={()=>void refresh()}>Ανανέωση</button>
  </div>
  {#if busy}<p role="status" data-testid="mobile-potm-loading">Γίνεται ασφαλής επαλήθευση…</p>{/if}
  {#if error}<p class="error" role="alert" data-testid="mobile-potm-error">{error}</p>{/if}
  {#if success}<p class="success" role="status" data-testid="mobile-potm-success">{success}</p>{/if}
  {#if ballot}
    <div class="current" data-testid="mobile-potm-current">
      <strong>Η ψήφος μου</strong>
      <p>{ballot.my_vote_player_id===null?'Δεν έχεις ενεργή ψήφο.':currentName??'Έχεις καταχωρημένη ψήφο.'}</p>
    </div>
    {#if !ballot.voting_open}
      <p class="notice" role="status" data-testid="mobile-potm-closed">Η ψηφοφορία δεν είναι ανοικτή. Μπορείς να δεις τα δημοσιευμένα αποτελέσματα.</p>
    {:else}
      <fieldset disabled={busy||confirmation!==null} class="choices">
        <legend>Επίλεξε ποδοσφαιριστή</legend>
        {#each candidates as player (player.id)}
          <label class="candidate" class:chosen={draft===player.id}>
            <input type="radio" name={id+'-candidate'} value={player.id} checked={draft===player.id}
              onchange={()=>{draft=player.id;success=null;}} data-testid={'mobile-potm-candidate-'+player.id}/>
            <span class="portrait"><PublicImage src={player.imageUrl} alt=""/></span>
            <span class="details"><strong>{player.name}</strong><small>#{player.number}{player.position?' · '+player.position:''}</small></span>
          </label>
        {:else}
          <p data-testid="mobile-potm-no-candidates">Δεν υπάρχουν διαθέσιμοι επιλέξιμοι ποδοσφαιριστές.</p>
        {/each}
      </fieldset>
      <div class="toolbar">
        <button type="button" class="primary" data-testid="mobile-potm-save" disabled={!canSave}
          onclick={()=>confirmation='save'}>{ballot.my_vote_player_id===null?'Υποβολή ψήφου':'Αλλαγή ψήφου'}</button>
        {#if ballot.my_vote_player_id!==null}
          <button type="button" class="secondary" data-testid="mobile-potm-withdraw" disabled={!canWithdraw}
            onclick={()=>confirmation='withdraw'}>Απόσυρση ψήφου</button>
        {/if}
      </div>
      {#if confirmation}
        <section class="confirm" aria-labelledby={id+'-confirm'} data-testid="mobile-potm-confirmation">
          <h2 id={id+'-confirm'}>Επιβεβαίωση</h2>
          <p>{confirmation==='withdraw'?'Θέλεις να αποσύρεις την ψήφο σου;':'Επιβεβαιώνεις την ψήφο σου για '+(selected?.name??'')+';'}</p>
          <div class="toolbar">
            <button type="button" class="primary" data-testid="mobile-potm-confirm" onclick={()=>void submit()}>Επιβεβαίωση</button>
            <button type="button" class="secondary" onclick={()=>confirmation=null}>Ακύρωση</button>
          </div>
        </section>
      {/if}
    {/if}
  {/if}
</section>

<style>
  .ballot-panel{min-width:0;display:grid;gap:14px;padding:18px;border:1px solid var(--lf-line);border-radius:10px;background:var(--lf-surface);color:var(--lf-text)}
  h1{font-size:var(--lf-type-display);line-height:1.15;margin:6px 0}
  h2{margin:0 0 8px}p{margin:0;overflow-wrap:anywhere;line-height:1.5}
  .eyebrow,.month{color:var(--lf-gold);font-weight:700;letter-spacing:.04em}
  .month{margin-top:8px;text-transform:capitalize}
  .toolbar{display:flex;flex-wrap:wrap;gap:8px;align-items:center}
  .toolbar a,.toolbar button{min-height:44px;padding:10px 14px;border:1px solid var(--lf-line);border-radius:6px;text-decoration:none;box-sizing:border-box;font:inherit;font-weight:600;overflow-wrap:anywhere}
  .toolbar a,.secondary{color:var(--lf-text);background:var(--lf-elevated)}
  .primary{color:var(--lf-bg);background:var(--lf-gold);border-color:var(--lf-gold)!important}
  .current,.notice,.confirm{padding:12px;border:1px solid var(--lf-line);border-radius:8px;background:var(--lf-elevated)}
  .notice,.confirm{border-color:var(--lf-gold)}
  .confirm .toolbar{margin-top:12px}
  .choices{margin:0;padding:0;border:0;min-width:0;display:grid;gap:8px}
  legend{font-weight:700;margin-bottom:8px}
  .candidate{display:grid;grid-template-columns:20px 46px minmax(0,1fr);align-items:center;gap:10px;padding:10px;border:1px solid var(--lf-line);border-radius:8px;min-width:0;background:var(--lf-elevated);cursor:pointer}
  .candidate.chosen{border-color:var(--lf-gold)}
  input[type=radio]{accent-color:var(--lf-gold);width:18px;height:18px}
  .portrait{width:46px;height:46px;overflow:hidden;border-radius:6px}
  .portrait :global(img){width:100%;height:100%;object-fit:cover}
  .details{min-width:0;display:grid;gap:3px;overflow-wrap:anywhere}
  .details small{color:var(--lf-muted)}
  .success{color:var(--lf-gold);font-weight:600}
  .error{border:1px solid var(--lf-gold);padding:10px;border-radius:6px}
</style>
