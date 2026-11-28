using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Race-safe exact-kickoff uniqueness for private match drafts.
/// Existing duplicates require explicit operator reconciliation, never deletion.
/// This is not a duration-based or published-fixture exclusion constraint.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261128203000_MatchDraftKickoffConflicts")]
public sealed class MatchDraftKickoffConflicts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE UNIQUE INDEX uq_football_draft_academy_group_kickoff
        ON club.football_match_drafts(organization_id,academy_group_id,match_date,kickoff_local)
        WHERE academy_group_id IS NOT NULL;
      CREATE UNIQUE INDEX uq_football_draft_club_team_kickoff
        ON club.football_match_drafts(organization_id,club_team_id,match_date,kickoff_local)
        WHERE club_team_id IS NOT NULL;
      CREATE UNIQUE INDEX uq_football_draft_field_kickoff
        ON club.football_match_drafts(scope_id,playing_field_id,match_date,kickoff_local);
      """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP INDEX club.uq_football_draft_field_kickoff;
      DROP INDEX club.uq_football_draft_club_team_kickoff;
      DROP INDEX club.uq_football_draft_academy_group_kickoff;
      """);
}
