using LefteriaFc.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Infrastructure.PublicContent;

public sealed class PublicSectionsSnapshotRecord
{
    public int Version { get; set; }
    public string Digest { get; set; } = "";
    public string BasePublicDigest { get; set; } = "";
    public string SourceRepository { get; set; } = "";
    public string SourceCommit { get; set; } = "";
    public string Environment { get; set; } = "";
    public DateTimeOffset ExportedAt { get; set; }
    public DateTimeOffset AppliedAt { get; set; }
    public int DocumentsCount { get; set; }
    public int SourcesCount { get; set; }
    public int IssuesCount { get; set; }
}
public sealed class PublicSectionsCurrentRecord
{
    public string Id { get; set; } = "current";
    public int SnapshotVersion { get; set; }
}
public sealed class PublicSectionsDocumentRecord
{
    public int SnapshotVersion { get; set; }
    public string Kind { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string Id { get; set; } = "";
    public string DataJson { get; set; } = "{}";
    public int DisplayOrder { get; set; }
}
public sealed class PublicSectionsSourceRecord
{
    public int SnapshotVersion { get; set; }
    public string Collection { get; set; } = "";
    public int Ordinal { get; set; }
    public string SourceJson { get; set; } = "{}";
    public override string ToString() => "Owner-only public-sections source";
}
public sealed class PublicSectionsEvidenceRecord
{
    public string Digest { get; set; } = "";
    public byte[] ManifestBytes { get; set; } = [];
    public override string ToString() => "Owner-only public-sections evidence";
}
public sealed class PublicSectionsIssueRecord
{
    public int SnapshotVersion { get; set; }
    public string Collection { get; set; } = "";
    public int Ordinal { get; set; }
    public string Code { get; set; } = "";
}

public static class PublicSectionsModel
{
    public static void Configure(ModelBuilder model)
    {
        var snapshots = model.Entity<PublicSectionsSnapshotRecord>();
        snapshots.ToTable("snapshots", "public_sections", table =>
        {
            table.HasCheckConstraint("ck_public_sections_snapshot", "version BETWEEN 1 AND 100000 AND digest ~ '^[0-9a-f]{64}$' AND source_commit ~ '^[0-9a-f]{40}$' AND environment IN ('development','staging')");
            table.HasCheckConstraint("ck_public_sections_counts", "documents_count >= 0 AND sources_count >= 0 AND issues_count >= 0");
        });
        snapshots.HasKey(row => row.Version);
        snapshots.Property(row => row.Version).ValueGeneratedNever();
        snapshots.HasAlternateKey(row => row.Digest);
        snapshots.Property(row => row.Digest).HasMaxLength(64);
        snapshots.Property(row => row.BasePublicDigest).HasMaxLength(64);
        snapshots.Property(row => row.SourceRepository).HasMaxLength(100);
        snapshots.Property(row => row.SourceCommit).HasMaxLength(40);
        snapshots.Property(row => row.Environment).HasMaxLength(16);
        snapshots.HasOne<PublicContentImportStateRecord>().WithMany().HasForeignKey(row => row.BasePublicDigest)
            .HasPrincipalKey(row => row.Digest).OnDelete(DeleteBehavior.Restrict);
        var current = model.Entity<PublicSectionsCurrentRecord>();
        current.ToTable("current", "public_sections", table => table.HasCheckConstraint("ck_public_sections_current", "id = 'current'"));
        current.HasKey(row => row.Id); current.Property(row => row.Id).HasMaxLength(16).ValueGeneratedNever();
        current.HasOne<PublicSectionsSnapshotRecord>().WithMany().HasForeignKey(row => row.SnapshotVersion).OnDelete(DeleteBehavior.Restrict);
        var docs = model.Entity<PublicSectionsDocumentRecord>();
        docs.ToTable("documents", "public_sections", table =>
        {
            table.HasCheckConstraint("ck_public_sections_kind", "kind IN ('player','staff','academy_group','standing','venue','season','gallery','sponsor','partner','event','product','trials','fixture','ticket','archive','review','academy_stats','potm','branding','module')");
            table.HasCheckConstraint("ck_public_sections_identity", "(jsonb_typeof(data_json) = 'object' AND data_json->>'id' = id AND data_json->>'organization_id' = organization_id) IS TRUE");
        });
        docs.HasKey(row => new { row.SnapshotVersion, row.Kind, row.OrganizationId, row.Id });
        docs.Property(row => row.Kind).HasMaxLength(24); docs.Property(row => row.OrganizationId).HasMaxLength(32);
        docs.Property(row => row.Id).HasMaxLength(128).UseCollation("C").ValueGeneratedNever(); docs.Property(row => row.DataJson).HasColumnType("jsonb");
        docs.HasOne<PublicSectionsSnapshotRecord>().WithMany().HasForeignKey(row => row.SnapshotVersion).OnDelete(DeleteBehavior.Restrict);
        docs.HasOne<OrganizationRecord>().WithMany().HasForeignKey(row => row.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        var source = model.Entity<PublicSectionsSourceRecord>();
        source.ToTable("public_sections_source", "migration", table => table.HasCheckConstraint("ck_public_sections_source", "ordinal >= 0 AND json_typeof(source_json::json) = 'object' AND collection IN ('players','staff','standings','venues','seasons','gallery','sponsors','partners','marketing_events','products','trials_settings','fixtures','teams','academy_groups','tickets','archived_seasons','reviews','training_sessions','attendance','potm_votes','club_profile')"));
        source.HasKey(row => new { row.SnapshotVersion, row.Collection, row.Ordinal });
        source.Property(row => row.Collection).HasMaxLength(32); source.Property(row => row.SourceJson).HasColumnType("text");
        source.HasOne<PublicSectionsSnapshotRecord>().WithMany().HasForeignKey(row => row.SnapshotVersion).OnDelete(DeleteBehavior.Restrict);
        var evidence = model.Entity<PublicSectionsEvidenceRecord>();
        evidence.ToTable("public_sections_evidence", "migration", table => table.HasCheckConstraint("ck_public_sections_evidence", "octet_length(manifest_bytes) BETWEEN 1 AND 33554432 AND encode(sha256(manifest_bytes),'hex') = digest"));
        evidence.HasKey(row => row.Digest); evidence.Property(row => row.Digest).HasMaxLength(64); evidence.Property(row => row.ManifestBytes).HasColumnType("bytea");
        evidence.HasOne<PublicSectionsSnapshotRecord>().WithOne().HasForeignKey<PublicSectionsEvidenceRecord>(row => row.Digest).HasPrincipalKey<PublicSectionsSnapshotRecord>(row => row.Digest).OnDelete(DeleteBehavior.Restrict);
        var issues = model.Entity<PublicSectionsIssueRecord>();
        issues.ToTable("public_sections_issues", "migration");
        issues.HasKey(row => new { row.SnapshotVersion, row.Collection, row.Ordinal, row.Code });
        issues.Property(row => row.Collection).HasMaxLength(32); issues.Property(row => row.Code).HasMaxLength(64);
        issues.HasOne<PublicSectionsSourceRecord>().WithMany().HasForeignKey(row => new { row.SnapshotVersion, row.Collection, row.Ordinal }).OnDelete(DeleteBehavior.Restrict);
    }
}
