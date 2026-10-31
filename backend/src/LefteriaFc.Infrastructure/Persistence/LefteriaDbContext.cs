using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using LefteriaFc.Domain.Organizations;
using LefteriaFc.Infrastructure.Identity;
using LefteriaFc.Infrastructure.Roster;
using LefteriaFc.Infrastructure.StaffRelationships;
using LefteriaFc.Infrastructure.AccountImport;
using LefteriaFc.Infrastructure.ClubCalendar;

namespace LefteriaFc.Infrastructure.Persistence;

public sealed class LefteriaDbContext(DbContextOptions<LefteriaDbContext> options)
    : DbContext(options)
{
    public DbSet<MobileIdentityRecord> MobileIdentities => Set<MobileIdentityRecord>();
    public DbSet<MobileAvatarRecord> MobileAvatars => Set<MobileAvatarRecord>();
    public DbSet<AdminAccountRecord> AdminAccounts => Set<AdminAccountRecord>();
    public DbSet<AdminOrganizationGrantRecord> AdminOrganizationGrants => Set<AdminOrganizationGrantRecord>();
    public DbSet<OrganizationRecord> Organizations => Set<OrganizationRecord>();
    public DbSet<OtpChallengeRecord> OtpChallenges => Set<OtpChallengeRecord>();
    public DbSet<AuthRateBucketRecord> AuthRateBuckets => Set<AuthRateBucketRecord>();
    public DbSet<SecurityAuditRecord> SecurityAuditEvents => Set<SecurityAuditRecord>();
    public DbSet<RosterTeamRecord> RosterTeams => Set<RosterTeamRecord>();
    public DbSet<RosterAcademyGroupRecord> RosterAcademyGroups => Set<RosterAcademyGroupRecord>();
    public DbSet<RosterPlayerRecord> RosterPlayers => Set<RosterPlayerRecord>();
    public DbSet<RosterPlayerAcademyGroupRecord> RosterPlayerAcademyGroups => Set<RosterPlayerAcademyGroupRecord>();
    public DbSet<RosterImportStateRecord> RosterImportStates => Set<RosterImportStateRecord>();
    public DbSet<RosterSourceDocumentRecord> RosterSourceDocuments => Set<RosterSourceDocumentRecord>();
    public DbSet<StaffRecord> Staff => Set<StaffRecord>();
    public DbSet<StaffTeamRecord> StaffTeams => Set<StaffTeamRecord>();
    public DbSet<StaffAcademyGroupRecord> StaffAcademyGroups => Set<StaffAcademyGroupRecord>();
    public DbSet<AcademyGroupCoachRecord> AcademyGroupCoaches => Set<AcademyGroupCoachRecord>();
    public DbSet<StaffRelationshipImportStateRecord> StaffRelationshipImportStates => Set<StaffRelationshipImportStateRecord>();
    public DbSet<StaffSourceDocumentRecord> StaffSourceDocuments => Set<StaffSourceDocumentRecord>();
    public DbSet<ContactProvenanceRecord> ContactProvenance => Set<ContactProvenanceRecord>();
    public DbSet<AccountImportStateRecord> AccountImportStates => Set<AccountImportStateRecord>();
    public DbSet<AccountSourceDocumentRecord> AccountSourceDocuments => Set<AccountSourceDocumentRecord>();
    public DbSet<AccountImportIssueRecord> AccountImportIssues => Set<AccountImportIssueRecord>();
    public DbSet<LegacyAccountMappingRecord> LegacyAccountMappings => Set<LegacyAccountMappingRecord>();
    public DbSet<FanRegistrationEvidenceRecord> FanRegistrationEvidence => Set<FanRegistrationEvidenceRecord>();
    public DbSet<CanonicalContactBindingRecord> CanonicalContactBindings => Set<CanonicalContactBindingRecord>();
    public DbSet<MobileRecordLinkRecord> MobileRecordLinks => Set<MobileRecordLinkRecord>();
    public DbSet<AdminStaffLinkRecord> AdminStaffLinks => Set<AdminStaffLinkRecord>();
    public DbSet<ImportedAdminGrantRecord> ImportedAdminGrants => Set<ImportedAdminGrantRecord>();
    public DbSet<CalendarItemRecord> CalendarItems => Set<CalendarItemRecord>();
    public DbSet<CalendarItemScopeRecord> CalendarItemScopes => Set<CalendarItemScopeRecord>();
    public DbSet<CalendarItemGuestRecord> CalendarItemGuests => Set<CalendarItemGuestRecord>();
    public DbSet<CalendarResponseRecord> CalendarResponses => Set<CalendarResponseRecord>();
    public DbSet<CalendarImportStateRecord> CalendarImportStates => Set<CalendarImportStateRecord>();
    public DbSet<CalendarSourceDocumentRecord> CalendarSourceDocuments => Set<CalendarSourceDocumentRecord>();
    public DbSet<CalendarImportIssueRecord> CalendarImportIssues => Set<CalendarImportIssueRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("identity");
        RosterModel.Configure(modelBuilder);
        LefteriaFc.Infrastructure.WebAdmin.WebAdminModel.ConfigureWebAdmin(modelBuilder);
        LefteriaFc.Infrastructure.PublicContent.PublicContentModel.ConfigurePublicContent(modelBuilder);
        StaffRelationshipModel.Configure(modelBuilder);
        CalendarModel.Configure(modelBuilder);
        var avatars = modelBuilder.Entity<MobileAvatarRecord>();
        avatars.ToTable("mobile_avatars", table =>
        {
            table.HasCheckConstraint("ck_avatar_filename", @"file_name ~ '^[0-9a-f]{32}_[0-9a-f]{64}\.jpg$'");
            table.HasCheckConstraint("ck_avatar_bytes", "octet_length(jpeg_bytes) BETWEEN 4 AND 1048576 AND substring(jpeg_bytes from 1 for 2) = decode('ffd8','hex') AND substring(jpeg_bytes from octet_length(jpeg_bytes)-1 for 2) = decode('ffd9','hex')");
            table.HasCheckConstraint("ck_avatar_dimensions", "width BETWEEN 1 AND 1024 AND height BETWEEN 1 AND 1024");
        });
        avatars.HasKey(x => x.IdentityId);
        avatars.Property(x => x.IdentityId).ValueGeneratedNever();
        avatars.Property(x => x.FileName).HasMaxLength(101).IsRequired();
        avatars.Property(x => x.JpegBytes).HasColumnType("bytea").IsRequired();
        avatars.HasIndex(x => x.FileName).IsUnique();
        avatars.HasOne<MobileIdentityRecord>().WithOne().HasForeignKey<MobileAvatarRecord>(x => x.IdentityId)
            .OnDelete(DeleteBehavior.Cascade);
        var identities = modelBuilder.Entity<MobileIdentityRecord>();
        identities.ToTable("mobile_identities", table =>
        {
            table.HasCheckConstraint("ck_mobile_identity_role", "role IN ('parent','coach','management','player','fan')");
            table.HasCheckConstraint("ck_mobile_identity_phone", "normalized_phone ~ '^[1-9][0-9]{7,14}$'");
            table.HasCheckConstraint("ck_management_identity_admin", "(role = 'management') = (management_admin_id IS NOT NULL)");
        });
        identities.HasKey(x => x.Id);
        identities.Property(x => x.Id).ValueGeneratedNever();
        identities.Property(x => x.NormalizedPhone).HasMaxLength(15).IsRequired();
        identities.Property(x => x.Role).HasMaxLength(16).IsRequired();
        identities.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        identities.Property(x => x.FirstName).HasMaxLength(100);
        identities.Property(x => x.LastName).HasMaxLength(100);
        identities.Property(x => x.Platform).HasMaxLength(24).IsRequired();
        identities.Property(x => x.FavoriteTeam).HasMaxLength(32);
        identities.Property(x => x.Email).HasMaxLength(254);
        identities.Property(x => x.FavouritePlayerId).HasMaxLength(128);
        identities.Property(x => x.NotificationPreferencesJson).HasColumnType("jsonb");
        identities.HasIndex(x => new { x.NormalizedPhone, x.Role }).IsUnique();
        identities.HasOne(x => x.ManagementAdmin).WithMany().HasForeignKey(x => x.ManagementAdminId)
            .OnDelete(DeleteBehavior.Restrict);

        var admins = modelBuilder.Entity<AdminAccountRecord>();
        admins.ToTable("admin_accounts", table => table.HasCheckConstraint(
            "ck_admin_account_role", "role IN ('super_admin','manager','editor','marketing','coach','matchday_editor')"));
        admins.HasKey(x => x.Id);
        admins.Property(x => x.Id).ValueGeneratedNever();
        admins.Property(x => x.Username).HasMaxLength(80).UseCollation("C").IsRequired();
        admins.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        admins.Property(x => x.Role).HasMaxLength(24).IsRequired();
        admins.Property(x => x.ActiveOrganizationId).HasMaxLength(32);
        admins.Property(x => x.NormalizedPhone).HasMaxLength(15);
        admins.ToTable(table => table.HasCheckConstraint("ck_admin_normalized_phone", "normalized_phone IS NULL OR normalized_phone ~ '^[1-9][0-9]{7,14}$'"));
        admins.ToTable(table => table.HasCheckConstraint("ck_admin_pending_inactive", "NOT invite_pending OR NOT is_active"));
        admins.HasIndex(x => x.Username).IsUnique();

        var organizations = modelBuilder.Entity<OrganizationRecord>();
        organizations.ToTable("organizations", "club");
        organizations.HasKey(x => x.Id);
        organizations.Property(x => x.Id).HasMaxLength(32).ValueGeneratedNever();
        organizations.Property(x => x.Name).HasMaxLength(120).IsRequired();
        organizations.HasData(
            new OrganizationRecord { Id = OrganizationIds.Club, Name = "Lefteria FC" },
            new OrganizationRecord { Id = OrganizationIds.Academy, Name = "Lefteria FC Academy" });

        var grants = modelBuilder.Entity<AdminOrganizationGrantRecord>();
        grants.ToTable("admin_organization_grants");
        grants.HasKey(x => new { x.AdminAccountId, x.OrganizationId });
        grants.Property(x => x.OrganizationId).HasMaxLength(32);
        grants.HasOne(x => x.AdminAccount).WithMany(x => x.OrganizationGrants)
            .HasForeignKey(x => x.AdminAccountId).OnDelete(DeleteBehavior.Restrict);
        grants.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        // Active context must reference an assignment, including under concurrent changes.
        admins.HasOne<AdminOrganizationGrantRecord>().WithMany()
            .HasForeignKey(x => new { x.Id, x.ActiveOrganizationId })
            .HasPrincipalKey(x => new { x.AdminAccountId, x.OrganizationId })
            .OnDelete(DeleteBehavior.Restrict).IsRequired(false);

        AccountImportModel.Configure(modelBuilder);
        var challenges = modelBuilder.Entity<OtpChallengeRecord>();
        challenges.ToTable("otp_challenges", table =>
        {
            table.HasCheckConstraint("ck_otp_digest", "octet_length(code_digest) = 32");
            table.HasCheckConstraint("ck_otp_calls", "verification_calls BETWEEN 0 AND 5");
            table.HasCheckConstraint("ck_otp_expiry", "expires_at > created_at AND expires_at <= created_at + interval '5 minutes'");
            table.HasCheckConstraint("ck_otp_phone", "normalized_phone ~ '^[1-9][0-9]{7,14}$'");
            table.HasCheckConstraint("ck_pending_registration", "(pending_first_name IS NULL AND pending_last_name IS NULL AND pending_birthday IS NULL) OR (pending_first_name IS NOT NULL AND pending_last_name IS NOT NULL AND pending_birthday IS NOT NULL)");
        });
        challenges.HasKey(x => x.Id);
        challenges.Property(x => x.Id).ValueGeneratedNever();
        challenges.Property(x => x.NormalizedPhone).HasMaxLength(15).IsRequired();
        challenges.Property(x => x.CodeDigest).IsRequired();
        challenges.Property(x => x.PendingFirstName).HasMaxLength(100);
        challenges.Property(x => x.PendingLastName).HasMaxLength(100);
        challenges.HasIndex(x => x.NormalizedPhone).IsUnique();
        challenges.HasIndex(x => x.ExpiresAt);

        var buckets = modelBuilder.Entity<AuthRateBucketRecord>();
        buckets.ToTable("auth_rate_buckets", table => table.HasCheckConstraint("ck_rate_tokens", "tokens >= 0"));
        buckets.HasKey(x => x.Key);
        buckets.Property(x => x.Key).HasMaxLength(64);
        buckets.HasIndex(x => x.UpdatedAt);

        var audit = modelBuilder.Entity<SecurityAuditRecord>();
        audit.ToTable("security_events", "audit");
        audit.HasKey(x => x.Id);
        audit.Property(x => x.Id).ValueGeneratedNever();
        audit.Property(x => x.Action).HasMaxLength(80).IsRequired();
        audit.Property(x => x.Outcome).HasMaxLength(32).IsRequired();
        audit.Property(x => x.SubjectDigest).HasMaxLength(64);
        audit.Property(x => x.ReasonCode).HasMaxLength(80);
        audit.Property(x => x.OrganizationId).HasMaxLength(32);
        audit.HasIndex(x => x.OccurredAt);

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name));
            }
        }
        identities.Property(x => x.NotificationPreferencesJson).HasColumnName("notification_preferences");
    }
}
