using LefteriaFc.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Infrastructure.PublicContent;

public static class PublicContentModel
{
    public static void ConfigurePublicContent(ModelBuilder model)
    {
        var documents = model.Entity<PublicContentDocumentRecord>();
        documents.ToTable("documents", "public_content", table =>
        {
            table.HasCheckConstraint("ck_public_content_kind", "kind IN ('club','team','academy_group','news','fixture')");
            table.HasCheckConstraint("ck_public_content_projection", "(jsonb_typeof(data_json) = 'object' AND data_json->>'organization_id' = organization_id AND data_json->>'id' = id) IS TRUE");
        });
        documents.HasKey(row => new { row.Kind, row.OrganizationId, row.Id });
        documents.Property(row => row.Kind).HasMaxLength(24);
        documents.Property(row => row.OrganizationId).HasMaxLength(32);
        documents.Property(row => row.Id).HasMaxLength(128).UseCollation("C").ValueGeneratedNever();
        documents.Property(row => row.DataJson).HasColumnType("jsonb");
        documents.HasOne<OrganizationRecord>().WithMany().HasForeignKey(row => row.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        documents.HasIndex(row => new { row.OrganizationId, row.Kind, row.DisplayOrder });

        var state = model.Entity<PublicContentImportStateRecord>();
        state.ToTable("import_state", "public_content", table =>
        {
            table.HasCheckConstraint("ck_public_content_state", "id = 'initial' AND digest ~ '^[0-9a-f]{64}$' AND source_commit ~ '^[0-9a-f]{40}$' AND environment IN ('development','staging')");
            table.HasCheckConstraint("ck_public_content_counts", "documents_count >= 0 AND sources_count >= 0");
        });
        state.HasKey(row => row.Id);
        state.HasAlternateKey(row => row.Digest);
        state.Property(row => row.Id).HasMaxLength(16).ValueGeneratedNever();
        state.Property(row => row.Digest).HasMaxLength(64);
        state.Property(row => row.SourceRepository).HasMaxLength(100);
        state.Property(row => row.SourceCommit).HasMaxLength(40);
        state.Property(row => row.Environment).HasMaxLength(16);

        var evidence = model.Entity<PublicContentImportEvidenceRecord>();
        evidence.ToTable("public_content_import_evidence", "migration", table =>
            table.HasCheckConstraint("ck_public_content_manifest_bytes", "octet_length(manifest_bytes) BETWEEN 1 AND 33554432"));
        evidence.HasKey(row => row.Digest);
        evidence.Property(row => row.Digest).HasMaxLength(64);
        evidence.Property(row => row.ManifestBytes).HasColumnType("bytea");
        evidence.HasOne<PublicContentImportStateRecord>().WithOne().HasForeignKey<PublicContentImportEvidenceRecord>(row => row.Digest)
            .HasPrincipalKey<PublicContentImportStateRecord>(row => row.Digest).OnDelete(DeleteBehavior.Restrict);

        var source = model.Entity<PublicContentSourceDocumentRecord>();
        source.ToTable("public_content_source_documents", "migration", table =>
            table.HasCheckConstraint("ck_public_content_source", "collection IN ('club_profile','news','teams','academy_groups','fixtures') AND ordinal >= 0 AND json_typeof(source_json::json) = 'object'"));
        source.HasKey(row => new { row.Collection, row.Ordinal });
        source.Property(row => row.Collection).HasMaxLength(32);
        source.Property(row => row.ImportDigest).HasMaxLength(64);
        source.Property(row => row.SourceJson).HasColumnType("text");
        source.HasOne<PublicContentImportStateRecord>().WithMany().HasForeignKey(row => row.ImportDigest)
            .HasPrincipalKey(row => row.Digest).OnDelete(DeleteBehavior.Restrict);
    }
}
