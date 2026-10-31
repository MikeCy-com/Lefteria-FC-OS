namespace LefteriaFc.Infrastructure.PublicContent;

public sealed class PublicContentDocumentRecord
{
    public string Kind { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string Id { get; set; } = "";
    public string DataJson { get; set; } = "{}";
    public int DisplayOrder { get; set; }
}
public sealed class PublicContentImportStateRecord
{
    public string Id { get; set; } = "initial";
    public string Digest { get; set; } = "";
    public string SourceRepository { get; set; } = "";
    public string SourceCommit { get; set; } = "";
    public string Environment { get; set; } = "";
    public int DocumentsCount { get; set; }
    public int SourcesCount { get; set; }
    public DateTimeOffset AppliedAt { get; set; }
}
public sealed class PublicContentImportEvidenceRecord
{
    public string Digest { get; set; } = "";
    public byte[] ManifestBytes { get; set; } = [];
    public override string ToString() => "Owner-only public-content import evidence";
}
public sealed class PublicContentSourceDocumentRecord
{
    public string Collection { get; set; } = "";
    public int Ordinal { get; set; }
    public string ImportDigest { get; set; } = "";
    public string SourceJson { get; set; } = "{}";
    public override string ToString() => "Owner-only public-content source document";
}
