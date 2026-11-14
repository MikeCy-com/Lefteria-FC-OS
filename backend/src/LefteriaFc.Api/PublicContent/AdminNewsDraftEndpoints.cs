using System.Text.Json;
using System.Text.Json.Serialization;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Application.WebAdmin;

namespace LefteriaFc.Api.PublicContent;

/// <summary>
/// Only unpublished editorial drafts. No API route here writes to the public
/// content tables or exposes imported source evidence, and no publish route
/// exists until a separately reviewed human approval workflow is completed.
/// </summary>
public static class AdminNewsDraftEndpoints
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        MaxDepth = 12
    };

    public static IEndpointRouteBuilder MapAdminNewsDrafts(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/website/news-drafts")
            .WithTags("Website administration")
            .RequireAuthorization(WebAdminServices.Policy);

        var list = group.MapGet("", async (
            string? organization_id, IAdminNewsDraftRepository repository,
            HttpContext context, CancellationToken token) =>
        {
            var owner = Organization(context, organization_id);
            return Results.Ok(await repository.ReadAsync(Session(context), owner, token));
        })
        .WithName("GetAdminWebsiteNewsDrafts")
        .Produces<AdminNewsDraftPageDto>(200);

        var create = group.MapPost("", async (
            string? organization_id, IAdminNewsDraftRepository repository,
            HttpContext context, CancellationToken token) =>
        {
            var owner = Organization(context, organization_id);
            var body = await Parse<AdminNewsDraftCreate>(context, token);
            return Results.Ok(await repository.CreateAsync(Session(context), owner, body, token));
        })
        .WithName("CreateAdminWebsiteNewsDraft")
        .Accepts<AdminNewsDraftCreate>("application/json")
        .Produces<AdminNewsDraftDto>(200);

        var revision = group.MapPut("/{id}", async (
            string id, string? organization_id, IAdminNewsDraftRepository repository,
            HttpContext context, CancellationToken token) =>
        {
            var owner = Organization(context, organization_id);
            var body = await Parse<AdminNewsDraftRevision>(context, token);
            return Results.Ok(await repository.UpdateAsync(Session(context), owner, id, body, token));
        })
        .WithName("UpdateAdminWebsiteNewsDraft")
        .Accepts<AdminNewsDraftRevision>("application/json")
        .Produces<AdminNewsDraftDto>(200);

        foreach (var endpoint in new[] { list, create, revision })
            foreach (var status in new[] { 400, 401, 403, 404, 409, 413, 415, 422, 503 })
                endpoint.Produces<WebAdminError>(status);

        return endpoints;
    }

    private static string Organization(HttpContext context, string? owner) =>
        owner is not null && PublicContentService.IsOrganization(owner) &&
        context.Request.Query["organization_id"].Count == 1
            ? owner : throw new WebAdminException(400, "invalid_organization", "Choose one organization.");

    private static string Session(HttpContext context) =>
        context.Request.Cookies[WebAdminServices.CookieName] ?? "";

    private static async Task<T> Parse<T>(HttpContext context, CancellationToken token)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body,
                new JsonDocumentOptions { MaxDepth = 12 }, token);
            RejectDuplicateKeys(document.RootElement);
            return document.Deserialize<T>(Json) ??
                throw AdminNewsDraftEditable.Invalid();
        }
        catch (JsonException) { throw AdminNewsDraftEditable.Invalid(); }
    }

    private static void RejectDuplicateKeys(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new JsonException();
                RejectDuplicateKeys(property.Value);
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
            foreach (var element in root.EnumerateArray()) RejectDuplicateKeys(element);
    }
}
