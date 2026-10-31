using System.Text.Json;

namespace LefteriaFc.Application.PublicContent;

public sealed record PublicContentDocument(string Kind, string OrganizationId, string Id, string DataJson, int DisplayOrder);
public sealed record PublicContentSource(string Collection, int Ordinal, string Json)
{
    public override string ToString() => "Owner-only public-content source document";
}
public sealed record ValidatedPublicContentImport(string Digest, string SourceCommit, byte[] ManifestBytes,
    IReadOnlyList<PublicContentDocument> Documents, IReadOnlyList<PublicContentSource> Source)
{
    public const string Repository = "MikeCy-com/Lefteria-FC";
    public override string ToString() => "Validated public-content import (private source retained)";
}
public sealed record PublicContentImportResult(bool Applied, bool Noop, int Documents, int SourceRows);

public static partial class PublicContentImportValidator
{
    public const int MaximumBytes = 32 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private static readonly string[] Collections = ["club_profile", "news", "teams", "academy_groups", "fixtures"];
}
