<script lang="ts">
  import type {PublicFixture} from './types';
  import type {PublicSections} from './sections-types';
  import {greekDate} from './content';
  import {fixtureStatus} from './content';
  import {fixturePublicLink} from './public-urls';
  import Icon from './PublicIcon.svelte';
  import TeamCrest from './TeamCrest.svelte';
  import { publicImages } from './images';

  let {fixtures=[],standings=[],teamId=null,clubLogo=null,clubName='ΛΕΥΤΕΡΙΑ FC'}:{
    fixtures:PublicFixture[];
    standings?:PublicSections['standings'];
    teamId:string|null;
    clubLogo?:string|null;
    clubName?:string;
  }=$props();
  const upcoming=$derived([...fixtures].filter(fixture=>fixture.status!=='Completed')
    .sort((a,b)=>a.match_date.localeCompare(b.match_date)||
      (a.match_time??'').localeCompare(b.match_time??'')||a.id.localeCompare(b.id)));
  const next=$derived(upcoming[0]??null);
  const nearest=$derived(upcoming.slice(0,4));
  const competition=$derived(next?.competition??null);
  // Standings projection is supplied only for the actual First Team. The
  // parent component deliberately passes none for Women/Selected teams.
  const table=$derived(competition
    ? standings.filter(row=>row.competition===competition).slice(0,6)
    : standings.slice(0,6));
  const moreHref=$derived(teamId?
    '/team?tab=schedule&team_id='+encodeURIComponent(teamId):'/team?tab=schedule');
  const standingsHref=$derived(teamId?
    '/team?team_id='+encodeURIComponent(teamId)+'#standings':'/team#standings');
</script>

<section class="match-overview" aria-labelledby="match-overview-title"
  data-testid="team-matches-overview">
  <header class="match-overview-heading">
    <div><p class="eyebrow">ΛΕΥΤΕΡΙΑ FC · ΑΓΩΝΙΣΤΙΚΟ</p><h2 id="match-overview-title">ΑΓΩΝΕΣ & ΠΡΟΓΡΑΜΜΑ</h2></div>
    <a class="view-all" href={moreHref}>ΟΛΟ ΤΟ ΠΡΟΓΡΑΜΜΑ <Icon name="arrow" size={17}/></a>
  </header>
  <div class="match-overview-grid">
    <article class="match-panel next-match" data-testid="team-featured-next-match" style:background-image={`linear-gradient(125deg,#0b0d0de8,#0e1111e0 76%),url("${publicImages.pitch}")`}>
      <h3><Icon name="calendar" size={20}/> ΕΠΟΜΕΝΟΣ ΑΓΩΝΑΣ</h3>
      {#if next}
        <p class="competition">{next.competition||'ΑΓΩΝΑΣ'}</p>
        <a class="match-pair" href={fixturePublicLink(next)}
          data-testid="team-next-match-detail">
          <div><TeamCrest name={next.home_team} logo={next.home_team_logo}
            {clubName} {clubLogo}/><strong>{next.home_team}</strong></div>
          <span>VS</span>
          <div><TeamCrest name={next.away_team} logo={next.away_team_logo}
            {clubName} {clubLogo}/><strong>{next.away_team}</strong></div>
        </a>
        <div class="match-facts">
          <p><Icon name="calendar" size={17}/><time datetime={next.match_date}>{greekDate(next.match_date)}</time></p>
          {#if next.match_time}<p><Icon name="clock" size={17}/>{next.match_time}</p>{/if}
          {#if next.venue}<p><Icon name="pin" size={17}/>{next.venue}</p>{/if}
        </div>
        <a href={fixturePublicLink(next)} class="match-detail">
          ΛΕΠΤΟΜΕΡΕΙΕΣ ΑΓΩΝΑ <Icon name="arrow" size={18}/>
        </a>
      {:else}
        <p class="empty">Δεν υπάρχει επόμενος επιβεβαιωμένος αγώνας για αυτή την ομάδα.</p>
      {/if}
    </article>
    <article class="match-panel upcoming-matches" data-testid="team-upcoming-match-list">
      <h3><Icon name="calendar" size={20}/> ΕΠΟΜΕΝΟΙ ΑΓΩΝΕΣ</h3>
      {#if nearest.length}
        <ol>
          {#each nearest as match (match.id)}
            <li><a href={fixturePublicLink(match)}>
              <span class="match-day"><strong>{greekDate(match.match_date)}</strong>
                {#if match.match_time}<small>{match.match_time}</small>{/if}</span>
              <span class="match-name"><strong>{match.home_team} – {match.away_team}</strong>
                <small>{match.competition||fixtureStatus(match)}</small></span>
              <Icon name="arrow" size={17}/>
            </a></li>
          {/each}
        </ol>
      {:else}
        <p class="empty">Δεν υπάρχουν προγραμματισμένοι αγώνες.</p>
      {/if}
    </article>
    <article class="match-panel league-table" data-testid="team-first-team-standings">
        <h3><Icon name="trophy" size={20}/> ΒΑΘΜΟΛΟΓΙΑ</h3>
        {#if competition}<p class="competition">{competition}</p>{/if}
        {#if table.length}
          <div class="standings-scroll">
            <table>
              <caption class="sr-only">Δημοσιευμένη βαθμολογία {competition||'ομάδας'}</caption>
              <thead><tr><th scope="col">ΟΜΑΔΑ</th><th scope="col">ΑΓ</th><th scope="col">Ν</th><th scope="col">Ι</th><th scope="col">Η</th><th scope="col">ΔΓ</th><th scope="col">Β</th></tr></thead>
              <tbody>
                {#each table as row (row.id)}
                  <tr><th scope="row">{row.team_name}</th>
                    <td>{row.played}</td><td>{row.won}</td><td>{row.drawn}</td><td>{row.lost}</td>
                    <td>{Number(row.goals_for)-Number(row.goals_against)>0?'+':''}{Number(row.goals_for)-Number(row.goals_against)}</td>
                    <td class="points">{row.points}</td></tr>
                {/each}
              </tbody>
            </table>
          </div>
        {:else}
          <p class="empty">Δεν έχει δημοσιευτεί επιβεβαιωμένη βαθμολογία για αυτή την ομάδα.</p>
        {/if}
        <a class="view-all bottom" href={standingsHref}>ΠΛΗΡΗΣ ΒΑΘΜΟΛΟΓΙΑ <Icon name="arrow" size={16}/></a>
      </article>
  </div>
</section>

<style>
  .match-overview{margin:0 0 35px;color:#f7f7f5}
  .match-overview-heading{display:flex;align-items:end;justify-content:space-between;gap:12px;flex-wrap:wrap;margin-bottom:16px}
  .eyebrow{color:#ffc548;font-size:12px;font-weight:800;letter-spacing:.08em;margin:0 0 6px}
  h2{font:900 clamp(27px,2.8vw,42px)/1.06 'Fira Sans Extra Condensed',sans-serif;
    font-style:italic;margin:0}
  .view-all{display:inline-flex;align-items:center;gap:10px;min-height:44px;color:#ffc548;
    font:800 14px 'Fira Sans Extra Condensed',sans-serif;text-decoration:none}
  .match-overview-grid{display:grid;grid-template-columns:minmax(0,1.2fr) minmax(0,1fr) minmax(0,.9fr);gap:16px}
  .match-panel{min-width:0;position:relative;overflow:hidden;border:1px solid #77706a;border-radius:6px;
    background:linear-gradient(130deg,#1b1e1e,#090b0b 72%,#32200b);padding:16px;box-shadow:inset 0 1px #ffffff23}
  .match-panel::before{content:"";position:absolute;pointer-events:none;left:0;right:0;top:0;height:2px;
    background:linear-gradient(90deg,transparent,#ffc754,#ffbf3660,transparent);box-shadow:0 0 10px #ffb82a6b}
  .next-match{background-size:cover;background-position:center;}
  .match-panel h3{font:800 clamp(19px,1.5vw,25px)/1.1 'Fira Sans Extra Condensed',sans-serif;
    font-style:italic;display:flex;align-items:center;gap:9px;
    padding:0 0 12px;margin:0;border-bottom:1px solid #ffffff27}
  .match-panel h3 :global(svg){color:#ffc548;flex:none}
  .competition{font-size:12px;color:#ffca6a;margin:12px 0 4px}
  .match-pair{display:grid;grid-template-columns:minmax(0,1fr) auto minmax(0,1fr);gap:12px;
    align-items:center;text-align:center;margin:16px 0;color:#fff}
  .match-pair>div{display:flex;flex-direction:column;align-items:center;gap:10px;min-width:0}
  .match-pair :global(.public-team-crest){width:70px;height:70px;margin:auto}
  .match-pair strong{font:750 clamp(15px,1.3vw,22px)/1.17 'Fira Sans Extra Condensed',sans-serif;
    overflow-wrap:anywhere}
  .match-pair>span{font:900 29px 'Fira Sans Extra Condensed',sans-serif;font-style:italic}
  .match-facts{display:flex;align-items:center;justify-content:center;gap:8px 19px;
    flex-wrap:wrap;border-top:1px solid #ffffff3b;padding-top:14px}
  .match-facts p{display:inline-flex;align-items:center;gap:6px;font-size:13px;margin:0}
  .match-facts :global(svg){color:#ffc548;flex:none}
  .match-detail{display:flex;align-items:center;justify-content:center;gap:10px;
    min-height:45px;margin-top:18px;background:var(--public-gold);color:#070809;
    font-weight:850;font-size:13px;border-radius:3px;padding:10px}
  .upcoming-matches ol{list-style:none;margin:0;padding:0}
  .upcoming-matches li+li{border-top:1px solid #ffffff28}
  .upcoming-matches li a{min-height:81px;display:grid;grid-template-columns:minmax(92px,.8fr) minmax(0,1.5fr) 18px;
    align-items:center;gap:12px;padding:8px 0;color:#fff}
  .match-day{border:1px solid #81754d;min-height:55px;display:flex;justify-content:center;
    flex-direction:column;text-align:center;padding:6px}
  .match-day strong{font-size:11px;font-weight:800}
  .match-day small,.match-name small{font-size:11px;color:#bdc1bf}
  .match-name{display:grid;gap:4px;min-width:0;overflow-wrap:anywhere}
  .match-name strong{font-size:12px;line-height:1.3}
  .upcoming-matches li a :global(svg){color:#ffc548}
  .standings-scroll{overflow-x:auto}
  table{width:100%;border-collapse:collapse;text-align:left;margin-top:13px;font-size:12px}
  th,td{border-bottom:1px solid #ffffff24;padding:8px 3px}
  thead th{color:#f8c34b;font-weight:850;font-size:10px}
  tbody th{font-weight:700;overflow-wrap:anywhere}
  td{text-align:center}
  td.points{color:var(--public-gold);font-weight:800}
  .bottom{margin-top:10px}
  .empty{font-size:13px;line-height:1.6;color:#c8cac7;padding-top:20px}
  .match-panel a:focus-visible{outline:3px solid #ffd357;outline-offset:3px}
  @media(max-width:1240px){.match-overview-grid{grid-template-columns:repeat(2,minmax(0,1fr))}
    .league-table{grid-column:1/-1}}
  @media(max-width:780px){.match-overview-grid{grid-template-columns:minmax(0,1fr)}
    .league-table{grid-column:auto}.match-panel{padding:14px}
    .match-pair :global(.public-team-crest){width:58px;height:58px}}
</style>
