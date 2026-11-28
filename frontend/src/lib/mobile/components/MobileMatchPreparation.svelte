<script lang="ts">
  import type {MobileCalendarResponses} from '../calendar.ts';
  import {matchAvailability,matchRosterSummary,filterMatchRoster,type MatchRosterFilter} from '../match-preparation.ts';
  let {responses,loadingMore=false,busy=false,onRefresh,onLoadMore}:{
    responses:MobileCalendarResponses;loadingMore?:boolean;busy?:boolean;
    onRefresh:()=>boolean|void|Promise<unknown>;onLoadMore:()=>boolean|void|Promise<unknown>;
  }=$props();
  let filter=$state<MatchRosterFilter>('all'),search=$state('');
  const categories=[
    {id:'going',label:'Διαθέσιμοι'},
    {id:'maybe',label:'Ίσως'},
    {id:'not_going',label:'Μη διαθέσιμοι'},
    {id:'unanswered',label:'Χωρίς απάντηση'}
  ] as const;
  const summary=$derived(matchRosterSummary(responses.players));
  const visible=$derived(filterMatchRoster(responses.players,filter,search));
  const partial=$derived(responses.next_offset!==null||loadingMore);
  function label(status:MatchRosterFilter){
    return categories.find(item=>item.id===status)?.label??'Όλοι';
  }
</script>

<section class="preparation" aria-label="Προετοιμασία αγώνα" data-testid="mobile-match-preparation">
  <header>
    <div><h4>Διαθεσιμότητα για τον αγώνα</h4>
      <p>{partial?'Μερικός κατάλογος':'Κατάλογος παικτών'} · {summary.total} {partial?'φορτωμένοι':'παίκτες'}</p>
    </div>
    <button type="button" disabled={busy||loadingMore} onclick={()=>void onRefresh()}>Ανανέωση</button>
  </header>
  <div class="summary" role="group" aria-label="Φίλτρα διαθεσιμότητας">
    {#each categories as category (category.id)}
      <button type="button" aria-pressed={filter===category.id} onclick={()=>filter=filter===category.id?'all':category.id}>
        <strong>{summary.counts[category.id]}</strong><span>{category.label}</span>
      </button>
    {/each}
  </div>
  {#if partial}<p class="hint" role="status">Οι αριθμοί αφορούν μόνο τους φορτωμένους παίκτες. Φόρτωσε την υπόλοιπη λίστα για πλήρη εικόνα.</p>{/if}
  <label class="search">Αναζήτηση στον κατάλογο
    <input type="search" bind:value={search} placeholder="Όνομα, θέση ή αριθμός" maxlength="120"/>
  </label>
  <div class="list-heading">
    <span>{label(filter)} · {visible.length}</span>
    {#if filter!=='all'||search}<button type="button" onclick={()=>{filter='all';search='';}}>Καθαρισμός</button>{/if}
  </div>
  {#if visible.length}
    <ul>{#each visible as player (player.id)}
      <li><div class="player-details"><strong>{player.name}</strong>
        <small>{Number(player.number)>0?'#'+player.number:''}{Number(player.number)>0&&player.position?' · ':''}{player.position}</small></div>
        <span class="availability-label" data-status={matchAvailability(player.status)}>{label(matchAvailability(player.status))}</span>
      </li>
    {/each}</ul>
  {:else}
    <p role="status">{summary.total?'Δεν υπάρχουν παίκτες στο επιλεγμένο φίλτρο.':'Δεν υπάρχουν διαθέσιμοι παίκτες στον κατάλογο.'}</p>
  {/if}
  {#if responses.next_offset!==null}
    <button class="more" type="button" disabled={busy||loadingMore} onclick={()=>void onLoadMore()}>{loadingMore?'Φόρτωση…':'Φόρτωση περισσότερων παικτών'}</button>
  {/if}
  <p class="hint">Οι δηλώσεις συμμετοχής δεν αποτελούν επιλογή αποστολής, ενδεκάδας ή καταγραφή παρουσίας.</p>
</section>
<style>
  .preparation{display:grid;gap:12px;min-width:0;color:var(--lf-text);font-size:13px}
  header,.list-heading{display:flex;justify-content:space-between;align-items:center;gap:10px;flex-wrap:wrap}
  h4{font-size:17px;margin:0}p{margin:4px 0;line-height:1.5}.hint,header p,small{font-size:12px;color:var(--lf-muted)}
  button,input{min-height:44px;border:1px solid var(--lf-line);border-radius:8px;background:var(--lf-surface);color:var(--lf-text);padding:8px 10px;font:inherit}
  button{cursor:pointer;color:var(--lf-gold-light)}button:disabled{opacity:.5;cursor:default}
  button:focus-visible,input:focus-visible{outline:2px solid var(--lf-gold);outline-offset:2px}
  .summary{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:7px}
  .summary button{display:grid;text-align:left;gap:5px;min-height:68px;overflow-wrap:anywhere}
  .summary button[aria-pressed=true]{border-color:#f6bb38;background:#f6bb3819}
  .summary strong{font-size:23px;font-variant-numeric:tabular-nums;color:#f6bb38}
  .summary span{font-size:12px;color:var(--lf-text)}.search{display:grid;gap:6px}.search input{width:100%;box-sizing:border-box}
  .list-heading span{color:var(--lf-muted)}ul{list-style:none;padding:0;margin:0;display:grid;gap:5px}
  li{display:flex;align-items:center;justify-content:space-between;gap:10px;border-bottom:1px solid var(--lf-line);padding:9px 0}
  .player-details{display:grid;gap:4px;min-width:0;overflow-wrap:anywhere}.availability-label{font-size:12px;color:var(--lf-muted);text-align:right}
  .availability-label[data-status=going]{color:#e2e8d9}.availability-label[data-status=maybe]{color:#f6bb38}.availability-label[data-status=not_going]{color:#fca5a5}
  .more{width:100%}
</style>
