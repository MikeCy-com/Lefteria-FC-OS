using System.Text.Json.Serialization;

namespace LefteriaFc.Application.Pos;

// A complete product/variant snapshot is the unit of concurrency. No client
// may override the owner, actor, audit, product ID or server-side version.
public sealed record AdminPosVariantCommand(string? Id, string? Name,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] int? CostCents,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] int? PriceCents);
public sealed record AdminPosProductCreateCommand(string? CategoryId, string? Name,
    string? ImageUrl, bool? Active, IReadOnlyList<AdminPosVariantCommand>? Variants);
public sealed record AdminPosProductUpdateCommand(string? CategoryId, string? Name,
    string? ImageUrl, bool? Active, IReadOnlyList<AdminPosVariantCommand>? Variants,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long? ExpectedVersion);
public interface IAdminPosProductWriter
{
    Task<AdminPosProductDto> CreateAsync(string cookie, string owner, AdminPosProductCreateCommand command, CancellationToken token);
    Task<AdminPosProductDto> UpdateAsync(string cookie, string owner, string id, AdminPosProductUpdateCommand command, CancellationToken token);
}
