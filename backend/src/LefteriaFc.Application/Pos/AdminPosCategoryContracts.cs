using System.Text.Json.Serialization;

namespace LefteriaFc.Application.Pos;

// Missing booleans are NOT silently interpreted as disabled products.
public sealed record AdminPosCategoryCreateCommand(string? Name, bool? Active);
public sealed record AdminPosCategoryUpdateCommand(string? Name, bool? Active,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long? ExpectedVersion);

public interface IAdminPosCategoryWriter
{
    Task<AdminPosCategoryDto> CreateAsync(string cookie, string owner, AdminPosCategoryCreateCommand command, CancellationToken token);
    Task<AdminPosCategoryDto> UpdateAsync(string cookie, string owner, string id, AdminPosCategoryUpdateCommand command, CancellationToken token);
}
