using System.Text.Json;
using System.Text.Json.Serialization;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.PublicContent;

namespace LefteriaFc.Api.PublicContent;

public static class AdminClubEndpoints
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 32
    };
    public static IEndpointRouteBuilder MapAdminClub(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/club").WithTags("Club settings").RequireAuthorization(WebAdminServices.Policy);
        Errors(group.MapGet("", async (string? organization_id, IAdminClubRepository repository, HttpContext context, CancellationToken token) =>
            Results.Ok(await repository.ReadAsync(Session(context), Organization(organization_id), token)))
            .WithName("GetAdminClubProfile").Produces<AdminClubProfileDto>());
        Errors(group.MapPut("", async (string? organization_id, IAdminClubRepository repository, HttpContext context, CancellationToken token) =>
        {
            var organization = Organization(organization_id);
            AdminClubUpdateCommand? command;
            try
            {
                using var document = await JsonDocument.ParseAsync(context.Request.Body, new JsonDocumentOptions { MaxDepth = 32 }, token);
                RequireUniqueKeys(document.RootElement);
                command = document.Deserialize<AdminClubUpdateCommand>(Json);
            }
            catch (JsonException) { throw AdminClubEditable.Invalid(); }
            if (command is null) throw AdminClubEditable.Invalid();
            return Results.Ok(await repository.UpdateAsync(Session(context), organization, command, token));
        }).WithName("UpdateAdminClubProfile").Accepts<AdminClubUpdateCommand>("application/json").Produces<AdminClubProfileDto>());
        return endpoints;
    }
    private static string Session(HttpContext context) => context.Request.Cookies[WebAdminServices.CookieName] ?? "";
    private static string Organization(string? organization) => organization is not null && PublicContentService.IsOrganization(organization) ? organization :
        throw new LefteriaFc.Application.WebAdmin.WebAdminException(400, "invalid_organization", "Choose a known organization.");
    private static void RequireUniqueKeys(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            { if (!keys.Add(property.Name)) throw new JsonException(); RequireUniqueKeys(property.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) RequireUniqueKeys(item);
    }
    private static RouteHandlerBuilder Errors(RouteHandlerBuilder endpoint)
    {
        foreach (var status in new[] { 400, 401, 403, 404, 409, 413, 415, 422, 503 }) endpoint.Produces<WebAdminError>(status);
        return endpoint;
    }
}
