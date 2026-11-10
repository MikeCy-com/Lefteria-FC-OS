using System.Text;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;

namespace LefteriaFc.Api.Evaluations;

/// <summary>
/// Metadata only. Authenticated management may inspect expiry; no file path,
/// object key, encryption key, download or medical content is returned.
/// </summary>
public static class AdminAcademyDocumentEndpoints
{
    public static IEndpointRouteBuilder MapAdminAcademyDocuments(
        this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/admin/academy/players")
            .WithTags("Academy protected documents")
            .RequireAuthorization(WebAdminServices.Policy);
        admin.MapGet("/{playerId}/documents", async (
            string playerId, string? organization_id,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers.Pragma = "no-cache";
            if (organization_id != "lefteria_fc_academy" ||
                context.Request.Query["organization_id"].Count != 1 ||
                playerId.Length is < 1 or > 128 ||
                playerId.Any(char.IsControl))
                throw new WebAdminException(400, "academy_document_scope_invalid",
                    "Choose one valid Academy player.");
            var cookie = context.Request.Cookies[WebAdminServices.CookieName];
            var session = await auth.CurrentAsync(cookie, ct);
            if (session is null)
                throw new WebAdminException(401, "not_authenticated", "Sign in again.");
            if (session.User.Role is not ("manager" or "super_admin") ||
                !AdminPermissions.Has(session.User.Role, "academy:read") ||
                !session.User.Organizations.Any(o => o.Id == organization_id))
                throw new WebAdminException(403, "academy_document_access_denied",
                    "Only Academy management may view document metadata.");
            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT academy_private.read_admin_player_document_metadata(@digest,@owner,@player)::text",
                connection);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie!));
            command.Parameters.AddWithValue("owner", organization_id);
            command.Parameters.AddWithValue("player", playerId);
            try
            {
                var json = await command.ExecuteScalarAsync(ct) as string;
                return json is null ? Results.Problem(statusCode:503)
                    : Results.Content(json, "application/json", Encoding.UTF8, 200);
            }
            catch (PostgresException e) when (e.SqlState is
                "42501" or "22023" or "54000")
            {
                var status = e.SqlState switch
                {
                    "42501" => 403, "22023" => 422, _ => 413
                };
                throw new WebAdminException(status, "academy_documents_unavailable",
                    "Private player documents are unavailable for this account.");
            }
        }).WithName("ReadAdminAcademyPlayerDocuments");
        return endpoints;
    }
}
