using System.Text.Json;
using LefteriaFc.Application.Pos;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Infrastructure.Pos;

/// <summary>
/// One trusted audited procedure. The admin runtime has no direct privilege
/// to mutate product, variant, operator, payment or sales tables.
/// </summary>
public sealed class PostgresAdminPosProductWriter(WebAdminConnections connections, IWebAdminSecrets secrets)
    : IAdminPosProductWriter
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public Task<AdminPosProductDto> CreateAsync(string cookie, string owner,
        AdminPosProductCreateCommand request, CancellationToken token)
    {
        Validate(request.CategoryId, request.Name, request.ImageUrl, request.Active,
            request.Variants, -1);
        return WriteAsync(cookie, owner, "prod_" + Guid.NewGuid().ToString("N"),
            request.CategoryId!, request.Name!, request.ImageUrl, request.Active!.Value,
            -1, request.Variants!, token);
    }

    public Task<AdminPosProductDto> UpdateAsync(string cookie, string owner, string id,
        AdminPosProductUpdateCommand request, CancellationToken token)
    {
        Validate(request.CategoryId, request.Name, request.ImageUrl, request.Active,
            request.Variants, request.ExpectedVersion);
        if (!Slug(id, 80))
            throw new WebAdminException(400, "invalid_product", "The product identifier is invalid.");
        return WriteAsync(cookie, owner, id, request.CategoryId!, request.Name!,
            request.ImageUrl, request.Active!.Value, request.ExpectedVersion!.Value,
            request.Variants!, token);
    }

    private static bool Slug(string? value, int length) =>
        value is { Length: > 0 } && value.Length <= length &&
        value.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-');

    private static void Validate(string? category, string? name, string? image, bool? active,
        IReadOnlyList<AdminPosVariantCommand>? variants, long? version)
    {
        if (!Slug(category, 80) || name is not {Length: >= 1 and <= 100} ||
            name != name.Trim() || name.Any(char.IsControl) ||
            active is null || version is null || version < -1 || version > 9007199254740991L ||
            variants is not {Count: >= 1 and <= 16} ||
            (image is not null && (image.Length > 2048 || image.Any(char.IsControl) ||
              !Uri.TryCreate(image, UriKind.Absolute, out var uri) ||
              uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))))
            throw Invalid();

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variant in variants)
        {
            if (!Slug(variant.Id, 60) || !ids.Add(variant.Id!) ||
                variant.Name is not {Length: >= 1 and <= 80} ||
                variant.Name != variant.Name.Trim() || variant.Name.Any(char.IsControl) ||
                variant.CostCents is null or < 0 or > 10000000 ||
                variant.PriceCents is null or < 1 or > 10000000)
                throw Invalid();
        }
    }

    private static WebAdminException Invalid() =>
        new(422, "invalid_product", "Provide valid category, name, state, prices and unique variants.");

    private async Task<AdminPosProductDto> WriteAsync(string cookie, string owner, string id,
        string category, string name, string? image, bool active, long version,
        IReadOnlyList<AdminPosVariantCommand> variants, CancellationToken ct)
    {
        if (!secrets.IsToken(cookie))
            throw new WebAdminException(401, "not_authenticated", "Sign in again.");
        if (owner is not ("lefteria_fc" or "lefteria_fc_academy"))
            throw new WebAdminException(400, "invalid_organization", "Select the current organization.");

        var payload = JsonSerializer.Serialize(variants.Select(row => new {
            id = row.Id, name = row.Name,
            cost_cents = row.CostCents, price_cents = row.PriceCents
        }));
        try
        {
            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand("""
                SELECT pos.write_admin_product(
                  @digest,@owner,@id,@category,@name,@image,@active,@version,@variants
                )::text
                """, connection);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner", owner);
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("category", category);
            command.Parameters.AddWithValue("name", name);
            command.Parameters.Add(new NpgsqlParameter("image", NpgsqlDbType.Text)
                { Value = (object?)image ?? DBNull.Value });
            command.Parameters.AddWithValue("active", active);
            command.Parameters.AddWithValue("version", version);
            command.Parameters.Add(new NpgsqlParameter("variants", NpgsqlDbType.Jsonb) {Value = payload});
            var raw = await command.ExecuteScalarAsync(ct) as string;
            var result = raw is null ? null : JsonSerializer.Deserialize<AdminPosProductDto>(raw, Json);
            if (result is null || result.Id != id || result.CategoryId != category ||
                result.Name != name || result.ImageUrl != image || result.Active != active ||
                result.Version < 0 || result.Variants is null ||
                result.Variants.Count != variants.Count ||
                !result.Variants.OrderBy(v => v.Id).Select(v =>
                    (v.Id,v.Name,v.CostCents,v.PriceCents)).SequenceEqual(
                    variants.OrderBy(v => v.Id).Select(v =>
                        (v.Id!,v.Name!,v.CostCents!.Value,v.PriceCents!.Value))))
                throw new WebAdminException(503, "pos_product_unavailable",
                    "The product write could not be verified.");
            return result;
        }
        catch (PostgresException e) when (e.SqlState=="P0001" &&
            e.MessageText=="pos_product_version_conflict")
        { throw new WebAdminException(409,"pos_product_version_conflict","Product changed; refresh before editing."); }
        catch (PostgresException e) when (e.SqlState=="P0001" &&
            e.MessageText=="admin_club_session_lock_missing")
        { throw new WebAdminException(401,"not_authenticated","Sign in again."); }
        catch (PostgresException e) when (e.SqlState=="P0001" &&
            e.MessageText=="admin_club_grant_lock_missing")
        { throw new WebAdminException(403,"not_authorized","Organization access was revoked."); }
        catch (PostgresException e) when (e.SqlState=="42501")
        { throw new WebAdminException(403,"not_authorized","Product management is not authorized."); }
        catch (PostgresException e) when (e.SqlState=="22023" || e.SqlState=="23503")
        { throw new WebAdminException(422,"invalid_product","Product or category is invalid."); }
        catch (PostgresException e) when (e.SqlState is "P0001" or "54000" or "57014" or "55P03")
        { throw new WebAdminException(503,"pos_product_unavailable","Product write is unavailable."); }
    }
}
