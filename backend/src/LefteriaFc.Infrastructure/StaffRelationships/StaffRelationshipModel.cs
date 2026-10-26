using LefteriaFc.Infrastructure.Roster;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Infrastructure.StaffRelationships;

internal static class StaffRelationshipModel
{
    public static void Configure(ModelBuilder model)
    {
        var staff = model.Entity<StaffRecord>();
        staff.ToTable("staff", "club", table =>
        {
            table.HasCheckConstraint("ck_staff_job_role", "job_role IN ('Head Coach','Assistant Coach','Goalkeeper Coach','Fitness Coach','Physiotherapist','Team Manager','Youth Coach','Scout')");
            table.HasCheckConstraint("ck_staff_team_type", "team_type IN ('First Team','Academy')");
        });
        staff.HasKey(row => row.Id);
        staff.Property(row => row.Id).HasMaxLength(128).UseCollation("C").ValueGeneratedNever();
        staff.Property(row => row.Name).HasMaxLength(200);
        staff.Property(row => row.JobRole).HasMaxLength(64);
        staff.Property(row => row.TeamType).HasMaxLength(16);
        var teams = model.Entity<StaffTeamRecord>();
        teams.ToTable("staff_teams", "club", table => table.HasCheckConstraint("ck_staff_team_origin", "from_plural OR from_singular"));
        teams.HasKey(row => new { row.StaffId, row.TeamId });
        teams.Property(row => row.StaffId).HasMaxLength(128).UseCollation("C");
        teams.Property(row => row.TeamId).HasMaxLength(128).UseCollation("C");
        teams.Property(row => row.OrganizationId).HasMaxLength(32);
        teams.HasOne<StaffRecord>().WithMany().HasForeignKey(row => row.StaffId).OnDelete(DeleteBehavior.Restrict);
        teams.HasOne<RosterTeamRecord>().WithMany().HasForeignKey(row => new { row.OrganizationId, row.TeamId })
            .HasPrincipalKey(row => new { row.OrganizationId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        var groups = model.Entity<StaffAcademyGroupRecord>();
        groups.ToTable("staff_academy_groups", "club", table => table.HasCheckConstraint("ck_staff_group_origin", "from_plural OR from_singular OR from_group_coach"));
        groups.HasKey(row => new { row.StaffId, row.AcademyGroupId });
        groups.Property(row => row.StaffId).HasMaxLength(128).UseCollation("C");
        groups.Property(row => row.AcademyGroupId).HasMaxLength(128).UseCollation("C");
        groups.Property(row => row.OrganizationId).HasMaxLength(32);
        groups.HasOne<StaffRecord>().WithMany().HasForeignKey(row => row.StaffId).OnDelete(DeleteBehavior.Restrict);
        groups.HasOne<RosterAcademyGroupRecord>().WithMany().HasForeignKey(row => new { row.OrganizationId, row.AcademyGroupId })
            .HasPrincipalKey(row => new { row.OrganizationId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        var coaches = model.Entity<AcademyGroupCoachRecord>();
        coaches.ToTable("academy_group_coaches", "club");
        coaches.HasKey(row => row.AcademyGroupId);
        coaches.Property(row => row.AcademyGroupId).HasMaxLength(128).UseCollation("C").ValueGeneratedNever();
        coaches.Property(row => row.StaffId).HasMaxLength(128).UseCollation("C");
        coaches.Property(row => row.OrganizationId).HasMaxLength(32);
        coaches.HasOne<StaffRecord>().WithMany().HasForeignKey(row => row.StaffId).OnDelete(DeleteBehavior.Restrict);
        coaches.HasOne<RosterAcademyGroupRecord>().WithMany().HasForeignKey(row => new { row.OrganizationId, row.AcademyGroupId })
            .HasPrincipalKey(row => new { row.OrganizationId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        var state = model.Entity<StaffRelationshipImportStateRecord>();
        state.ToTable("staff_relationship_import_state", "migration", table =>
        {
            table.HasCheckConstraint("ck_staff_initial_state", "id = 'initial' AND digest ~ '^[0-9a-f]{64}$' AND source_commit ~ '^[0-9a-f]{40}$' AND environment IN ('development','staging')");
            table.HasCheckConstraint("ck_staff_import_counts", "staff_count >= 0 AND team_assignments_count >= 0 AND group_assignments_count >= 0 AND group_coaches_count >= 0 AND contacts_count >= 0");
        });
        state.HasKey(row => row.Id);
        state.HasAlternateKey(row => row.Digest);
        state.Property(row => row.Id).HasMaxLength(16).ValueGeneratedNever();
        state.Property(row => row.Digest).HasMaxLength(64);
        state.Property(row => row.RosterDigest).HasMaxLength(64);
        state.Property(row => row.SourceCommit).HasMaxLength(40);
        state.Property(row => row.Environment).HasMaxLength(16);
        state.HasOne<RosterImportStateRecord>().WithMany().HasForeignKey(row => row.RosterDigest)
            .HasPrincipalKey(row => row.Digest).OnDelete(DeleteBehavior.Restrict);
        var documents = model.Entity<StaffSourceDocumentRecord>();
        documents.ToTable("staff_source_documents", "migration", table => table.HasCheckConstraint("ck_staff_source_json", "json_typeof(source_json::json) = 'object'"));
        documents.HasKey(row => row.SourceId);
        documents.Property(row => row.SourceId).HasMaxLength(128).UseCollation("C").ValueGeneratedNever();
        documents.Property(row => row.ImportDigest).HasMaxLength(64);
        documents.Property(row => row.SourceJson).HasColumnType("text");
        documents.HasOne<StaffRecord>().WithMany().HasForeignKey(row => row.SourceId).OnDelete(DeleteBehavior.Restrict);
        documents.HasOne<StaffRelationshipImportStateRecord>().WithMany().HasForeignKey(row => row.ImportDigest)
            .HasPrincipalKey(row => row.Digest).OnDelete(DeleteBehavior.Restrict);
        var contacts = model.Entity<ContactProvenanceRecord>();
        contacts.ToTable("contact_provenance", "migration", table =>
        {
            table.HasCheckConstraint("ck_contact_source", "(source_collection = 'players' AND player_id = source_id AND player_id IS NOT NULL AND staff_id IS NULL AND ((field = 'phone' AND kind = 'player_own') OR (field IN ('parent_phone','parent_email','parent_name') AND kind = 'guardian'))) OR (source_collection = 'staff' AND staff_id = source_id AND staff_id IS NOT NULL AND player_id IS NULL AND field IN ('phone','email') AND kind = 'staff_own')");
            table.HasCheckConstraint("ck_contact_presence", "(presence IN ('missing','null') AND raw_value IS NULL) OR (presence = 'empty' AND raw_value IS NOT NULL AND raw_value = '') OR (presence = 'value' AND raw_value IS NOT NULL AND length(raw_value) > 0)");
        });
        contacts.HasKey(row => new { row.SourceCollection, row.SourceId, row.Field });
        contacts.Property(row => row.SourceCollection).HasMaxLength(16);
        contacts.Property(row => row.SourceId).HasMaxLength(128).UseCollation("C");
        contacts.Property(row => row.PlayerId).HasMaxLength(128).UseCollation("C");
        contacts.Property(row => row.StaffId).HasMaxLength(128).UseCollation("C");
        contacts.Property(row => row.Field).HasMaxLength(32);
        contacts.Property(row => row.Kind).HasMaxLength(16);
        contacts.Property(row => row.Presence).HasMaxLength(8);
        contacts.Property(row => row.RawValue).HasMaxLength(320);
        contacts.Property(row => row.SourceDigest).HasMaxLength(64);
        contacts.Property(row => row.ImportDigest).HasMaxLength(64);
        contacts.HasOne<RosterPlayerRecord>().WithMany().HasForeignKey(row => row.PlayerId).OnDelete(DeleteBehavior.Restrict);
        contacts.HasOne<StaffRecord>().WithMany().HasForeignKey(row => row.StaffId).OnDelete(DeleteBehavior.Restrict);
        contacts.HasOne<StaffRelationshipImportStateRecord>().WithMany().HasForeignKey(row => row.ImportDigest)
            .HasPrincipalKey(row => row.Digest).OnDelete(DeleteBehavior.Restrict);
    }
}
