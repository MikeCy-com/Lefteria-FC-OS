<script lang="ts">
  import type { PublicFixture } from './types';
  import { fixtureStatus, greekDate } from './content';
  let { fixtures }: { fixtures: PublicFixture[] } = $props();
</script>
<div class="public-fixtures" aria-label="Αγώνες και αποτελέσματα">{#each fixtures as fixture (fixture.id)}<article class="fixture-row"><div><p class="public-eyebrow">{fixture.competition || fixtureStatus(fixture)}</p><time datetime={fixture.match_date}>{greekDate(fixture.match_date)}{fixture.match_time ? ` · ${fixture.match_time}` : ''}</time></div><div class="fixture-teams"><strong>{fixture.home_team}</strong><span>{fixture.home_score !== null && fixture.away_score !== null ? `${fixture.home_score} : ${fixture.away_score}` : '—'}</span><strong>{fixture.away_team}</strong></div><div class="fixture-venue"><span>{fixtureStatus(fixture)}</span>{#if fixture.venue}<p>{fixture.venue}</p>{/if}<a class="fixture-detail-link" href={`/match/${encodeURIComponent(fixture.id)}`} aria-label={`Λεπτομέρειες αγώνα ${fixture.home_team} – ${fixture.away_team}`}>ΛΕΠΤΟΜΕΡΕΙΕΣ ↗</a></div><a class="fixture-mobile-detail-link" href={`/match/${encodeURIComponent(fixture.id)}`} aria-label={`Λεπτομέρειες αγώνα ${fixture.home_team} – ${fixture.away_team}`}>ΛΕΠΤΟΜΕΡΕΙΕΣ ΑΓΩΝΑ ↗</a></article>{/each}</div>

