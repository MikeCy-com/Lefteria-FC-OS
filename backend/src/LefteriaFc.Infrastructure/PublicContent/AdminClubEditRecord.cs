using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Infrastructure.PublicContent;

public sealed class AdminClubEditRecord
{
    public string Kind { get; set; } = "club";
    public string OrganizationId { get; set; } = "";
    public string Id { get; set; } = "";
    public string DataJson { get; set; } = "{}";
    public long Version { get; set; }
}

public static class AdminClubModel
{
    public static void Configure(ModelBuilder model)
    {
        var edits = model.Entity<AdminClubEditRecord>();
        edits.ToTable("edits", "public_content", table =>
        {
            table.HasCheckConstraint("ck_public_club_edit", "kind = 'club' AND version > 0");
            table.HasCheckConstraint("ck_public_club_edit_identity", "(jsonb_typeof(data_json) = 'object' AND data_json->>'id' = id AND data_json->>'organization_id' = organization_id) IS TRUE");
        });
        edits.HasKey(row => new { row.Kind, row.OrganizationId, row.Id });
        edits.Property(row => row.Kind).HasMaxLength(24);
        edits.Property(row => row.OrganizationId).HasMaxLength(32);
        edits.Property(row => row.Id).HasMaxLength(128).UseCollation("C").ValueGeneratedNever();
        edits.Property(row => row.DataJson).HasColumnType("jsonb");
        edits.HasOne<PublicContentDocumentRecord>().WithMany().HasForeignKey(row => new { row.Kind, row.OrganizationId, row.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
