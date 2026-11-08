namespace LefteriaFc.Application.Pos;

public sealed record AdminPosCategoryDto(string Id, string Name, bool Active, long Version);
public sealed record AdminPosVariantDto(string Id, string Name, int CostCents, int PriceCents);
public sealed record AdminPosProductDto(string Id, string CategoryId, string Name, string? ImageUrl,
    bool Active, long Version, IReadOnlyList<AdminPosVariantDto> Variants);
public sealed record AdminPosCatalogDto(string OrganizationId,
    IReadOnlyList<AdminPosCategoryDto> Categories, IReadOnlyList<AdminPosProductDto> Products);

public interface IAdminPosCatalogReader
{
    Task<AdminPosCatalogDto> ReadAsync(string opaqueCookie, string organizationId, CancellationToken cancellationToken);
}
