<script lang="ts">
  import {onMount} from 'svelte';
  import type {MobileAuthController} from '../controller.ts';
  import type {PlayerCoachFeedbackItem} from '../coach-feedback.ts';
  import './mobile-theme.css';

  let {controller}:{controller:MobileAuthController}=$props();
  let items=$state<PlayerCoachFeedbackItem[]>([]);
  let nextOffset=$state<number|null>(null);
  let busy=$state(true);
  let error=$state<string|null>(null);
  let alive=false,request=0;

  onMount(()=>{
    alive=true;
    void refresh();
    return()=>{alive=false;request++;items=[];};
  });

  async function refresh(){
    const current=++request;
    busy=true;error=null;items=[];nextOffset=null;
    try{
      const page=await controller.getPlayerCoachFeedback(0);
      if(!alive||current!==request)return;
      items=page.items;
      nextOffset=page.items.length===20 ? 20 : null;
    }catch{
      if(alive&&current===request)error='Δεν ήταν δυνατή η επαλήθευση των προσωπικών μηνυμάτων του προπονητή.';
    }finally{if(alive&&current===request)busy=false;}
  }

  async function loadMore(){
    if(nextOffset===null||busy)return;
    const current=++request,offset=nextOffset;
    busy=true;error=null;
    try{
      const page=await controller.getPlayerCoachFeedback(offset);
      if(!alive||current!==request)return;
      const previous=new Set(items.map(item=>item.id));
      items=[...items,...page.items.filter(item=>!previous.has(item.id))];
      nextOffset=page.items.length===20&&offset<9980?offset+20:null;
    }catch{
      if(alive&&current===request)error='Δεν φορτώθηκαν τα επόμενα προσωπικά μηνύματα.';
    }finally{if(alive&&current===request)busy=false;}
  }
</script>

<section class="feedback" aria-labelledby="player-feedback-title"
  data-testid="mobile-player-coach-feedback">
  <header class="heading">
    <div>
      <p class="eyebrow">ΛΕΥΤΕΡΙΑ FC · ΠΡΟΣΩΠΙΚΑ</p>
      <h2 id="player-feedback-title">Μηνύματα από τους προπονητές μου</h2>
    </div>
    <button type="button" class="action" disabled={busy}
      onclick={()=>void refresh()}>Ανανέωση</button>
  </header>
  <p class="privacy">Εδώ εμφανίζονται μόνο μηνύματα που έχουν εγκριθεί και αποσταλεί
    προσωπικά σε εσένα από τον προπονητή σου. Δεν είναι ομαδικές ανακοινώσεις.</p>
  {#if busy&&items.length===0}
    <p class="state" role="status">Φόρτωση προσωπικών μηνυμάτων…</p>
  {:else if !busy&&items.length===0&&!error}
    <p class="state" role="status">Δεν έχεις ακόμα προσωπικά μηνύματα από τον προπονητή σου.</p>
  {/if}
  {#if items.length}
    <ol class="messages">
      {#each items as item (item.id)}
        <li class="message" data-testid="coach-feedback-message">
          <div class="meta">
            <strong>{item.coach_name}</strong>
            <time datetime={item.sent_at}>
              {new Date(item.sent_at).toLocaleDateString('el-CY',{day:'numeric',month:'short',year:'numeric'})}
            </time>
          </div>
          <p class="words">{item.message}</p>
        </li>
      {/each}
    </ol>
  {/if}
  {#if nextOffset!==null}
    <button type="button" class="action more" disabled={busy}
      onclick={()=>void loadMore()}>Περισσότερα μηνύματα</button>
  {/if}
  {#if error}
    <p class="failure" role="alert">{error}</p>
  {/if}
</section>

<style>
  .feedback{padding:18px 16px 25px;margin:0 0 20px;background:var(--lf-surface);
    border:1px solid var(--lf-line);border-radius:14px;color:var(--lf-text);
    font-family:var(--lf-body);font-size:var(--lf-type-body)}
  .heading{display:flex;justify-content:space-between;align-items:start;gap:12px}
  .eyebrow{font-size:10px;font-weight:700;letter-spacing:.13em;color:var(--lf-gold);
    margin:0 0 5px}
  h2{font-family:var(--lf-heading);font-size:clamp(21px,5vw,28px);
    line-height:1.12;margin:0}
  .privacy,.state{color:var(--lf-muted);font-size:12px;line-height:1.65}
  .privacy{margin:13px 0 18px}
  .messages{list-style:none;margin:0;padding:0;display:grid;gap:11px}
  .message{padding:16px;border:1px solid var(--lf-brand-edge);border-radius:10px;
    background:var(--lf-elevated);overflow-wrap:anywhere}
  .meta{display:flex;align-items:baseline;justify-content:space-between;gap:12px;flex-wrap:wrap}
  .meta strong{color:var(--lf-gold-light);font-size:15px}
  .meta time{font-size:11px;color:var(--lf-muted)}
  .words{white-space:pre-wrap;line-height:1.65;font-size:14px;margin:12px 0 0}
  .action{padding:9px 12px;border:1px solid var(--lf-brand-edge);
    border-radius:9px;color:var(--lf-gold-light);background:transparent;
    min-height:44px;font:inherit;cursor:pointer}
  .action:disabled{opacity:.5;cursor:default}
  .action:focus-visible{outline:2px solid var(--lf-gold);outline-offset:2px}
  .more{width:100%;margin-top:13px}
  .failure{padding:12px;color:#ffcdbe;border:1px solid #7b3e35;
    border-radius:9px;background:#2a1b1a;font-size:12px}
</style>
