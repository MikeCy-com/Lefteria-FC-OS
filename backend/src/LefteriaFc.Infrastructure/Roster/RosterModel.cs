using LefteriaFc.Domain.Organizations;
using LefteriaFc.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Infrastructure.Roster;

internal static class RosterModel
{
    public static void Configure(ModelBuilder model)
    {
        var teams = model.Entity<RosterTeamRecord>();
        teams.ToTable("teams", "club", table => table.HasCheckConstraint("ck_team_club", "organization_id = 'lefteria_fc'"));
        teams.HasKey(row => row.Id);
        teams.HasAlternateKey(row => new { row.OrganizationId, row.Id });
        teams.Property(row => row.Id).HasMaxLength(128).UseCollation("C").ValueGeneratedNever();
        teams.Property(row => row.OrganizationId).HasMaxLength(32);
        teams.Property(row => row.Name).HasMaxLength(200);
        teams.HasOne<OrganizationRecord>().WithMany().HasForeignKey(row => row.OrganizationId).OnDelete(DeleteBehavior.Restrict);

        var groups = model.Entity<RosterAcademyGroupRecord>();
        groups.ToTable("academy_groups", "club", table => table.HasCheckConstraint("ck_group_academy", "organization_id = 'lefteria_fc_academy'"));
        groups.HasKey(row => row.Id);
        groups.HasAlternateKey(row => new { row.OrganizationId, row.Id });
        groups.Property(row => row.Id).HasMaxLength(128).UseCollation("C").ValueGeneratedNever();
        groups.Property(row => row.OrganizationId).HasMaxLength(32);
        groups.Property(row => row.Name).HasMaxLength(200);
        groups.HasOne<OrganizationRecord>().WithMany().HasForeignKey(row => row.OrganizationId).OnDelete(DeleteBehavior.Restrict);

        var players = model.Entity<RosterPlayerRecord>();
        players.ToTable("players", "club", table =>
        {
            table.HasCheckConstraint("ck_player_team_type", "team_type IN ('First Team','Academy')");
            table.HasCheckConstraint("ck_player_position", "position IN ('Goalkeeper','Defender','Midfielder','Forward')");
        });
        players.HasKey(row => row.Id);
        players.HasAlternateKey(row => new { row.OrganizationId, row.Id });
        players.Property(row => row.Id).HasMaxLength(128).UseCollation("C").ValueGeneratedNever();
        players.Property(row => row.OrganizationId).HasMaxLength(32);
        players.Property(row => row.Name).HasMaxLength(200);
        players.Property(row => row.Position).HasMaxLength(32);
        players.Property(row => row.TeamType).HasMaxLength(16);
        players.Property(row => row.TeamId).HasMaxLength(128).UseCollation("C");
        players.Property(row => row.PrimaryAcademyGroupId).HasMaxLength(128).UseCollation("C");
        players.Property(row => row.ImageUrl).HasMaxLength(2048);
        players.HasOne<OrganizationRecord>().WithMany().HasForeignKey(row => row.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        players.HasOne<RosterTeamRecord>().WithMany().HasForeignKey(row => new { row.OrganizationId, row.TeamId })
            .HasPrincipalKey(row => new { row.OrganizationId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        players.HasOne<RosterAcademyGroupRecord>().WithMany().HasForeignKey(row => new { row.OrganizationId, row.PrimaryAcademyGroupId })
            .HasPrincipalKey(row => new { row.OrganizationId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        players.HasIndex(row => new { row.OrganizationId, row.HasExplicitPickerFields, row.TeamType, row.IsActive, row.IsArchived, row.IsTransferred, row.Number, row.Id });

        var memberships = model.Entity<RosterPlayerAcademyGroupRecord>();
        memberships.ToTable("player_academy_groups", "club");
        memberships.HasKey(row => new { row.OrganizationId, row.PlayerId, row.AcademyGroupId });
        memberships.Property(row => row.OrganizationId).HasMaxLength(32);
        memberships.Property(row => row.PlayerId).HasMaxLength(128).UseCollation("C");
        memberships.Property(row => row.AcademyGroupId).HasMaxLength(128).UseCollation("C");
        memberships.HasOne<RosterPlayerRecord>().WithMany().HasForeignKey(row => new { row.OrganizationId, row.PlayerId })
            .HasPrincipalKey(row => new { row.OrganizationId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        memberships.HasOne<RosterAcademyGroupRecord>().WithMany().HasForeignKey(row => new { row.OrganizationId, row.AcademyGroupId })
            .HasPrincipalKey(row => new { row.OrganizationId, row.Id }).OnDelete(DeleteBehavior.Restrict);

        var state = model.Entity<RosterImportStateRecord>();
        state.ToTable("roster_import_state", "club", table =>
        {
            table.HasCheckConstraint("ck_roster_initial_state", "id = 'initial' AND digest ~ '^[0-9a-f]{64}$' AND source_commit ~ '^[0-9a-f]{40}$' AND environment IN ('development','staging')");
            table.HasCheckConstraint("ck_roster_counts", "teams_count >= 0 AND academy_groups_count >= 0 AND players_count >= 0 AND memberships_count >= 0");
        });
        state.HasKey(row => row.Id);
        state.HasAlternateKey(row => row.Digest);
        state.Property(row => row.Id).HasMaxLength(16).ValueGeneratedNever();
        state.Property(row => row.Digest).HasMaxLength(64);
        state.Property(row => row.SourceCommit).HasMaxLength(40);
        state.Property(row => row.SourceRepository).HasMaxLength(100);
        state.Property(row => row.Environment).HasMaxLength(16);

        var documents = model.Entity<RosterSourceDocumentRecord>();
        documents.ToTable("roster_source_documents", "migration", table =>
            table.HasCheckConstraint("ck_roster_source_json", "collection IN ('teams','academy_groups','players') AND json_typeof(source_json::json) = 'object'"));
        documents.HasKey(row => new { row.Collection, row.SourceId });
        documents.Property(row => row.Collection).HasMaxLength(32);
        documents.Property(row => row.SourceId).HasMaxLength(128).UseCollation("C");
        documents.Property(row => row.ImportDigest).HasMaxLength(64);
        documents.Property(row => row.SourceJson).HasColumnType("text");
        documents.HasOne<RosterImportStateRecord>().WithMany().HasForeignKey(row => row.ImportDigest)
            .HasPrincipalKey(row => row.Digest).OnDelete(DeleteBehavior.Restrict);
    }
}
