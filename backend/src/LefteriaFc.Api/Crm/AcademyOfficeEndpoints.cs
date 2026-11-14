using System.Text.Json;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Api.Crm;

public static class AcademyOfficeEndpoints
{
    private const string Academy = "lefteria_fc_academy";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public sealed record Registration(Guid Id, string? FirstName, string? LastName, DateOnly BirthDate,
        string? GuardianName, string? GuardianRelationship, string? GuardianPhone, string? GuardianEmail);
    public sealed record Order(Guid Id, string? PlayerId, string? Item, string? Size, int Quantity);
    public sealed record Decision(Guid Id, string? Status, int Version);

    public static void MapAcademyOffice(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/academy/office").WithTags("Academy office")
            .RequireAuthorization(WebAdminServices.Policy);

        group.MapGet("/{collection}", async (string collection, int? offset, string? search,
            WebAdminService admin, WebAdminConnections db, IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            if (collection is not ("registrations" or "orders" or "players"))
                throw new WebAdminException(404, "unknown_collection", "Unknown Academy office collection.");
            if (offset is < 0 or > 20000 || search?.Length > 100)
                throw new WebAdminException(422, "invalid_filters", "Invalid Academy office filters.");
            return await Execute(collection, new { Offset = offset, Search = search?.Trim() }, false, admin, db, secrets, context, ct);
        }).WithName("ReadAcademyOffice");

        group.MapPost("/registrations", (Registration body, WebAdminService admin,
            WebAdminConnections db, IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
            Execute("register", body, true, admin, db, secrets, context, ct))
            .WithName("CreateAcademyRegistration");

        group.MapPost("/registrations/review", (Decision body, WebAdminService admin,
            WebAdminConnections db, IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
            Execute("review", body, true, admin, db, secrets, context, ct))
            .WithName("ReviewAcademyRegistration");

        group.MapPost("/orders", (Order body, WebAdminService admin,
            WebAdminConnections db, IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
            Execute("order", body, true, admin, db, secrets, context, ct))
            .WithName("CreateAcademyInternalOrder");

        group.MapPost("/orders/status", (Decision body, WebAdminService admin,
            WebAdminConnections db, IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            if (body.Status is not ("fulfilled" or "cancelled"))
                throw new WebAdminException(422, "invalid_status", "Invalid order status.");
            return Execute(body.Status, body, true, admin, db, secrets, context, ct);
        }).WithName("CloseAcademyInternalOrder");
    }

    private static async Task<IResult> Execute(string operation, object payload, bool write,
        WebAdminService admin, WebAdminConnections db, IWebAdminSecrets secrets, HttpContext context, CancellationToken ct)
    {
        var cookie = context.Request.Cookies[WebAdminServices.CookieName];
        var session = await admin.CurrentAsync(cookie, ct);
        if (session is null)
            throw new WebAdminException(401, "not_authenticated", "Sign in again.");
        if (session.User.Role is not ("manager" or "super_admin") ||
            !AdminPermissions.Has(session.User.Role, write ? "academy:write" : "academy:read") ||
            !session.User.Organizations.Any(org => org.Id == Academy))
            throw new WebAdminException(403, "not_authorized", "Academy manager access required.");

        await using var connection = await db.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT identity.admin_academy_office(@digest,@operation,@body)::text", connection);
        cmd.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie!));
        cmd.Parameters.AddWithValue("operation", operation);
        cmd.Parameters.AddWithValue("body", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(payload, JsonOptions));
        try
        {
            var json = await cmd.ExecuteScalarAsync(ct) as string;
            return json is null ? Results.Problem(statusCode: 503) : Results.Content(json, "application/json");
        }
        catch (PostgresException error) when (error.SqlState is "42501" or "23505" or "23502" or "23514" or "22001" or "22023" or "22007" or "22008" or "22P02")
        {
            throw new WebAdminException(error.SqlState switch { "42501" => 403, "23505" => 409, _ => 422 },
                "academy_office_request_rejected", "The Academy office request could not be applied.");
        }
    }
}
