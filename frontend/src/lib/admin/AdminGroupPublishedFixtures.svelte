<script lang="ts">
  import {browser} from '$app/env';
  import {untrack} from 'svelte';
  import type {PublicFixture} from '../public/types.ts';
  import {greekDate,fixtureStatus} from '../public/content.ts';
  type Owner='lefteria_fc'|'lefteria_fc_academy';
  let {groupId,organization,actorId}: {groupId:string;organization:Owner;actorId:string}=$props();
  let fixtures=$state<PublicFixture[]>([]);
  let loading=$state(false),unavailable=$state(false),refresh=$state(0);
  let tab=$state<'all'|'scheduled'|'results'>('all');
  const selected=$derived(fixtures.filter(f=>
    (organization==='lefteria_fc'?f.team_id:f.academy_group_id)===groupId &&
    (tab==='scheduled'?f.status!=='Completed':tab==='results'?f.status==='Completed':true))
    .toSorted((a,b)=>a.match_date.localeCompare(b.match_date)||
      (a.match_time??'').localeCompare(b.match_time??'')||a.id.localeCompare(b.id)));
  function verified(x:unknown,owner:Owner):x is {organization_id:Owner;fixtures:PublicFixture[]}{
    if(!x||typeof x!=='object'||Array.isArray(x))return false;
    const y=x as Record<string,unknown>;
    return y.organization_id===owner&&Array.isArray(y.fixtures)&&y.fixtures.length<=50&&
      y.fixtures.every((row:unknown)=>{
        if(!row||typeof row!=='object'||Array.isArray(row))return false;
        const f=row as Record<string,unknown>;
        return f.organization_id===owner&&typeof f.id==='string'&&f.id.length>0&&
          typeof f.match_date==='string'&&/^\d{4}-\d\d-\d\d$/.test(f.match_date)&&
          typeof f.home_team==='string'&&typeof f.away_team==='string'&&
          typeof f.status==='string'&&
          (f.home_score===null||Number.isInteger(f.home_score)&&Number(f.home_score)>=0)&&
          (f.away_score===null||Number.isInteger(f.away_score)&&Number(f.away_score)>=0)&&
          (f.team_id===null||typeof f.team_id==='string')&&
          (f.academy_group_id===null||typeof f.academy_group_id==='string');
      });
  }
  $effect(()=>{
    const org=organization,playerGroup=groupId,actor=actorId;
    void refresh;void actor;void playerGroup;
    fixtures=[];loading=false;unavailable=false;
    if(!browser||!org||!playerGroup||!actor)return;
    const controller=new AbortController();loading=true;
    untrack(()=>void(async()=>{
      try{
        const res=await fetch('/api/public/site?organization_id='+encodeURIComponent(org),{
          credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'},signal:controller.signal
        });
        if(!res.ok)throw Error('Οι δημοσιευμένοι αγώνες δεν είναι διαθέσιμοι.');
        const raw:unknown=await res.json();
        if(!verified(raw,org))throw Error('Μη επαληθευμένος κατάλογος αγώνων.');
        if(!controller.signal.aborted)fixtures=raw.fixtures;
      }catch{if(!controller.signal.aborted)unavailable=true;}
      finally{if(!controller.signal.aborted)loading=false;}
    })());
    return()=>controller.abort();
  });
</script>
<section class="admin-group-fixtures" aria-labelledby="group-fixtures-heading" data-testid="admin-group-published-fixtures">
  <div class="group-fixture-heading">
    <div><h4 id="group-fixtures-heading">Αγώνες & αποτελέσματα τμήματος</h4>
      <p class="admin-muted">Μόνο δημοσιευμένοι αγώνες για την επιλεγμένη ομάδα. Τα νέα παιχνίδια δημιουργούνται παρακάτω ως ελεγχόμενα πρόχειρα.</p></div>
    <button type="button" class="admin-secondary" onclick={()=>refresh++} disabled={loading}>Ανανέωση</button>
  </div>
  <div class="group-fixture-tabs" role="group" aria-label="Φίλτρο αγώνων">
    <button type="button" aria-pressed={tab==='all'} onclick={()=>tab='all'}>Όλοι</button>
    <button type="button" aria-pressed={tab==='scheduled'} onclick={()=>tab='scheduled'}>Πρόγραμμα</button>
    <button type="button" aria-pressed={tab==='results'} onclick={()=>tab='results'}>Αποτελέσματα</button>
  </div>
  {#if loading}
    <p role="status">Φόρτωση αγώνων…</p>
  {:else if unavailable}
    <p role="alert">Οι δημοσιευμένοι αγώνες δεν είναι διαθέσιμοι. Δεν εμφανίζονται υποθετικά σκορ.</p>
  {:else if !selected.length}
    <p class="admin-muted">Δεν υπάρχουν δημοσιευμένοι αγώνες για αυτό το τμήμα και το επιλεγμένο φίλτρο.</p>
  {:else}
    <div class="group-fixture-scroll">
      <table>
        <caption class="sr-only">Δημοσιευμένοι αγώνες του επιλεγμένου τμήματος</caption>
        <thead><tr><th>Ημερομηνία</th><th>Αγώνας</th><th>Γήπεδο</th><th>Κατάσταση</th><th>Λεπτομέρειες</th></tr></thead>
        <tbody>{#each selected as fixture (fixture.id)}
          <tr>
            <td><time datetime={fixture.match_date}>{greekDate(fixture.match_date)}</time>
              {#if fixture.match_time}<small>{fixture.match_time}</small>{/if}</td>
            <th scope="row">{fixture.home_team} <span class="group-score">{fixture.home_score!==null&&fixture.away_score!==null?fixture.home_score+' : '+fixture.away_score:'–'}</span> {fixture.away_team}</th>
            <td>{fixture.venue??'—'}</td><td>{fixtureStatus(fixture)}</td>
            <td><a href={'/match/'+encodeURIComponent(fixture.id)}>Προβολή ↗</a></td>
          </tr>
        {/each}</tbody>
      </table>
    </div>
  {/if}
  <p class="group-fixtures-warning">Η καταχώριση ή διόρθωση σκορ απαιτεί ξεχωριστό προστατευμένο API και δεν εμφανίζεται ως λειτουργική ενέργεια μέχρι να είναι διαθέσιμη.</p>
</section>
<style>
  .admin-group-fixtures{display:grid;gap:16px;margin:18px 0;min-width:0}
  .group-fixture-heading{display:flex;justify-content:space-between;align-items:center;gap:16px;flex-wrap:wrap}
  .group-fixture-heading h4{font-size:22px;margin:0 0 8px}
  .group-fixture-heading p{margin:0;max-width:75ch}
  .group-fixture-tabs{display:flex;flex-wrap:wrap;gap:0}
  .group-fixture-tabs button{min-height:42px;padding:10px 16px;background:#151719;color:#eee;border:1px solid #666;cursor:pointer}
  .group-fixture-tabs button[aria-pressed=true]{background:#f6bb38;color:#080808}
  .group-fixture-scroll{overflow-x:auto}
  table{width:100%;min-width:710px;border-collapse:collapse;text-align:left}
  :is(td,th){padding:12px 10px;border-bottom:1px solid #494949;font-size:13px}
  thead th{color:#ffcd58;font-weight:800}
  tbody th{font-weight:650}
  td small{display:block;color:#b5b5b5;margin-top:5px}
  .group-score{color:#f6bb38;margin:0 8px;white-space:nowrap}
  .group-fixtures-warning{color:#c3bdab;font-size:12px;line-height:1.5;margin:0}
</style>
