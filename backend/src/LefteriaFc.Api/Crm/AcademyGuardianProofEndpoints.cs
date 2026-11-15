using System.Text.Json;
using System.Text.RegularExpressions;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Api.Crm;

/// <summary>
/// Case-by-case human guardian proof review for an already enrolled minor.
/// This does NOT grant app access, create OTP identities or mutate the roster.
/// The independently authenticated DB function enforces Academy Super Admin.
/// </summary>
public static class AcademyGuardianProofEndpoints
{
    private const string Academy = "lefteria_fc_academy";
    private static readonly JsonSerializerOptions JsonOptions = new()
    { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private static readonly Regex PhonePattern = new("^[1-9][0-9]{7,14}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ReferencePattern = new(
        "^[A-Za-z0-9][A-Za-z0-9_-]{7,63}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public sealed record VerifyRequest(Guid RegistrationId, string? Phone,
        string? ProofMethod, string? CaseReference, Guid RequestId, bool Attested);
    public sealed record RevokeRequest(Guid RegistrationId, int Version);

    public static IEndpointRouteBuilder MapAcademyGuardianProof(this IEndpointRouteBuilder app)
    {
        var routes = app.MapGroup("/api/admin/academy/office/guardians")
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("Academy guardian proof review");

        routes.MapGet("", (Guid? registration_id, WebAdminService auth,
            WebAdminConnections connections, IWebAdminSecrets secrets,
            HttpContext context, CancellationToken ct) =>
        {
            if (context.Request.Query["registration_id"].Count != 1 ||
                registration_id is null || registration_id == Guid.Empty)
                throw new WebAdminException(422, "guardian_review_invalid",
                    "Select exactly one enrolled Academy application.");
            return Execute("read", new { RegistrationId = registration_id.Value },
                auth, connections, secrets, context, ct);
        }).WithName("ReadAcademyGuardianProof").ExcludeFromDescription();

        routes.MapPost("/verify", (VerifyRequest body, WebAdminService auth,
            WebAdminConnections connections, IWebAdminSecrets secrets,
            HttpContext context, CancellationToken ct) =>
        {
            if (body.RegistrationId == Guid.Empty || body.RequestId == Guid.Empty ||
                !body.Attested || body.Phone is null || !PhonePattern.IsMatch(body.Phone) ||
                body.ProofMethod is not ("in_person_document" or "court_guardianship_order") ||
                body.CaseReference is null || !ReferencePattern.IsMatch(body.CaseReference))
                throw new WebAdminException(422, "guardian_review_invalid",
                    "Independent proof and a recorded human attestation are required.");
            return Execute("verify", body, auth, connections, secrets, context, ct);
        }).WithName("VerifyAcademyGuardianProof").ExcludeFromDescription();

        routes.MapPost("/revoke", (RevokeRequest body, WebAdminService auth,
            WebAdminConnections connections, IWebAdminSecrets secrets,
            HttpContext context, CancellationToken ct) =>
        {
            if (body.RegistrationId == Guid.Empty || body.Version is < 1 or > 99999)
                throw new WebAdminException(422, "guardian_review_invalid",
                    "Select a verified case at its current version.");
            return Execute("revoke", body, auth, connections, secrets, context, ct);
        }).WithName("RevokeAcademyGuardianProof").ExcludeFromDescription();
        return app;
    }

    private static async Task<IResult> Execute(string operation, object body,
        WebAdminService auth, WebAdminConnections connections, IWebAdminSecrets secrets,
        HttpContext context, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.Headers.Pragma = "no-cache";

        var cookie = context.Request.Cookies[WebAdminServices.CookieName];
        var session = await auth.CurrentAsync(cookie, ct);
        if (session is null)
            throw new WebAdminException(401, "not_authenticated", "Sign in again.");
        if (session.User.Role != "super_admin" ||
            !AdminPermissions.Has(session.User.Role, "academy:write") ||
            !session.User.Organizations.Any(org => org.Id == Academy))
            throw new WebAdminException(403, "guardian_review_denied",
                "Academy Super Admin approval required.");

        await using var connection = await connections.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            "SELECT identity.admin_academy_guardian_proof(@digest,@operation,@body)::text",
            connection);
        command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie!));
        command.Parameters.AddWithValue("operation", operation);
        command.Parameters.AddWithValue("body", NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(body, JsonOptions));

        try
        {
            var response = await command.ExecuteScalarAsync(ct) as string;
            return response is null ? Results.Problem(statusCode:503) :
                Results.Content(response, "application/json");
        }
        catch (PostgresException e) when (e.SqlState is "42501" or "23505"
            or "23514" or "23503" or "22023" or "22P02")
        {
            throw new WebAdminException(e.SqlState switch
            {
                "42501" => 403, "23505" or "23503" => 409, _ => 422
            }, "guardian_review_rejected",
                "The guardian case cannot be recorded or its state has changed.");
        }
        catch (PostgresException e) when (e.SqlState is "42883" or "42P01" or "3F000")
        {
            throw new WebAdminException(503, "guardian_review_not_enabled",
                "Guardian proof review is not enabled in this environment.");
        }
    }
}
