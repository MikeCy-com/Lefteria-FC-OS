using System.Text.Json;
using System.Text.Json.Serialization;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.Pos;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;

namespace LefteriaFc.Api.Pos;

/// <summary>
/// Category edits only. Request guards enforce origin, JSON and size before
/// these endpoints. No product, checkout, cash or operator write exists.
/// </summary>
public static class AdminPosCategoryEndpoints
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };

    public static IEndpointRouteBuilder MapAdminPosCategories(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/pos/categories")
            .WithTags("POS administration")
            .RequireAuthorization(WebAdminServices.Policy);

        Errors(group.MapPost("", async (string? organization_id, WebAdminService admin,
            IAdminPosCategoryWriter writer, HttpContext context, CancellationToken token) =>
        {
            var (cookie, owner) = await Authority(admin, organization_id, context, token);
            var command = await Body<AdminPosCategoryCreateCommand>(context, token);
            return Results.Ok(await writer.CreateAsync(cookie, owner, command, token));
        }).WithName("CreateAdminPosCategory")
          .Accepts<AdminPosCategoryCreateCommand>("application/json")
          .Produces<AdminPosCategoryDto>(200));

        Errors(group.MapPut("/{id}", async (string id, string? organization_id, WebAdminService admin,
            IAdminPosCategoryWriter writer, HttpContext context, CancellationToken token) =>
        {
            var (cookie, owner) = await Authority(admin, organization_id, context, token);
            var command = await Body<AdminPosCategoryUpdateCommand>(context, token);
            return Results.Ok(await writer.UpdateAsync(cookie, owner, id, command, token));
        }).WithName("UpdateAdminPosCategory")
          .Accepts<AdminPosCategoryUpdateCommand>("application/json")
          .Produces<AdminPosCategoryDto>(200));

        return endpoints;
    }

    private static async Task<(string Cookie, string Owner)> Authority(
        WebAdminService admin, string? organization, HttpContext context, CancellationToken token)
    {
        if (organization is null || !PublicContentService.IsOrganization(organization))
            throw new WebAdminException(400, "invalid_organization", "Select an exact organization.");
        var cookie = context.Request.Cookies[WebAdminServices.CookieName];
        var session = await admin.CurrentAsync(cookie, token);
        if (session is null)
            throw new WebAdminException(401, "not_authenticated", "Sign in again.");
        if (!AdminPermissions.Has(session.User.Role, "shop:write") ||
            !session.User.Organizations.Any(org => org.Id == organization))
            throw new WebAdminException(403, "not_authorized", "You cannot edit this organization's POS catalog.");
        return (cookie!, organization);
    }

    private static async Task<T> Body<T>(HttpContext context, CancellationToken token) where T : class
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(
                context.Request.Body, new JsonDocumentOptions { MaxDepth = 8 }, token);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in doc.RootElement.EnumerateObject())
                if (!keys.Add(property.Name)) throw new JsonException();
            return doc.Deserialize<T>(Json) ?? throw new JsonException();
        }
        catch (JsonException)
        { throw new WebAdminException(400, "invalid_json", "Provide one well-formed category JSON object."); }
    }

    private static void Errors(RouteHandlerBuilder endpoint)
    {
        foreach (var status in new[] {400,401,403,409,413,415,422,503})
            endpoint.Produces<WebAdminError>(status);
    }
}
