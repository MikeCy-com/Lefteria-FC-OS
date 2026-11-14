<script lang="ts">
  import type { PublicFixture } from './types';
  import { fixtureStatus, greekDate } from './content';
  let { fixtures }: { fixtures: PublicFixture[] } = $props();
</script>
<div class="public-fixtures" aria-label="Αγώνες και αποτελέσματα">{#each fixtures as fixture (fixture.id)}<article class="fixture-row"><div><p class="public-eyebrow">{fixture.competition || fixtureStatus(fixture)}</p><time datetime={fixture.match_date}>{greekDate(fixture.match_date)}{fixture.match_time ? ` · ${fixture.match_time}` : ''}</time></div><div class="fixture-teams"><strong>{fixture.home_team}</strong><span>{fixture.home_score !== null && fixture.away_score !== null ? `${fixture.home_score} : ${fixture.away_score}` : '—'}</span><strong>{fixture.away_team}</strong></div><div class="fixture-venue"><span>{fixtureStatus(fixture)}</span>{#if fixture.venue}<p>{fixture.venue}</p>{/if}<a class="fixture-detail-link" href={`/match/${encodeURIComponent(fixture.id)}`} aria-label={`Λεπτομέρειες αγώνα ${fixture.home_team} – ${fixture.away_team}`}>ΛΕΠΤΟΜΕΡΕΙΕΣ ↗</a></div><a class="fixture-mobile-detail-link" href={`/match/${encodeURIComponent(fixture.id)}`} aria-label={`Λεπτομέρειες αγώνα ${fixture.home_team} – ${fixture.away_team}`}>ΛΕΠΤΟΜΕΡΕΙΕΣ ΑΓΩΝΑ ↗</a></article>{/each}</div>

<style>
  .fixture-detail-link {
    display:inline-flex;align-items:center;min-height:44px;margin-top:10px;
    color:#f5a623;font-size:13px;line-height:1.45;font-weight:700;text-decoration:none;
  }
  .fixture-detail-link:hover {text-decoration:underline}
  .fixture-detail-link:focus-visible,.fixture-mobile-detail-link:focus-visible {
    outline:3px solid #ffca46;outline-offset:3px;
  }
  .fixture-mobile-detail-link {display:none}
  .fixture-row time {display:block;color:#ddd;font-size:14px;line-height:1.5}
  .fixture-teams {min-width:0}
  .fixture-teams strong {min-width:0;overflow-wrap:anywhere;hyphens:auto}
  @media(max-width:767px) {
    .fixture-row {grid-template-columns:minmax(0,1fr);gap:16px;padding:22px 0}
    .fixture-teams {
      display:grid;grid-template-columns:minmax(0,1fr) auto minmax(0,1fr);
      align-items:center;gap:10px;font-size:clamp(17px,4.8vw,23px);
    }
    .fixture-teams strong {line-height:1.2}
    .fixture-teams span {font-size:clamp(21px,6vw,28px)}
    .fixture-mobile-detail-link {
      display:inline-flex;align-items:center;justify-content:center;min-height:44px;
      padding:12px 16px;margin-top:4px;border:1px solid #f5a623;
      color:#f5a623;font-size:13px;line-height:1.5;font-weight:700;text-decoration:none;
    }
  }
  @media(max-width:360px) {
    .fixture-teams {gap:6px;font-size:16px}
    .fixture-teams span {font-size:20px}
  }
</style>
