<script lang="ts">
  import type { PublicFixture } from './types';
  import { fixtureStatus, greekDate } from './content';
  import { fixturePublicLink } from './public-urls';
  import Icon from './PublicIcon.svelte';
  import TeamCrest from './TeamCrest.svelte';

  let { fixtures=[],clubName='ΛΕΥΤΕΡΙΑ FC',clubLogo=null }: {
    fixtures:PublicFixture[]; clubName?:string; clubLogo?:string|null
  } = $props();
  let competition = $state('');
  let season = $state('');
  const seasonFor = (day:string) => {
    const year=Number(day.slice(0,4)), month=Number(day.slice(5,7));
    if(!Number.isInteger(year) || !Number.isInteger(month) || month<1 || month>12) return '—';
    const start=month>=7?year:year-1;
    return start+'/'+String((start+1)%100).padStart(2,'0');
  };
  const competitions = $derived([...new Set(fixtures.map(row=>row.competition).filter((value):value is string=>Boolean(value)))].sort((a,b)=>a.localeCompare(b,'el-GR')));
  const seasons = $derived([...new Set(fixtures.map(row=>seasonFor(row.match_date)).filter(value=>value!=='—'))].sort().reverse());
  const rows = $derived(fixtures
    .filter(row=>(!competition||row.competition===competition)&&(!season||seasonFor(row.match_date)===season))
    .toSorted((a,b)=>a.match_date.localeCompare(b.match_date)||(a.match_time??'').localeCompare(b.match_time??'')||a.id.localeCompare(b.id)));
</script>
<section class="reference-fixture-schedule" aria-labelledby="public-fixture-schedule-title" data-testid="reference-fixture-schedule">
  <header class="reference-fixture-header">
    <h2 id="public-fixture-schedule-title"><Icon name="calendar" size={24}/> ΠΡΟΓΡΑΜΜΑ ΑΓΩΝΩΝ – <em>{clubName}</em></h2>
    <div class="reference-fixture-filters">
      <label>Διοργάνωση <select bind:value={competition} aria-label="Φίλτρο διοργάνωσης" data-testid="fixture-competition">
        <option value="">Όλες</option>
        {#each competitions as item (item)}<option value={item}>{item}</option>{/each}
      </select></label>
      <label>Περίοδος <select bind:value={season} aria-label="Φίλτρο περιόδου" data-testid="fixture-season">
        <option value="">Όλες</option>
        {#each seasons as item (item)}<option value={item}>{item}</option>{/each}
      </select></label>
    </div>
  </header>
  <div class="reference-fixture-scroll">
    <table>
      <caption class="sr-only">Δημοσιευμένοι αγώνες και αποτελέσματα {clubName}</caption>
      <thead><tr><th scope="col">ΗΜΕΡΟΜΗΝΙΑ</th><th scope="col">ΔΙΟΡΓΑΝΩΣΗ</th>
        <th scope="col">ΑΓΩΝΑΣ</th><th scope="col">ΓΗΠΕΔΟ</th><th scope="col">ΩΡΑ</th><th scope="col">ΚΑΤΑΣΤΑΣΗ</th></tr></thead>
      <tbody>
        {#each rows as fixture (fixture.id)}
          <tr>
            <td><time datetime={fixture.match_date}>{greekDate(fixture.match_date)}</time></td>
            <td><span class="fixture-chip">{fixture.competition || 'ΑΓΩΝΑΣ'}</span></td>
            <th scope="row" class="fixture-pair-col">
              <a href={fixturePublicLink(fixture)} aria-label={`Πληροφορίες αγώνα ${fixture.home_team} με ${fixture.away_team}`}>
                <span class="fixture-club"><TeamCrest name={fixture.home_team} logo={fixture.home_team_logo} {clubName} {clubLogo}/>{fixture.home_team}</span>
                <span class="fixture-vs">{fixture.home_score!==null&&fixture.away_score!==null
                  ? fixture.home_score+' : '+fixture.away_score : 'VS'}</span>
                <span class="fixture-club"><TeamCrest name={fixture.away_team} logo={fixture.away_team_logo} {clubName} {clubLogo}/>{fixture.away_team}</span>
              </a>
            </th>
            <td>{fixture.venue || '—'}</td>
            <td>{fixture.match_time || '—'}</td>
            <td><a class="fixture-status" href={fixturePublicLink(fixture)}>{fixtureStatus(fixture)}</a></td>
          </tr>
        {:else}
          <tr><td colspan="6" class="fixture-empty">Δεν υπάρχουν δημοσιευμένοι αγώνες για τα επιλεγμένα φίλτρα.</td></tr>
        {/each}
      </tbody>
    </table>
  </div>
  <p class="fixture-counter" role="status" aria-live="polite">{rows.length} από {fixtures.length} δημοσιευμένοι αγώνες</p>
</section>
<style>
  .reference-fixture-schedule{border:1px solid var(--lf-color-border,#595955);background:var(--lf-color-bg,#0b0d0e);color:var(--lf-color-text,#fafafa);margin:26px 0 0;min-width:0}
  .reference-fixture-header{min-height:56px;display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:12px;padding:9px 16px;background:linear-gradient(130deg,var(--lf-color-surface,#1d1d1d),var(--lf-color-bg,#0b0c0d));border-bottom:1px solid var(--lf-color-accent,#b18c3c)}
  .reference-fixture-header h2{font:900 clamp(20px,1.7vw,27px)/1 'Fira Sans Extra Condensed',sans-serif;margin:0;display:flex;align-items:center;gap:11px}
  .reference-fixture-header h2 :global(svg){color:var(--lf-color-accent,#f6bb38);flex-shrink:0}
  .reference-fixture-header em{color:var(--lf-color-accent,#f6bb38);font-style:normal}
  .reference-fixture-filters{display:flex;flex-wrap:wrap;gap:12px}
  .reference-fixture-filters label{display:inline-flex;align-items:center;gap:7px;font-size:12px}
  .reference-fixture-filters select{max-width:220px;min-height:36px;min-width:114px;border:1px solid var(--lf-color-muted,#8b8882);border-radius:0;background:var(--lf-color-bg,#0b0b0b);color:var(--lf-color-text,#ffffff);padding:6px 10px}
  .reference-fixture-scroll{overflow-x:auto;padding:7px 15px 9px;scrollbar-color:var(--lf-color-accent,#99772e) var(--lf-color-bg,#111111)}
  table{border-collapse:collapse;min-width:840px;width:100%;font-size:13px;text-align:left}
  thead{border-bottom:1px solid var(--lf-color-border,#666666)}
  thead th{padding:12px 8px;font:800 13px 'Fira Sans Extra Condensed',sans-serif;color:var(--lf-color-accent,#f6bb38);white-space:nowrap}
  tbody tr{border-bottom:1px solid var(--lf-color-border,#373838)}
  tbody tr:hover{background:color-mix(in srgb,var(--lf-color-accent,#f6bb38) 5.098%,transparent)}
  tbody :is(th,td){padding:10px 8px;vertical-align:middle;font-size:12px}
  .fixture-chip{border:1px solid var(--lf-color-accent,#ac8229);color:var(--lf-color-accent,#f8c354);padding:3px 9px;border-radius:5px;font:800 12px 'Fira Sans Extra Condensed',sans-serif;white-space:nowrap}
  .fixture-pair-col a{display:flex;align-items:center;justify-content:space-between;gap:10px;text-decoration:none;color:var(--lf-color-text,#ffffff);min-width:300px}
  .fixture-club{display:inline-flex;align-items:center;gap:9px;min-width:0;flex:1;font:800 13px 'Fira Sans Extra Condensed',sans-serif}
  .fixture-club:last-child{justify-content:flex-end}
  .fixture-club :global(.public-team-crest){width:29px;height:29px;flex:none}
  .fixture-vs{font:900 13px 'Fira Sans Extra Condensed',sans-serif;min-width:32px;text-align:center}
  .fixture-status{display:inline-flex;justify-content:center;min-width:78px;border:1px solid var(--lf-color-accent,#a67b2b);padding:4px 7px;text-decoration:none;color:var(--lf-color-accent,#f6cb71);font:800 11px 'Fira Sans Extra Condensed',sans-serif}
  .fixture-counter{margin:0;padding:6px 16px 12px;font-size:11px;color:var(--lf-color-muted,#aeb3b3)}
  .fixture-empty{text-align:center;padding:25px!important;color:var(--lf-color-text,#d3d3d3)}
  @media(max-width:790px){.reference-fixture-header{align-items:flex-start}.reference-fixture-header h2{font-size:22px}.reference-fixture-scroll{padding-inline:8px}}
</style>
