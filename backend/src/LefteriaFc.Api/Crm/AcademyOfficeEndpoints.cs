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
    public sealed record Enrollment(Guid Id, int Version, Guid RequestId,
        Guid CityId, Guid LocationId, string GroupId, string Position, int Number);

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

        // Verified public guardian-consent records only. Review + roster
        // creation + location placement run in one PostgreSQL transaction.
        group.MapPost("/registrations/enroll", (Enrollment body,
            WebAdminService admin, WebAdminConnections db, IWebAdminSecrets secrets,
            HttpContext context, CancellationToken ct) =>
        {
            if (body.Id == Guid.Empty || body.RequestId == Guid.Empty ||
                body.CityId == Guid.Empty || body.LocationId == Guid.Empty ||
                body.Version < 1 || body.Version > 100000 ||
                body.GroupId is null || body.GroupId.Length is < 1 or > 128 ||
                body.Position is not ("Goalkeeper" or "Defender" or "Midfielder" or "Forward") ||
                body.Number is < 1 or > 99)
                throw new WebAdminException(422, "invalid_enrollment",
                    "Select one exact Academy group and the real player details.");
            return Execute("enroll", body, true, admin, db, secrets, context, ct);
        }).WithName("EnrollAcademyRegistration");

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
        // Academy applications contain private minor and guardian details.
        // Never allow browsers or intermediaries to cache these responses.
        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.Headers.Pragma = "no-cache";
        var cookie = context.Request.Cookies[WebAdminServices.CookieName];
        var session = await admin.CurrentAsync(cookie, ct);
        if (session is null)
            throw new WebAdminException(401, "not_authenticated", "Sign in again.");
        if (session.User.Role is not ("manager" or "super_admin") ||
            !AdminPermissions.Has(session.User.Role, write ? "academy:write" : "academy:read") ||
            !session.User.Organizations.Any(org => org.Id == Academy))
            throw new WebAdminException(403, "not_authorized", "Academy manager access required.");

        await using var connection = await db.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            operation == "enroll"
                ? "SELECT identity.enroll_admin_academy_registration(@digest,@body)::text"
                : "SELECT identity.admin_academy_office(@digest,@operation,@body)::text", connection);
        cmd.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie!));
        if (operation != "enroll")
            cmd.Parameters.AddWithValue("operation", operation);
        cmd.Parameters.AddWithValue("body", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(payload, JsonOptions));
        try
        {
            var json = await cmd.ExecuteScalarAsync(ct) as string;
            return json is null ? Results.Problem(statusCode: 503) : Results.Content(json, "application/json");
        }
        catch (PostgresException error) when (error.SqlState is "42501" or "23505" or "23503" or "23502" or "23514" or "22001" or "22023" or "22007" or "22008" or "22P02" or "55000" or "40001")
        {
            throw new WebAdminException(error.SqlState switch { "42501" => 403, "23505" or "23503" or "40001" => 409, "55000" => 503, _ => 422 },
                "academy_office_request_rejected", "The Academy office request could not be applied.");
        }
    }
}
