using System.Text.Json;
using LefteriaFc.Application.Pos;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;

namespace LefteriaFc.Infrastructure.Pos;

/// <summary>
/// Exactly one audited SECURITY DEFINER call. Web admin credentials cannot
/// directly mutate POS categories, products, or variants.
/// </summary>
public sealed class PostgresAdminPosCategoryWriter(WebAdminConnections connections, IWebAdminSecrets secrets)
    : IAdminPosCategoryWriter
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public Task<AdminPosCategoryDto> CreateAsync(string cookie, string owner,
        AdminPosCategoryCreateCommand command, CancellationToken ct)
    {
        Validate(command.Name, command.Active, -1);
        return ExecuteAsync(cookie, owner, "cat_" + Guid.NewGuid().ToString("N"),
            command.Name!, command.Active!.Value, -1, ct);
    }

    public Task<AdminPosCategoryDto> UpdateAsync(string cookie, string owner, string id,
        AdminPosCategoryUpdateCommand command, CancellationToken ct)
    {
        Validate(command.Name, command.Active, command.ExpectedVersion);
        if (id is not {Length: >= 1 and <= 80} || !id.All(c =>
                c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-'))
            throw new WebAdminException(400, "invalid_category", "The category ID is invalid.");
        return ExecuteAsync(cookie, owner, id, command.Name!, command.Active!.Value,
            command.ExpectedVersion!.Value, ct);
    }

    private static void Validate(string? name, bool? active, long? version)
    {
        if (name is not {Length: >= 1 and <= 100} || name != name.Trim() ||
            name.Any(char.IsControl) || active is null || version is null ||
            version < -1 || version > 9007199254740991L)
            throw new WebAdminException(422, "invalid_category", "A valid name, state and version are required.");
    }

    private async Task<AdminPosCategoryDto> ExecuteAsync(string cookie, string owner,
        string id, string name, bool active, long version, CancellationToken ct)
    {
        if (!secrets.IsToken(cookie))
            throw new WebAdminException(401, "not_authenticated", "Sign in again.");
        if (owner is not ("lefteria_fc" or "lefteria_fc_academy"))
            throw new WebAdminException(400, "invalid_organization", "Choose an assigned organization.");

        try
        {
            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand("""
                SELECT pos.write_admin_category(
                  @digest,@owner,@id,@name,@active,@expected_version
                )::text
                """, connection);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner", owner);
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("name", name);
            command.Parameters.AddWithValue("active", active);
            command.Parameters.AddWithValue("expected_version", version);
            var result = await command.ExecuteScalarAsync(ct) as string;
            var category = result is null ? null : JsonSerializer.Deserialize<AdminPosCategoryDto>(result, Json);
            if (category is null || category.Id != id || category.Name != name ||
                category.Active != active || category.Version < 0)
                throw new WebAdminException(503, "pos_category_unavailable", "The category write could not be verified.");
            return category;
        }
        catch (PostgresException e) when (e.SqlState == "P0001" && e.MessageText == "pos_category_version_conflict")
        { throw new WebAdminException(409, "pos_category_version_conflict", "The category changed; refresh before saving."); }
        catch (PostgresException e) when (e.SqlState is "42501" or "P0001")
        { throw new WebAdminException(403, "not_authorized", "The category change is not authorized."); }
        catch (PostgresException e) when (e.SqlState == "22023")
        { throw new WebAdminException(422, "invalid_category", "The category data is not valid."); }
        catch (PostgresException e) when (e.SqlState is "54000" or "57014" or "55P03")
        { throw new WebAdminException(503, "pos_category_unavailable", "Category edits are temporarily unavailable."); }
    }
}
