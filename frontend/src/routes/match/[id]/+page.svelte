<script lang="ts">
  import PublicShell from '../../../lib/public/PublicShell.svelte';
  import TeamCrest from '../../../lib/public/TeamCrest.svelte';
  import { fixtureStatus, greekDate } from '../../../lib/public/content';
  import { CLUB_TIME_ZONE, kickoffInstant } from '../../../lib/public/fixture-clock';
  import type { PublicMatchData } from '../../../lib/server/public-match';
  let { data }: { data: PublicMatchData } = $props();
  const fixture = $derived(data.fixture);
  const academy = $derived(data.organization === 'lefteria_fc_academy');
  const back = $derived(academy ? '/academy#academy-fixtures' : fixture.status === 'Completed' ? '/team?tab=results' : '/team?tab=schedule');
  const group = $derived(data.site?.academy_groups.find(row => row.id === fixture.academy_group_id));
  const kickoff = $derived(kickoffInstant({ ...fixture, kickoff_at: data.kickoffAt }));
  const clockTime = $derived(kickoff === null ? null : new Intl.DateTimeFormat('el-GR', { timeZone: CLUB_TIME_ZONE, hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).format(kickoff));
  const hasScore = $derived(fixture.home_score !== null && fixture.away_score !== null);
</script>

<svelte:head>
  <title>{fixture.home_team} – {fixture.away_team} | ΛΕΥΤΕΡΙΑ FC</title>
  <meta name="description" content={`${fixture.competition || 'Αγώνας'} · ${greekDate(fixture.match_date)}${fixture.venue ? ' · ' + fixture.venue : ''}`} />
</svelte:head>
<PublicShell site={data.site} branding={data.branding} organization={data.organization} navigationGroups={data.navigationGroups}>
  <section class="match-detail" data-testid="public-match-detail">
    <nav class="match-breadcrumb" aria-label="Διαδρομή σελίδας"><a href="/">Αρχική</a><span aria-hidden="true">/</span><a href={academy ? '/academy' : '/team'}>{academy ? 'Ακαδημία' : 'Πρώτη ομάδα'}</a><span aria-hidden="true">/</span><span aria-current="page">Αγώνας</span></nav>
    <div class="match-intro"><p class="public-eyebrow">{academy ? 'ΑΚΑΔΗΜΙΑ ΛΕΥΤΕΡΙΑ FC' : 'ΠΡΩΤΗ ΟΜΑΔΑ'}</p><h1>ΗΜΕΡΑ ΑΓΩΝΑ</h1><p>{fixture.competition || 'Αγώνας'}{group ? ` · ${group.name}` : ''}</p></div>
    <article class="match-scoreboard" aria-label={`${fixture.home_team} εναντίον ${fixture.away_team}`}>
      <header><span>{fixture.competition || 'Αγώνας'}</span><time datetime={fixture.match_date}>{greekDate(fixture.match_date)}</time></header>
      <div class="match-teams">
        <div class="match-team"><TeamCrest name={fixture.home_team} logo={fixture.home_team_logo} clubName={data.branding?.name || ''} clubLogo={data.branding?.logo_url} /><h2>{fixture.home_team}</h2><span>ΓΗΠΕΔΟΥΧΟΣ</span></div>
        <div class="match-score"><p class:match-live={fixture.status === 'Live' || fixture.status === 'Half Time'}>{fixtureStatus(fixture)}</p><strong aria-label={hasScore ? `Σκορ ${fixture.home_score} προς ${fixture.away_score}` : 'Δεν έχει καταχωριστεί σκορ'}>{hasScore ? `${fixture.home_score} : ${fixture.away_score}` : '—'}</strong>{#if clockTime}<span>{clockTime}<small>ΩΡΑ ΚΥΠΡΟΥ</small></span>{:else if fixture.match_time}<span>{fixture.match_time}<small>ΩΡΑ ΚΥΠΡΟΥ</small></span>{:else}<span class="match-time-pending">Η ώρα θα ανακοινωθεί</span>{/if}</div>
        <div class="match-team"><TeamCrest name={fixture.away_team} logo={fixture.away_team_logo} clubName={data.branding?.name || ''} clubLogo={data.branding?.logo_url} /><h2>{fixture.away_team}</h2><span>ΦΙΛΟΞΕΝΟΥΜΕΝΟΣ</span></div>
      </div>
      {#if fixture.venue}<footer><span>ΓΗΠΕΔΟ</span><strong>{fixture.venue}</strong></footer>{/if}
    </article>
    <div class="match-footer"><p>Οι ώρες αγώνων εμφανίζονται στην ώρα Κύπρου.</p><a class="public-button public-button-outline" href={back}>← {academy ? 'ΑΓΩΝΕΣ ΑΚΑΔΗΜΙΑΣ' : fixture.status === 'Completed' ? 'ΟΛΑ ΤΑ ΑΠΟΤΕΛΕΣΜΑΤΑ' : 'ΠΡΟΓΡΑΜΜΑ ΑΓΩΝΩΝ'}</a></div>
  </section>
</PublicShell>

<style>
  .match-detail{max-width:1200px;margin:auto;padding:28px clamp(20px,4vw,64px) 80px}.match-breadcrumb{display:flex;flex-wrap:wrap;gap:12px;color:#aaa;font-size:.85rem}.match-breadcrumb a{color:#d8d8d8;text-decoration:none}.match-breadcrumb a:hover{color:#f5a623}.match-intro{padding:52px 0 32px}.match-intro h1{font-family:'Fira Sans Condensed',sans-serif;font-size:clamp(2.5rem,5.5vw,5rem);line-height:1;margin:12px 0 18px;letter-spacing:-.03em}.match-intro>p:last-child{font-size:1.05rem;color:#bbb}.match-scoreboard{background:linear-gradient(135deg,#1d1b16,#111 55%);border:1px solid #30302c;border-top:3px solid #f5a623}.match-scoreboard>header{padding:20px 28px;display:flex;justify-content:space-between;gap:16px;border-bottom:1px solid #ffffff12;color:#bbb;font-size:.9rem}.match-scoreboard>header>span{color:#f5a623}.match-teams{display:grid;grid-template-columns:1fr minmax(110px,.6fr) 1fr;gap:20px;padding:48px 28px;align-items:center;text-align:center}.match-team{min-width:0;display:flex;align-items:center;flex-direction:column;gap:16px}.match-team :global(.public-team-crest){width:96px;height:96px;flex-shrink:0}.match-team h2{font-family:'Fira Sans Condensed',sans-serif;font-size:clamp(1.1rem,2.2vw,1.9rem);line-height:1.15;overflow-wrap:anywhere;margin:0}.match-team>span{font-size:.65rem;letter-spacing:.12em;color:#8d8d8d}.match-score{display:flex;align-items:center;flex-direction:column;gap:14px}.match-score>p{color:#c7c7c7;font-size:.8rem;margin:0}.match-score>p.match-live{color:#f5a623}.match-score>strong{font-family:'Fira Sans Condensed',sans-serif;font-size:clamp(2.5rem,6vw,5rem);line-height:1;color:#fff;white-space:nowrap}.match-score>span{font-size:1.1rem;color:#f5a623}.match-score small{display:block;color:#919191;font-size:.6rem;letter-spacing:.08em;margin-top:8px}.match-score .match-time-pending{font-size:.75rem;color:#aaa}.match-scoreboard>footer{display:flex;justify-content:center;align-items:center;gap:16px;padding:24px;border-top:1px solid #ffffff12;text-align:center}.match-scoreboard>footer span{font-size:.65rem;letter-spacing:.1em;color:#888}.match-scoreboard>footer strong{font-size:.95rem;font-weight:500}.match-footer{padding-top:28px;display:flex;align-items:center;justify-content:space-between;gap:24px;flex-wrap:wrap}.match-footer p{font-size:.8rem;color:#999}.match-footer a{text-decoration:none;font-size:.8rem}.match-breadcrumb a:focus-visible,.match-footer a:focus-visible{outline:2px solid #f5a623;outline-offset:5px}
  @media(max-width:600px){.match-detail{padding-inline:16px}.match-intro{padding-top:34px}.match-scoreboard>header{padding:16px;flex-direction:column;gap:8px}.match-teams{padding:30px 12px;gap:10px;grid-template-columns:1fr minmax(70px,.55fr) 1fr}.match-team :global(.public-team-crest){width:62px;height:62px}.match-team{gap:12px}.match-team>span{font-size:.55rem;letter-spacing:.04em}.match-scoreboard>footer{flex-direction:column;gap:8px}.match-score>span{font-size:.9rem}.match-footer{align-items:stretch}.match-footer a{text-align:center;width:100%}}
</style>
