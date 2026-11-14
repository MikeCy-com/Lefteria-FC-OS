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

