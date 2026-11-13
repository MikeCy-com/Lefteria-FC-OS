<script lang="ts">
  import type { PublicFixture } from './types';
  import { fixtureStatus, greekDate } from './content';
  let { fixtures }: { fixtures: PublicFixture[] } = $props();
</script>
<div class="public-fixtures">{#each fixtures as fixture (fixture.id)}<article class="fixture-row"><div><p class="public-eyebrow">{fixture.competition || fixtureStatus(fixture)}</p><time datetime={fixture.match_date}>{greekDate(fixture.match_date)}{fixture.match_time ? ` · ${fixture.match_time}` : ''}</time></div><div class="fixture-teams"><strong>{fixture.home_team}</strong><span>{fixture.home_score !== null && fixture.away_score !== null ? `${fixture.home_score} : ${fixture.away_score}` : '—'}</span><strong>{fixture.away_team}</strong></div><div class="fixture-venue"><span>{fixtureStatus(fixture)}</span>{#if fixture.venue}<p>{fixture.venue}</p>{/if}<a class="fixture-detail-link" href={`/match/${encodeURIComponent(fixture.id)}`} aria-label={`Λεπτομέρειες αγώνα ${fixture.home_team} – ${fixture.away_team}`}>ΛΕΠΤΟΜΕΡΕΙΕΣ ↗</a></div><a class="fixture-mobile-detail-link" href={`/match/${encodeURIComponent(fixture.id)}`} aria-label={`Λεπτομέρειες αγώνα ${fixture.home_team} – ${fixture.away_team}`}>ΛΕΠΤΟΜΕΡΕΙΕΣ ΑΓΩΝΑ ↗</a></article>{/each}</div>

<style>.fixture-detail-link{display:inline-block;margin-top:12px;color:#f5a623;font-size:.7rem;letter-spacing:.06em;text-decoration:none}.fixture-detail-link:hover{text-decoration:underline}.fixture-detail-link:focus-visible{outline:2px solid #f5a623;outline-offset:4px}  .fixture-mobile-detail-link{display:none}
  @media(max-width:767px){.fixture-mobile-detail-link{display:inline-flex;align-items:center;justify-content:center;min-height:44px;padding:12px 16px;margin-top:8px;border:1px solid #f5a623;color:#f5a623;font-size:13px;font-weight:700;text-decoration:none}.fixture-mobile-detail-link:focus-visible{outline:3px solid #ffca46;outline-offset:3px}}
</style>
