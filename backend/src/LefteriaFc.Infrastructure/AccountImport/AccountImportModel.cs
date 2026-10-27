using LefteriaFc.Infrastructure.Identity;
using LefteriaFc.Infrastructure.Roster;
using LefteriaFc.Infrastructure.StaffRelationships;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Infrastructure.AccountImport;

internal static partial class AccountImportModel
{
    public static void Configure(ModelBuilder model)
    {
        var state=model.Entity<AccountImportStateRecord>();
        state.ToTable("account_import_state","migration",table=>
        {
            table.HasCheckConstraint("ck_account_import_initial","id='initial' AND digest ~ '^[0-9a-f]{64}$' AND source_commit ~ '^[0-9a-f]{40}$' AND environment IN ('development','staging')");
            table.HasCheckConstraint("ck_account_import_counts","jsonb_typeof(counts_json)='object'");
            table.HasCheckConstraint("ck_account_manifest_bytes","octet_length(manifest_bytes) BETWEEN 1 AND 33554432");
        });
        state.HasKey(row=>row.Id);state.HasAlternateKey(row=>row.Digest);
        state.Property(row=>row.Id).HasMaxLength(16).ValueGeneratedNever();state.Property(row=>row.Digest).HasMaxLength(64);
        state.Property(row=>row.RosterDigest).HasMaxLength(64);state.Property(row=>row.StaffDigest).HasMaxLength(64);
        state.Property(row=>row.SourceCommit).HasMaxLength(40);state.Property(row=>row.Environment).HasMaxLength(16);state.Property(row=>row.CountsJson).HasColumnType("jsonb");
        state.Property(row=>row.ManifestBytes).HasColumnType("bytea");
        state.HasOne<RosterImportStateRecord>().WithMany().HasForeignKey(row=>row.RosterDigest).HasPrincipalKey(row=>row.Digest).OnDelete(DeleteBehavior.Restrict);
        state.HasOne<StaffRelationshipImportStateRecord>().WithMany().HasForeignKey(row=>row.StaffDigest).HasPrincipalKey(row=>row.Digest).OnDelete(DeleteBehavior.Restrict);
        var source=model.Entity<AccountSourceDocumentRecord>();
        source.ToTable("account_source_documents","migration",table=>
        {
            table.HasCheckConstraint("ck_account_source_collection","collection IN ('mobile_users','fans','admin_users') AND ordinal >= 0");
            table.HasCheckConstraint("ck_account_source_json","json_typeof(source_json::json)='object'");
            table.HasCheckConstraint("ck_account_source_disposition","(disposition='quarantined' AND target_id IS NULL) OR (disposition IN ('mobile','admin','fan_evidence') AND target_id IS NOT NULL)");
        });
        source.HasKey(row=>new{row.Collection,row.Ordinal});source.Property(row=>row.Collection).HasMaxLength(16);
        source.Property(row=>row.LegacyId).HasMaxLength(128).UseCollation("C");source.Property(row=>row.Disposition).HasMaxLength(16);
        source.Property(row=>row.ImportDigest).HasMaxLength(64);source.Property(row=>row.SourceJson).HasColumnType("text");
        source.HasOne<AccountImportStateRecord>().WithMany().HasForeignKey(row=>row.ImportDigest).HasPrincipalKey(row=>row.Digest).OnDelete(DeleteBehavior.Restrict);
        var issue=model.Entity<AccountImportIssueRecord>();
        issue.ToTable("account_import_issues","migration",table=>table.HasCheckConstraint("ck_account_issue_code","code ~ '^[a-z_]{1,80}$'"));
        issue.HasKey(row=>new{row.Collection,row.Ordinal,row.Code});issue.Property(row=>row.Collection).HasMaxLength(16);issue.Property(row=>row.Code).HasMaxLength(80);
        issue.HasOne<AccountSourceDocumentRecord>().WithMany().HasForeignKey(row=>new{row.Collection,row.Ordinal}).OnDelete(DeleteBehavior.Restrict);
        var fan=model.Entity<FanRegistrationEvidenceRecord>();
        fan.ToTable("fan_registration_evidence","identity",table=>table.HasCheckConstraint("ck_fan_evidence_phone","normalized_phone ~ '^[1-9][0-9]{7,14}$'"));
        fan.HasKey(row=>row.Id);fan.Property(row=>row.Id).ValueGeneratedNever();fan.Property(row=>row.LegacyId).HasMaxLength(128).UseCollation("C");
        fan.HasAlternateKey(row=>new{row.Id,row.NormalizedPhone});
        fan.Property(row=>row.NormalizedPhone).HasMaxLength(15);fan.HasIndex(row=>row.NormalizedPhone).IsUnique();
        fan.Property(row=>row.FirstName).HasMaxLength(100);fan.Property(row=>row.LastName).HasMaxLength(100);
        fan.Property(row=>row.FavouritePlayerId).HasMaxLength(128);fan.Property(row=>row.NotificationPreferencesJson).HasColumnType("jsonb");
        model.Entity<OtpChallengeRecord>().HasOne<FanRegistrationEvidenceRecord>().WithMany()
            .HasForeignKey(row=>new{row.PendingEvidenceId,row.NormalizedPhone})
            .HasPrincipalKey(row=>new{row.Id,row.NormalizedPhone}).OnDelete(DeleteBehavior.Restrict).IsRequired(false);
        model.Entity<OtpChallengeRecord>().ToTable(table=>table.HasCheckConstraint("ck_pending_evidence_fields",
            "pending_evidence_id IS NULL OR (pending_first_name IS NOT NULL AND pending_last_name IS NOT NULL AND pending_birthday IS NOT NULL)"));
        var mappings=model.Entity<LegacyAccountMappingRecord>();
        mappings.ToTable("legacy_account_mappings","migration",table=>table.HasCheckConstraint("ck_account_mapping_target",
            "(collection='mobile_users' AND disposition='mobile' AND mobile_identity_id IS NOT NULL AND mobile_identity_id=target_id AND admin_account_id IS NULL AND fan_evidence_id IS NULL) OR "+
            "(collection='admin_users' AND disposition='admin' AND admin_account_id IS NOT NULL AND admin_account_id=target_id AND mobile_identity_id IS NULL AND fan_evidence_id IS NULL) OR "+
            "(collection='fans' AND disposition='fan_evidence' AND fan_evidence_id IS NOT NULL AND fan_evidence_id=target_id AND mobile_identity_id IS NULL AND admin_account_id IS NULL)"));
        mappings.HasKey(row=>new{row.Collection,row.LegacyId});mappings.HasIndex(row=>new{row.Collection,row.TargetId}).IsUnique();
        mappings.Property(row=>row.Collection).HasMaxLength(16);mappings.Property(row=>row.LegacyId).HasMaxLength(128).UseCollation("C");mappings.Property(row=>row.Disposition).HasMaxLength(16);
        mappings.HasOne<AccountSourceDocumentRecord>().WithMany().HasForeignKey(row=>new{row.Collection,row.Ordinal}).OnDelete(DeleteBehavior.Restrict);
        mappings.HasOne<MobileIdentityRecord>().WithMany().HasForeignKey(row=>row.MobileIdentityId).OnDelete(DeleteBehavior.Restrict);
        mappings.HasOne<AdminAccountRecord>().WithMany().HasForeignKey(row=>row.AdminAccountId).OnDelete(DeleteBehavior.Restrict);
        mappings.HasOne<FanRegistrationEvidenceRecord>().WithMany().HasForeignKey(row=>row.FanEvidenceId).OnDelete(DeleteBehavior.Restrict);
        ConfigureLinks(model);
    }
}
