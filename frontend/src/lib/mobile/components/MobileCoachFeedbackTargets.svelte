<script lang="ts">
  import {onMount} from 'svelte';
  import type {MobileAuthController} from '../controller.ts';
  import type {CoachFeedbackTarget} from '../coach-feedback-composer.ts';
  import './mobile-theme.css';

  let {controller,onOpenFeedback}:{
    controller:MobileAuthController;
    onOpenFeedback:()=>void|Promise<void>;
  }=$props();
  let targets=$state<CoachFeedbackTarget[]>([]);
  let loading=$state(true);
  let error=$state<string|null>(null);
  let alive=false,revision=0;
  onMount(()=>{
    alive=true;void refresh();
    return()=>{alive=false;revision++;targets=[];};
  });
  async function refresh(){
    const request=++revision;
    targets=[];loading=true;error=null;
    try{
      const result=await controller.getCoachFeedbackTargets();
      if(!alive||request!==revision)return;
      targets=result.items;
    }catch{
      if(alive&&request===revision)error='Δεν είναι διαθέσιμος ο επαληθευμένος κατάλογος παικτών.';
    }finally{if(alive&&request===revision)loading=false;}
  }
</script>
<section class="targets" data-testid="coach-home-feedback-targets" aria-labelledby="coach-home-players-heading">
  <div class="heading">
    <div><p class="eyebrow">ΠΡΟΠΟΝΗΤΗΣ · ΙΔΙΩΤΙΚΗ ΚΑΘΟΔΗΓΗΣΗ</p>
      <h2 id="coach-home-players-heading">Παίκτες για προσωπικό feedback</h2>
    </div>
    <button type="button" class="refresh" disabled={loading} onclick={()=>void refresh()}>Ανανέωση</button>
  </div>
  <p class="hint">Εμφανίζονται μόνο παίκτες με τρέχουσα επαληθευμένη ανάθεση και δυνατότητα ασφαλούς ιδιωτικής αποστολής. Δεν αποτελεί ολόκληρο το ρόστερ.</p>
  {#if loading}
    <p class="state" role="status">Επαλήθευση αναθέσεων…</p>
  {:else if error}
    <p class="state error" role="alert">{error}</p>
  {:else if targets.length===0}
    <p class="state">Δεν υπάρχουν αυτή τη στιγμή επιτρεπόμενοι παραλήπτες.</p>
  {:else}
    <p class="state" role="status" aria-live="polite">{targets.length} επαληθευμένοι παραλήπτες</p>
    <ul>
      {#each targets.slice(0,8) as target (target.organization_id+':'+target.id)}
        <li><span class="avatar" aria-hidden="true">{target.name.charAt(0).toLocaleUpperCase('el-GR')}</span>
          <span><strong>{target.name}</strong><small>{target.organization_id==='lefteria_fc_academy'?'Ακαδημία':'Πρώτη ομάδα'}</small></span></li>
      {/each}
    </ul>
    {#if targets.length>8}<p class="state">Άλλοι {targets.length-8} παραλήπτες διατίθενται στη σύνθεση μηνύματος.</p>{/if}
    <button type="button" class="open" onclick={()=>void onOpenFeedback()}>
      Προσωπικό feedback με έλεγχο & υπογραφή ›
    </button>
  {/if}
</section>
<style>
  .targets{padding:18px 15px;margin:0 14px 22px;border:1px solid #4e3e20;border-radius:13px;background:linear-gradient(135deg,#221d14,#121416);color:#f5f5f5;font-family:var(--lf-body)}
  .heading{display:flex;align-items:start;justify-content:space-between;gap:10px}
  .eyebrow{font-size:10px;color:#ffca46;letter-spacing:.1em;font-weight:700}
  h2{font-size:22px;font-family:var(--lf-heading);line-height:1.15;margin:6px 0}
  .hint,.state{font-size:12px;line-height:1.65;color:var(--lf-muted)}
  .error{color:#ffb5af}
  ul{list-style:none;margin:14px 0;padding:0;display:grid;gap:8px}
  li{display:flex;gap:10px;align-items:center;border:1px solid #35332e;border-radius:9px;padding:9px}
  .avatar{width:38px;height:38px;display:grid;place-items:center;flex:0 0 38px;border-radius:10px;background:#403012;color:#ffca46;font-weight:700}
  li span:last-child{display:grid;gap:2px}
  li strong{font-size:13px;overflow-wrap:anywhere}
  li small{font-size:11px;color:var(--lf-muted)}
  button{font:inherit;min-height:44px;border-radius:8px;color:#ffca46;cursor:pointer}
  .refresh{background:transparent;border:1px solid #60502f;padding:8px}
  .open{width:100%;margin-top:8px;background:#3b2f18;border:1px solid #a87d28;padding:12px}
  button:focus-visible{outline:2px solid #ffca46;outline-offset:3px}
  button:disabled{opacity:.5;cursor:wait}
</style>
