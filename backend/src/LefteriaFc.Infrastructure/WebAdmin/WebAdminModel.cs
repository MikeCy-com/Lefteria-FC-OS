using LefteriaFc.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Infrastructure.WebAdmin;

public sealed class WebAdminSessionRecord
{
    public Guid Id { get; set; }
    public string TokenDigest { get; set; } = "";
    public Guid AdminAccountId { get; set; }
    public string CredentialDigest { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class WebAdminRateBucketRecord
{
    public string Key { get; set; } = "";
    public double Tokens { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class WebAdminModel
{
    public static void ConfigureWebAdmin(ModelBuilder model)
    {
        var session = model.Entity<WebAdminSessionRecord>();
        session.ToTable("admin_sessions", "identity", table =>
        {
            table.HasCheckConstraint("ck_admin_session_digests", "token_digest ~ '^[0-9a-f]{64}$' AND credential_digest ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_admin_session_expiry", "expires_at > created_at AND expires_at <= created_at + interval '90 days'");
            table.HasCheckConstraint("ck_admin_session_revocation", "revoked_at IS NULL OR revoked_at >= created_at");
        });
        session.HasKey(row => row.Id);
        session.Property(row => row.Id).ValueGeneratedNever();
        session.Property(row => row.TokenDigest).HasMaxLength(64);
        session.Property(row => row.CredentialDigest).HasMaxLength(64);
        session.HasIndex(row => row.TokenDigest).IsUnique();
        session.HasIndex(row => row.ExpiresAt);
        session.HasIndex(row => row.AdminAccountId);
        session.HasOne<AdminAccountRecord>().WithMany().HasForeignKey(row => row.AdminAccountId).OnDelete(DeleteBehavior.Restrict);
        var buckets = model.Entity<WebAdminRateBucketRecord>();
        buckets.ToTable("admin_auth_rate_buckets", "identity", table => table.HasCheckConstraint("ck_admin_auth_rate_tokens", "tokens >= 0 AND tokens <= 10"));
        buckets.HasKey(row => row.Key);
        buckets.Property(row => row.Key).HasMaxLength(64);
        buckets.HasIndex(row => row.UpdatedAt);
        model.Entity<SecurityAuditRecord>().Property(row => row.ActorRealm).HasMaxLength(16).HasDefaultValue("mobile");
        model.Entity<SecurityAuditRecord>().ToTable("security_events", "audit", table =>
            table.HasCheckConstraint("ck_audit_actor_realm", "actor_realm IN ('mobile','admin')"));
    }
}
