using LefteriaFc.Infrastructure.Identity;
using LefteriaFc.Infrastructure.Roster;
using LefteriaFc.Infrastructure.StaffRelationships;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Infrastructure.AccountImport;

internal static partial class AccountImportModel
{
    private static void ConfigureLinks(ModelBuilder model)
    {
        model.Entity<MobileIdentityRecord>().HasAlternateKey(row=>new{row.Id,row.NormalizedPhone,row.Role});
        var binding=model.Entity<CanonicalContactBindingRecord>();
        binding.ToTable("canonical_contact_bindings","identity",table=>
        {
            table.HasCheckConstraint("ck_canonical_binding_phone","normalized_phone ~ '^[1-9][0-9]{7,14}$'");
            table.HasCheckConstraint("ck_canonical_binding_origin","(collection='players' AND kind IN ('player_own','guardian') AND player_id IS NOT NULL AND player_id=source_id AND staff_id IS NULL) OR (collection='staff' AND kind='staff_own' AND staff_id IS NOT NULL AND staff_id=source_id AND player_id IS NULL)");
        });
        binding.HasKey(row=>new{row.Collection,row.SourceId,row.Kind});binding.HasAlternateKey(row=>new{row.Collection,row.SourceId,row.Kind,row.NormalizedPhone});
        binding.Property(row=>row.Collection).HasMaxLength(16);binding.Property(row=>row.SourceId).HasMaxLength(128).UseCollation("C");
        binding.Property(row=>row.Kind).HasMaxLength(16);binding.Property(row=>row.NormalizedPhone).HasMaxLength(15);
        binding.Property(row=>row.PlayerId).HasMaxLength(128).UseCollation("C");binding.Property(row=>row.StaffId).HasMaxLength(128).UseCollation("C");binding.Property(row=>row.ImportDigest).HasMaxLength(64);
        binding.HasOne<RosterPlayerRecord>().WithMany().HasForeignKey(row=>row.PlayerId).OnDelete(DeleteBehavior.Restrict);
        binding.HasOne<StaffRecord>().WithMany().HasForeignKey(row=>row.StaffId).OnDelete(DeleteBehavior.Restrict);
        binding.HasOne<AccountImportStateRecord>().WithMany().HasForeignKey(row=>row.ImportDigest).HasPrincipalKey(row=>row.Digest).OnDelete(DeleteBehavior.Restrict);
        var links=model.Entity<MobileRecordLinkRecord>();
        links.ToTable("mobile_record_links","identity",table=>table.HasCheckConstraint("ck_mobile_record_link_role",
            "(role='parent' AND collection='players' AND kind='guardian') OR (role='player' AND collection='players' AND kind='player_own') OR (role='coach' AND collection='staff' AND kind='staff_own')"));
        links.HasKey(row=>new{row.IdentityId,row.Kind,row.RecordId});
        links.Property(row=>row.Role).HasMaxLength(16);links.Property(row=>row.NormalizedPhone).HasMaxLength(15);
        links.Property(row=>row.Collection).HasMaxLength(16);links.Property(row=>row.RecordId).HasMaxLength(128).UseCollation("C");links.Property(row=>row.Kind).HasMaxLength(16);
        links.HasIndex(row=>row.IdentityId).IsUnique().HasDatabaseName("IX_mobile_record_link_primary").HasFilter("is_primary");
        links.HasIndex(row=>new{row.IdentityId,row.Role}).IsUnique().HasDatabaseName("IX_mobile_record_link_single_self").HasFilter("kind <> 'guardian'");
        links.HasOne<MobileIdentityRecord>().WithMany().HasForeignKey(row=>new{row.IdentityId,row.NormalizedPhone,row.Role})
            .HasPrincipalKey(row=>new{row.Id,row.NormalizedPhone,row.Role}).OnDelete(DeleteBehavior.Restrict);
        links.HasOne<CanonicalContactBindingRecord>().WithMany().HasForeignKey(row=>new{row.Collection,row.RecordId,row.Kind,row.NormalizedPhone})
            .HasPrincipalKey(row=>new{row.Collection,row.SourceId,row.Kind,row.NormalizedPhone}).OnDelete(DeleteBehavior.Restrict);
        var staff=model.Entity<AdminStaffLinkRecord>();
        staff.ToTable("admin_staff_links","identity");staff.HasKey(row=>row.AdminAccountId);staff.HasIndex(row=>row.StaffId).IsUnique();staff.Property(row=>row.StaffId).HasMaxLength(128).UseCollation("C");
        staff.HasOne<AdminAccountRecord>().WithMany().HasForeignKey(row=>row.AdminAccountId).OnDelete(DeleteBehavior.Restrict);
        staff.HasOne<StaffRecord>().WithMany().HasForeignKey(row=>row.StaffId).OnDelete(DeleteBehavior.Restrict);
        var provenance=model.Entity<ImportedAdminGrantRecord>();
        provenance.ToTable("imported_admin_grants","migration",table=>table.HasCheckConstraint("ck_imported_grant_origin",
            "provenance IN ('explicit_plural','explicit_singular','legacy_super_admin','coach_group_backfill','coach_team_backfill')"));
        provenance.HasKey(row=>new{row.AdminAccountId,row.OrganizationId});provenance.Property(row=>row.OrganizationId).HasMaxLength(32);provenance.Property(row=>row.Provenance).HasMaxLength(32);
        // Import evidence is historical; removing a current grant must not be
        // blocked by, or erase, its original provenance.
        provenance.HasOne<AdminAccountRecord>().WithMany().HasForeignKey(row=>row.AdminAccountId).OnDelete(DeleteBehavior.Restrict);
        provenance.HasOne<OrganizationRecord>().WithMany().HasForeignKey(row=>row.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}
