<script lang="ts">
  import { browser } from '$app/env';
  import { ONBOARDING_VERSION, type WalkthroughStep } from './guides.ts';

  let {steps,scope,title='Οδηγός πρώτης χρήσης',onNavigate}:{
    steps:WalkthroughStep[];
    scope:string;
    title?:string;
    onNavigate?: (destination:string)=>void|Promise<void>;
  }=$props();

  let active=$state(0);
  let visited=$state<string[]>([]);
  let open=$state(true);
  const current=$derived(steps[Math.min(active,Math.max(steps.length-1,0))]);
  const done=$derived(steps.filter(s=>s.state==='verified' ||
    (s.id!=='profile' && s.state==='ready' && visited.includes(s.id))).length);
  const available=$derived(steps.filter(s=>s.state!=='planned').length);
  const complete=$derived(available>0 && done===available);
  const progress=$derived(available?Math.round(done/available*100):0);

  const key=(value:string)=>'lefteria-onboarding:'+ONBOARDING_VERSION+':'+value;
  $effect(()=>{
    const identity=scope;
    active=0;
    open=true;
    if(!browser)return;
    let raw:string|null=null;
    try{raw=localStorage.getItem(key(identity));}catch{}
    let previous:string[]=[];
    try{
      const parsed:unknown=raw?JSON.parse(raw):[];
      if(Array.isArray(parsed))
        previous=parsed.filter((item:unknown):item is string=>
          typeof item==='string' && item.length<=40);
    }catch{}
    visited=previous.filter(id=>steps.some(s=>s.id===id && s.state==='ready' && id!=='profile'));
  });
  function remember(id:string){
    const step=steps.find(s=>s.id===id);
    if(!step || step.state!=='ready' || id==='profile' || visited.includes(id))return;
    visited=[...visited,id];
    if(!browser)return;
    try{localStorage.setItem(key(scope),JSON.stringify(visited));}catch{}
  }
  async function navigate(url:string){
    if(!current || !current.destination)return;
    if(current.id!=='profile')remember(current.id);
    if(onNavigate)await onNavigate(url);
    else if(browser)window.location.assign(url);
  }
</script>

<section class="setup" aria-label={title} data-testid="role-onboarding" data-onboarding-version={ONBOARDING_VERSION}>
  <div class="setup-head">
    <div><p class="setup-eyebrow">ΛΕΥΤΕΡΙΑ FC · ΠΡΩΤΑ ΒΗΜΑΤΑ</p>
      <h2>{title}</h2><p class="setup-desc">Βήμα προς βήμα, σύμφωνα με τον ρόλο και τα πραγματικά διαθέσιμα εργαλεία σου.</p></div>
    <button type="button" class="setup-toggle" aria-expanded={open}
      onclick={()=>open=!open}>{open?'Σύμπτυξη':'Προβολή οδηγού'}</button>
  </div>
  {#if open && current}
    <div class="setup-progress">
      <span>{done} από {available} διαθέσιμα βήματα</span>
      <span>{progress}%</span>
    </div>
    <div class="setup-bar" role="progressbar" aria-label="Πρόοδος οδηγού" aria-valuenow={progress}
      aria-valuemin="0" aria-valuemax="100"><span style:width={progress+'%'}></span></div>
    <ol class="setup-steps" aria-label="Βήματα οδηγού">
      {#each steps as entry,i (entry.id)}
        <li>
          <button type="button" class:current={active===i} aria-current={active===i?'step':undefined}
            onclick={()=>active=i}>
            <span class="number">{i+1}</span>
            <span>{entry.title}</span>
            {#if entry.state==='verified'}<span class="state">✓ Επαληθευμένο</span>
            {:else if entry.state==='planned'}<span class="state muted">Σε ανάπτυξη</span>
            {:else if visited.includes(entry.id)}<span class="state">✓ Προβλήθηκε</span>{/if}
          </button>
        </li>
      {/each}
    </ol>
    <div class="setup-current" data-testid="onboarding-current">
      <p class="setup-count">Βήμα {active+1} / {steps.length}</p>
      <h3>{current.title}</h3>
      <p>{current.description}</p>
      {#if current.state==='planned'}
        <p class="setup-planned">Under Developments — δεν υπάρχει ακόμη διαθέσιμη ενέργεια.</p>
      {:else if current.state==='verified'}
        <p class="setup-verified" role="status">Το βήμα επιβεβαιώθηκε από τα πραγματικά στοιχεία του λογαριασμού.</p>
      {/if}
      <div class="setup-actions">
        <button type="button" class="setup-secondary" disabled={active===0}
          onclick={()=>active--}>Πίσω</button>
        {#if current.destination}
          <button type="button" class="setup-primary" onclick={()=>void navigate(current.destination!)}>
            {current.destinationLabel??'Άνοιγμα'}
          </button>
        {/if}
        {#if current.state==='ready' && current.id!=='profile' && !visited.includes(current.id)}
          <button type="button" class="setup-secondary" onclick={()=>remember(current.id)}>Το είδα</button>
        {/if}
        <button type="button" class="setup-secondary" disabled={active>=steps.length-1}
          onclick={()=>active++}>Επόμενο</button>
      </div>
      {#if complete}<p class="setup-verified" role="status">Ο οδηγός διαθέσιμων βημάτων ολοκληρώθηκε. Οι μελλοντικές δυνατότητες θα εμφανιστούν όταν ενεργοποιηθούν.</p>{/if}
    </div>
  {/if}
</section>
<style>
  .setup{border:1px solid #5b4823;border-radius:12px;background:linear-gradient(130deg,#252015,#141719 55%,#111213);padding:18px;color:#f7f7f7;font-family:'Fira Sans',sans-serif;font-size:13px;margin:0 0 16px}
  .setup-head{display:flex;align-items:start;justify-content:space-between;gap:14px;flex-wrap:wrap}
  .setup-eyebrow{color:#ffca46;font-size:10px;letter-spacing:.12em;margin:0 0 6px;font-weight:700}
  .setup h2{font-family:'Fira Sans Extra Condensed',sans-serif;font-size:21px;margin:0 0 5px}
  .setup-desc{color:#b9bcbe;margin:0;max-width:65ch}
  .setup button{font:inherit;cursor:pointer;min-height:42px;border-radius:8px;padding:8px 12px}
  .setup button:disabled{opacity:.5;cursor:default}
  .setup button:focus-visible{outline:2px solid #ffca46;outline-offset:2px}
  .setup-toggle,.setup-secondary{border:1px solid #65502d;background:#171819;color:#f9d69d}
  .setup-progress{display:flex;justify-content:space-between;gap:8px;margin-top:16px;color:#e6d2ae}
  .setup-bar{height:5px;margin:7px 0 17px;border-radius:99px;background:#4b4540;overflow:hidden}
  .setup-bar span{display:block;height:100%;background:#f5a623;transition:width .2s}
  .setup-steps{display:grid;gap:5px;list-style:none;padding:0;margin:0 0 12px}
  .setup-steps button{display:flex;align-items:center;gap:9px;width:100%;border:1px solid transparent;background:transparent;color:#d0d1d3;text-align:left}
  .setup-steps button.current{background:#302714;border-color:#806027;color:#fff}
  .number{display:grid;place-items:center;flex:0 0 26px;height:26px;border:1px solid #88682e;border-radius:50%;color:#ffca46;font-size:12px}
  .state{margin-left:auto;color:#ffd079;font-size:10px;white-space:nowrap}
  .state.muted{color:#aeb1b4}
  .setup-current{border-top:1px solid #393b3a;padding-top:14px}
  .setup-count{font-size:10px;color:#ffca46}
  .setup-current h3{font-family:'Fira Sans Extra Condensed',sans-serif;font-size:18px;margin:4px 0 8px}
  .setup-current p{line-height:1.6;color:#d6d6d6}
  .setup-current .setup-planned{color:#c1aa83}
  .setup-current .setup-verified{color:#ffca46}
  .setup-actions{display:flex;flex-wrap:wrap;gap:7px;margin-top:15px}
  .setup-primary{background:#f5a623;color:#101010;border:1px solid #f5a623;font-weight:700}
  @media(min-width:700px){.setup-steps{grid-template-columns:repeat(2,minmax(0,1fr))}}
  @media(max-width:380px){.setup-steps button{flex-wrap:wrap}.state{margin-left:34px}}
  @media(prefers-reduced-motion:reduce){.setup-bar span{transition:none}}
</style>
