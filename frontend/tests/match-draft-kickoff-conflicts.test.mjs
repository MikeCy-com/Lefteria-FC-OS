import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
test('match kickoff constraints are race-safe and do not rewrite existing drafts',()=>{
 const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261128203000_MatchDraftKickoffConflicts.cs');
 assert.match(sql,/CREATE UNIQUE INDEX uq_football_draft_academy_group_kickoff/);
 assert.match(sql,/organization_id,academy_group_id,match_date,kickoff_local/);
 assert.match(sql,/organization_id,club_team_id,match_date,kickoff_local/);
 assert.match(sql,/scope_id,playing_field_id,match_date,kickoff_local/);
 assert.doesNotMatch(sql,/DELETE FROM|UPDATE club\.football_match_drafts|GRANT /);
 const api=read('backend/src/LefteriaFc.Api/Football/AdminFootballTerritoryEndpoints.cs');
 assert.match(api,/e.SqlState is "23505"/);
 const fixture=read('ops/postgres/football-city-location-catalog-checks.sql');
 for(const marker of ['duplicate_group_kickoff_accepted','duplicate_shared_field_kickoff_accepted','duplicate_club_kickoff_accepted'])assert.ok(fixture.includes(marker));
});
