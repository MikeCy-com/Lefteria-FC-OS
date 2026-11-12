using System.Text.Json;
using System.Security.Cryptography;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Api.Hub;

/// <summary>
/// Lists limited metadata for actual stored HUB editorial drafts. No private
/// body, media, fan billing data, public publishing or raw table SELECT.
/// A fresh web-admin session and exact org grant are rechecked in SQL.
/// </summary>
public sealed record HubDraftCommand(
    string Kind, string Title, string Body,
    bool AudienceParent, bool AudiencePlayer, short? AgeMin, short? AgeMax);

public sealed record HubReviewSubmitCommand(long Version);
public sealed record HubDraftRevisionCommand(long Version, string Title, string Body);
public sealed record HubQuarantineImageCommand(long Version, string Base64, string AltText);
public sealed record HubTextReviewCommand(long Version, string Decision);

public static class AdminHubEndpoints
{
    public static IEndpointRouteBuilder MapAdminHub(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/hub/editorial",
            async (string? organization_id, WebAdminService admin, WebAdminConnections connections,
                   IWebAdminSecrets secrets, HttpContext context, CancellationToken token) =>
            {
                context.Response.Headers.CacheControl = "private, no-store";
                context.Response.Headers.Pragma = "no-cache";
                if (organization_id is null ||
                    context.Request.Query["organization_id"].Count != 1 ||
                    !PublicContentService.IsOrganization(organization_id))
                    return Failure(400, "invalid_organization");

                var cookie = context.Request.Cookies[WebAdminServices.CookieName];
                var session = await admin.CurrentAsync(cookie, token);
                if (session is null) return Failure(401, "not_authenticated");
                if (!session.User.Organizations.Any(org => org.Id == organization_id) ||
                    !AdminPermissions.Has(session.User.Role, "news:read"))
                    return Failure(403, "not_authorized");
                if (string.IsNullOrWhiteSpace(cookie) || !secrets.IsToken(cookie))
                    return Failure(401, "not_authenticated");

                try
                {
                    await using var connection = await connections.OpenAsync(token);
                    await using var command = new NpgsqlCommand(
                        "SELECT hub.read_admin_editorial(@digest,@owner)::text", connection);
                    command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie!));
                    command.Parameters.AddWithValue("owner", organization_id);
                    var raw = await command.ExecuteScalarAsync(token) as string;
                    if (raw is null) return Failure(503, "hub_unavailable");
                    using var parsed = JsonDocument.Parse(raw);
                    if (parsed.RootElement.ValueKind != JsonValueKind.Object ||
                        !parsed.RootElement.TryGetProperty("organization_id", out var owner) ||
                        owner.GetString() != organization_id ||
                        !parsed.RootElement.TryGetProperty("items", out var items) ||
                        items.ValueKind != JsonValueKind.Array ||
                        items.GetArrayLength() > 100)
                        return Failure(503, "hub_unavailable");
                    return Results.Content(raw, "application/json");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception) { return Failure(503, "hub_unavailable"); }
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("HUB administration")
            .WithName("GetAdminHubEditorial")
            .Produces(200, contentType: "application/json")
            .Produces<WebAdminError>(400)
            .Produces<WebAdminError>(401)
            .Produces<WebAdminError>(403)
            .Produces<WebAdminError>(503);

        endpoints.MapPost("/api/admin/hub/editorial",
            async (string? organization_id, HubDraftCommand draft, WebAdminService admin,
                   WebAdminConnections connections, IWebAdminSecrets secrets,
                   HttpContext context, CancellationToken token) =>
            {
                context.Response.Headers.CacheControl = "private, no-store";
                context.Response.Headers.Pragma = "no-cache";
                if (organization_id is null ||
                    context.Request.Query["organization_id"].Count != 1 ||
                    !PublicContentService.IsOrganization(organization_id))
                    return Failure(400, "invalid_organization");
                var cookie = context.Request.Cookies[WebAdminServices.CookieName];
                var session = await admin.CurrentAsync(cookie, token);
                if (session is null || !secrets.IsToken(cookie))
                    return Failure(401, "not_authenticated");
                if (!session.User.Organizations.Any(org => org.Id == organization_id) ||
                    !AdminPermissions.Has(session.User.Role, "news:write"))
                    return Failure(403, "not_authorized");

                if (draft.Kind is not ("article" or "drill" or "video" or "visual" or "course") ||
                    string.IsNullOrEmpty(draft.Title) || draft.Title.Length > 180 ||
                    draft.Title != draft.Title.Trim() || draft.Title.Any(char.IsControl) ||
                    string.IsNullOrEmpty(draft.Body) || draft.Body.Length > 20000 ||
                    draft.Body != draft.Body.Trim() || draft.Body.Any(char.IsControl) ||
                    !(draft.AudienceParent || draft.AudiencePlayer) ||
                    (draft.AgeMin is null) != (draft.AgeMax is null) ||
                    (draft.AgeMin is not null && (draft.AgeMin < 4 || draft.AgeMax > 99 || draft.AgeMax < draft.AgeMin)))
                    return Failure(422, "invalid_hub_draft");
                try
                {
                    await using var connection = await connections.OpenAsync(token);
                    await using var command = new NpgsqlCommand(
                        "SELECT hub.create_admin_draft(@digest,@owner,@id,@revision_id,@kind,@title,@body,@parent,@player,@age_min,@age_max)::text", connection);
                    command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie!));
                    command.Parameters.AddWithValue("owner", organization_id);
                    var id = Guid.NewGuid();
                    command.Parameters.AddWithValue("id", id);
                    command.Parameters.AddWithValue("revision_id", Guid.NewGuid());
                    command.Parameters.AddWithValue("kind", draft.Kind);
                    command.Parameters.AddWithValue("title", draft.Title);
                    command.Parameters.AddWithValue("body", draft.Body);
                    command.Parameters.AddWithValue("parent", draft.AudienceParent);
                    command.Parameters.AddWithValue("player", draft.AudiencePlayer);
                    command.Parameters.AddWithValue("age_min", NpgsqlDbType.Smallint,
                        (object?)draft.AgeMin ?? DBNull.Value);
                    command.Parameters.AddWithValue("age_max", NpgsqlDbType.Smallint,
                        (object?)draft.AgeMax ?? DBNull.Value);
                    var raw = await command.ExecuteScalarAsync(token) as string;
                    if (raw is null) return Failure(503, "hub_unavailable");
                    using var parsed = JsonDocument.Parse(raw);
                    var root = parsed.RootElement;
                    if (!root.TryGetProperty("id", out var returnedId) ||
                        returnedId.GetGuid() != id ||
                        !root.TryGetProperty("organization_id", out var owner) ||
                        owner.GetString() != organization_id ||
                        !root.TryGetProperty("status", out var status) ||
                        status.GetString() != "ai_draft")
                        return Failure(503, "hub_unavailable");
                    return Results.Content(raw, "application/json", statusCode: 201);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (PostgresException ex) when (ex.SqlState is "23505" or "23503")
                { return Failure(409, "hub_draft_conflict"); }
                catch (PostgresException ex) when (ex.SqlState == "42501")
                { return Failure(403, "not_authorized"); }
                catch (Exception) { return Failure(503, "hub_unavailable"); }
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("HUB administration")
            .WithName("CreateAdminHubDraft")
            .Produces(201, contentType: "application/json")
            .Produces<WebAdminError>(400)
            .Produces<WebAdminError>(401)
            .Produces<WebAdminError>(403)
            .Produces<WebAdminError>(409)
            .Produces<WebAdminError>(422)
            .Produces<WebAdminError>(503);

        // Human changes are allowed only while a draft is unpublished and unreviewed.
        // Only an authenticated manager can inspect a sanitized, quarantined JPEG.
        // No anonymous URL, member retrieval, approval or publication.
        endpoints.MapGet("/api/admin/hub/editorial/{id:guid}/images/{mediaId:guid}/preview",
            async (Guid id, Guid mediaId, string? organization_id,
                   WebAdminService admin, WebAdminConnections connections,
                   IWebAdminSecrets secrets, HttpContext context, CancellationToken token) =>
            {
                context.Response.Headers.CacheControl = "private, no-store";
                context.Response.Headers.Pragma = "no-cache";
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
                context.Response.Headers["Content-Disposition"] = "inline; filename=\"hub-review.jpg\"";
                if (organization_id is null ||
                    context.Request.Query["organization_id"].Count != 1 ||
                    !PublicContentService.IsOrganization(organization_id))
                    return Failure(400, "invalid_organization");
                if (id == Guid.Empty || mediaId == Guid.Empty)
                    return Failure(422, "invalid_hub_preview");
                var cookie = context.Request.Cookies[WebAdminServices.CookieName];
                var session = await admin.CurrentAsync(cookie, token);
                if (session is null || !secrets.IsToken(cookie))
                    return Failure(401, "not_authenticated");
                if (!session.User.Organizations.Any(org => org.Id == organization_id) ||
                    session.User.Role is not ("manager" or "super_admin"))
                    return Failure(403, "not_authorized");
                try
                {
                    await using var connection = await connections.OpenAsync(token);
                    await using var command = new NpgsqlCommand(
                        "SELECT hub.read_admin_quarantined_image(@digest,@owner,@id,@media)::text", connection);
                    command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie!));
                    command.Parameters.AddWithValue("owner", organization_id);
                    command.Parameters.AddWithValue("id", id);
                    command.Parameters.AddWithValue("media", mediaId);
                    var json = await command.ExecuteScalarAsync(token) as string;
                    if (json is null) return Failure(503, "hub_unavailable");
                    using var parsed = JsonDocument.Parse(json);
                    var result = parsed.RootElement;
                    if (result.ValueKind != JsonValueKind.Object ||
                        !result.TryGetProperty("id", out var returnedId) || returnedId.GetGuid() != mediaId ||
                        !result.TryGetProperty("content_id", out var contentId) || contentId.GetGuid() != id ||
                        !result.TryGetProperty("organization_id", out var owner) || owner.GetString() != organization_id ||
                        !result.TryGetProperty("mime_type", out var type) || type.GetString() != "image/jpeg" ||
                        !result.TryGetProperty("sha256", out var sha) || sha.GetString() is not { Length: 64 } ||
                        !result.TryGetProperty("byte_count", out var count) ||
                        !count.TryGetInt32(out var expectedBytes) || expectedBytes is < 4 or > 1048576 ||
                        !result.TryGetProperty("base64", out var encoded) ||
                        encoded.GetString() is not { Length: > 0 and <= 1398104 })
                        return Failure(503, "hub_unavailable");
                    var raw = encoded.GetString()!;
                    byte[] jpeg;
                    try { jpeg = Convert.FromBase64String(raw); }
                    catch (FormatException) { return Failure(503, "hub_unavailable"); }
                    if (jpeg.Length != expectedBytes ||
                        jpeg.Length < 4 ||
                        jpeg[0] != 0xff || jpeg[1] != 0xd8 ||
                        jpeg[^2] != 0xff || jpeg[^1] != 0xd9 ||
                        !string.Equals(Convert.ToHexString(SHA256.HashData(jpeg)),
                            sha.GetString(), StringComparison.OrdinalIgnoreCase))
                        return Failure(503, "hub_unavailable");
                    return Results.File(jpeg, "image/jpeg");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (PostgresException ex) when (ex.SqlState == "P0002")
                { return Failure(404, "hub_image_not_found"); }
                catch (PostgresException ex) when (ex.SqlState == "42501")
                { return Failure(403, "not_authorized"); }
                catch (Exception) { return Failure(503, "hub_unavailable"); }
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("HUB administration")
            .WithName("PreviewAdminHubQuarantinedImage")
            .Produces(200, contentType: "image/jpeg")
            .Produces<WebAdminError>(400)
            .Produces<WebAdminError>(401)
            .Produces<WebAdminError>(403)
            .Produces<WebAdminError>(404)
            .Produces<WebAdminError>(422)
            .Produces<WebAdminError>(503);

        // Small, sanitized JPEGs are stored in quarantine only; there is no media download.
        endpoints.MapPost("/api/admin/hub/editorial/{id:guid}/images/quarantine",
            async (Guid id, string? organization_id, HubQuarantineImageCommand body,
                   WebAdminService admin, WebAdminConnections connections,
                   IWebAdminSecrets secrets, IAvatarImageSanitizer sanitizer,
                   HttpContext context, CancellationToken token) =>
            {
                context.Response.Headers.CacheControl = "private, no-store";
                context.Response.Headers.Pragma = "no-cache";
                if (organization_id is null ||
                    context.Request.Query["organization_id"].Count != 1 ||
                    !PublicContentService.IsOrganization(organization_id))
                    return Failure(400, "invalid_organization");
                if (id == Guid.Empty || body.Version < 1 ||
                    string.IsNullOrWhiteSpace(body.Base64) ||
                    body.Base64.Length > 2097152 ||
                    string.IsNullOrWhiteSpace(body.AltText) ||
                    body.AltText.Length > 500 ||
                    body.AltText != body.AltText.Trim() || body.AltText.Any(char.IsControl))
                    return Failure(422, "invalid_hub_image");
                var cookie = context.Request.Cookies[WebAdminServices.CookieName];
                var session = await admin.CurrentAsync(cookie, token);
                if (session is null || !secrets.IsToken(cookie))
                    return Failure(401, "not_authenticated");
                if (!session.User.Organizations.Any(org => org.Id == organization_id) ||
                    !AdminPermissions.Has(session.User.Role, "news:write"))
                    return Failure(403, "not_authorized");
                byte[] raw;
                try { raw = Convert.FromBase64String(body.Base64); }
                catch (FormatException) { return Failure(422, "invalid_hub_image"); }
                byte[]? clean = null;
                try
                {
                    if (raw.Length is < 4 or > 1048576) return Failure(422, "invalid_hub_image");
                    try { clean = sanitizer.Sanitize(raw, "image/jpeg", token).JpegBytes; }
                    catch (MobileAuthException) { return Failure(422, "invalid_hub_image"); }
                    if (clean.Length is < 4 or > 1048576) return Failure(422, "invalid_hub_image");
                    var mediaId = Guid.NewGuid();
                    await using var connection = await connections.OpenAsync(token);
                    await using var command = new NpgsqlCommand(
                        "SELECT hub.quarantine_admin_image(@digest,@owner,@id,@version,@media,@alt,@jpeg)::text", connection);
                    command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie!));
                    command.Parameters.AddWithValue("owner", organization_id);
                    command.Parameters.AddWithValue("id", id);
                    command.Parameters.AddWithValue("version", body.Version);
                    command.Parameters.AddWithValue("media", mediaId);
                    command.Parameters.AddWithValue("alt", body.AltText);
                    command.Parameters.Add("jpeg", NpgsqlDbType.Bytea).Value = clean;
                    var returned = await command.ExecuteScalarAsync(token) as string;
                    if (returned is null) return Failure(503, "hub_unavailable");
                    using var parsed = JsonDocument.Parse(returned);
                    var result = parsed.RootElement;
                    if (!result.TryGetProperty("id", out var resultId) || resultId.GetGuid() != mediaId ||
                        !result.TryGetProperty("organization_id", out var owner) || owner.GetString() != organization_id ||
                        !result.TryGetProperty("content_id", out var content) || content.GetGuid() != id ||
                        !result.TryGetProperty("status", out var status) || status.GetString() != "quarantined")
                        return Failure(503, "hub_unavailable");
                    return Results.Content(returned, "application/json", statusCode: 201);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (PostgresException ex) when (ex.SqlState is "40001" or "23505")
                { return Failure(409, "hub_image_conflict"); }
                catch (PostgresException ex) when (ex.SqlState == "P0002")
                { return Failure(404, "hub_draft_not_found"); }
                catch (PostgresException ex) when (ex.SqlState == "42501")
                { return Failure(403, "not_authorized"); }
                catch (Exception) { return Failure(503, "hub_unavailable"); }
                finally
                {
                    CryptographicOperations.ZeroMemory(raw);
                    if (clean is not null) CryptographicOperations.ZeroMemory(clean);
                }
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("HUB administration")
            .WithName("QuarantineAdminHubImage")
            .Produces(201, contentType: "application/json")
            .Produces<WebAdminError>(400)
            .Produces<WebAdminError>(401)
            .Produces<WebAdminError>(403)
            .Produces<WebAdminError>(404)
            .Produces<WebAdminError>(409)
            .Produces<WebAdminError>(422)
            .Produces<WebAdminError>(503);

        endpoints.MapPost("/api/admin/hub/editorial/{id:guid}/revise",
            async (Guid id, string? organization_id, HubDraftRevisionCommand body,
                   WebAdminService admin, WebAdminConnections connections,
                   IWebAdminSecrets secrets, HttpContext context, CancellationToken token) =>
            {
                context.Response.Headers.CacheControl = "private, no-store";
                context.Response.Headers.Pragma = "no-cache";
                if (organization_id is null ||
                    context.Request.Query["organization_id"].Count != 1 ||
                    !PublicContentService.IsOrganization(organization_id))
                    return Failure(400, "invalid_organization");
                if (id == Guid.Empty || body.Version < 1 ||
                    body.Version == long.MaxValue ||
                    string.IsNullOrEmpty(body.Title) || body.Title.Length > 180 ||
                    body.Title != body.Title.Trim() || body.Title.Any(char.IsControl) ||
                    string.IsNullOrEmpty(body.Body) || body.Body.Length > 20000 ||
                    body.Body != body.Body.Trim() || body.Body.Any(char.IsControl))
                    return Failure(422, "invalid_hub_revision");

                var cookie = context.Request.Cookies[WebAdminServices.CookieName];
                var session = await admin.CurrentAsync(cookie, token);
                if (session is null || !secrets.IsToken(cookie))
                    return Failure(401, "not_authenticated");
                if (!session.User.Organizations.Any(org => org.Id == organization_id) ||
                    !AdminPermissions.Has(session.User.Role, "news:write"))
                    return Failure(403, "not_authorized");
                try
                {
                    await using var connection = await connections.OpenAsync(token);
                    await using var command = new NpgsqlCommand(
                        "SELECT hub.revise_admin_draft(@digest,@owner,@id,@version,@title,@body)::text", connection);
                    command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie!));
                    command.Parameters.AddWithValue("owner", organization_id);
                    command.Parameters.AddWithValue("id", id);
                    command.Parameters.AddWithValue("version", body.Version);
                    command.Parameters.AddWithValue("title", body.Title);
                    command.Parameters.AddWithValue("body", body.Body);
                    var raw = await command.ExecuteScalarAsync(token) as string;
                    if (raw is null) return Failure(503, "hub_unavailable");
                    using var parsed = JsonDocument.Parse(raw);
                    var result = parsed.RootElement;
                    if (!result.TryGetProperty("id", out var returnedId) || returnedId.GetGuid() != id ||
                        !result.TryGetProperty("organization_id", out var owner) || owner.GetString() != organization_id ||
                        !result.TryGetProperty("status", out var status) || status.GetString() != "ai_draft" ||
                        !result.TryGetProperty("version", out var version) || version.GetInt64() != body.Version + 1)
                        return Failure(503, "hub_unavailable");
                    return Results.Content(raw, "application/json");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (PostgresException ex) when (ex.SqlState is "40001" or "23505")
                { return Failure(409, "hub_revision_conflict"); }
                catch (PostgresException ex) when (ex.SqlState == "P0002")
                { return Failure(404, "hub_draft_not_found"); }
                catch (PostgresException ex) when (ex.SqlState == "42501")
                { return Failure(403, "not_authorized"); }
                catch (Exception) { return Failure(503, "hub_unavailable"); }
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("HUB administration")
            .WithName("ReviseAdminHubDraft")
            .Produces(200, contentType: "application/json")
            .Produces<WebAdminError>(400)
            .Produces<WebAdminError>(401)
            .Produces<WebAdminError>(403)
            .Produces<WebAdminError>(404)
            .Produces<WebAdminError>(409)
            .Produces<WebAdminError>(422)
            .Produces<WebAdminError>(503);

        endpoints.MapPost("/api/admin/hub/editorial/{id:guid}/submit-review",
            async (Guid id, string? organization_id, HubReviewSubmitCommand body,
                   WebAdminService admin, WebAdminConnections connections,
                   IWebAdminSecrets secrets, HttpContext context, CancellationToken token) =>
            {
                context.Response.Headers.CacheControl = "private, no-store";
                context.Response.Headers.Pragma = "no-cache";
                if (organization_id is null ||
                    context.Request.Query["organization_id"].Count != 1 ||
                    !PublicContentService.IsOrganization(organization_id))
                    return Failure(400, "invalid_organization");
                if (id == Guid.Empty || body.Version < 1)
                    return Failure(422, "invalid_hub_review_request");

                var cookie = context.Request.Cookies[WebAdminServices.CookieName];
                var session = await admin.CurrentAsync(cookie, token);
                if (session is null || !secrets.IsToken(cookie))
                    return Failure(401, "not_authenticated");
                if (!session.User.Organizations.Any(org => org.Id == organization_id) ||
                    !AdminPermissions.Has(session.User.Role, "news:write"))
                    return Failure(403, "not_authorized");
                try
                {
                    await using var connection = await connections.OpenAsync(token);
                    await using var command = new NpgsqlCommand(
                        "SELECT hub.submit_admin_draft_review(@digest,@owner,@id,@version)::text", connection);
                    command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie!));
                    command.Parameters.AddWithValue("owner", organization_id);
                    command.Parameters.AddWithValue("id", id);
                    command.Parameters.AddWithValue("version", body.Version);
                    var raw = await command.ExecuteScalarAsync(token) as string;
                    if (raw is null) return Failure(503, "hub_unavailable");
                    using var parsed = JsonDocument.Parse(raw);
                    var root = parsed.RootElement;
                    if (!root.TryGetProperty("id", out var resultId) ||
                        resultId.GetGuid() != id ||
                        !root.TryGetProperty("organization_id", out var resultOwner) ||
                        resultOwner.GetString() != organization_id ||
                        !root.TryGetProperty("status", out var resultStatus) ||
                        resultStatus.GetString() != "review_required" ||
                        !root.TryGetProperty("version", out var resultVersion) ||
                        resultVersion.GetInt64() != body.Version + 1)
                        return Failure(503, "hub_unavailable");
                    return Results.Content(raw, "application/json");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (PostgresException ex) when (ex.SqlState is "40001" or "23505")
                { return Failure(409, "hub_review_conflict"); }
                catch (PostgresException ex) when (ex.SqlState == "P0002")
                { return Failure(404, "hub_draft_not_found"); }
                catch (PostgresException ex) when (ex.SqlState == "42501")
                { return Failure(403, "not_authorized"); }
                catch (Exception) { return Failure(503, "hub_unavailable"); }
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("HUB administration")
            .WithName("SubmitAdminHubDraftReview")
            .Produces(200, contentType: "application/json")
            .Produces<WebAdminError>(400)
            .Produces<WebAdminError>(401)
            .Produces<WebAdminError>(403)
            .Produces<WebAdminError>(404)
            .Produces<WebAdminError>(409)
            .Produces<WebAdminError>(422)
            .Produces<WebAdminError>(503);

        endpoints.MapGet("/api/admin/hub/editorial/{id:guid}",
            async (Guid id, string? organization_id, WebAdminService admin,
                   WebAdminConnections connections, IWebAdminSecrets secrets,
                   HttpContext context, CancellationToken token) =>
            {
                context.Response.Headers.CacheControl = "private, no-store";
                context.Response.Headers.Pragma = "no-cache";
                if (organization_id is null ||
                    context.Request.Query["organization_id"].Count != 1 ||
                    !PublicContentService.IsOrganization(organization_id))
                    return Failure(400, "invalid_organization");
                if (id == Guid.Empty) return Failure(422, "invalid_hub_detail");

                var cookie = context.Request.Cookies[WebAdminServices.CookieName];
                var session = await admin.CurrentAsync(cookie, token);
                if (session is null || !secrets.IsToken(cookie))
                    return Failure(401, "not_authenticated");
                if (!session.User.Organizations.Any(org => org.Id == organization_id) ||
                    !AdminPermissions.Has(session.User.Role, "news:read"))
                    return Failure(403, "not_authorized");
                try
                {
                    await using var connection = await connections.OpenAsync(token);
                    await using var command = new NpgsqlCommand(
                        "SELECT hub.read_admin_editorial_detail(@digest,@owner,@id)::text", connection);
                    command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie!));
                    command.Parameters.AddWithValue("owner", organization_id);
                    command.Parameters.AddWithValue("id", id);
                    var raw = await command.ExecuteScalarAsync(token) as string;
                    if (raw is null) return Failure(503, "hub_unavailable");
                    using var parsed = JsonDocument.Parse(raw);
                    var detail = parsed.RootElement;
                    if (detail.ValueKind != JsonValueKind.Object ||
                        !detail.TryGetProperty("id", out var resultId) ||
                        resultId.GetGuid() != id ||
                        !detail.TryGetProperty("organization_id", out var resultOwner) ||
                        resultOwner.GetString() != organization_id ||
                        !detail.TryGetProperty("body", out var resultBody) ||
                        resultBody.ValueKind != JsonValueKind.String ||
                        resultBody.GetString() is not { Length: > 0 and <= 20000 } ||
                        !detail.TryGetProperty("visuals", out var visuals) ||
                        visuals.ValueKind != JsonValueKind.Array ||
                        visuals.GetArrayLength() > 100)
                        return Failure(503, "hub_unavailable");
                    return Results.Content(raw, "application/json");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (PostgresException ex) when (ex.SqlState == "P0002")
                { return Failure(404, "hub_draft_not_found"); }
                catch (PostgresException ex) when (ex.SqlState == "42501")
                { return Failure(403, "not_authorized"); }
                catch (Exception) { return Failure(503, "hub_unavailable"); }
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("HUB administration")
            .WithName("GetAdminHubEditorialDetail")
            .Produces(200, contentType: "application/json")
            .Produces<WebAdminError>(400)
            .Produces<WebAdminError>(401)
            .Produces<WebAdminError>(403)
            .Produces<WebAdminError>(404)
            .Produces<WebAdminError>(422)
            .Produces<WebAdminError>(503);

        // A manager can send explicitly rejected text back for correction.
        endpoints.MapPost("/api/admin/hub/editorial/{id:guid}/return-for-revision",
            async (Guid id, string? organization_id, HubReviewSubmitCommand body,
                   WebAdminService admin, WebAdminConnections connections,
                   IWebAdminSecrets secrets, HttpContext context, CancellationToken token) =>
            {
                context.Response.Headers.CacheControl = "private, no-store";
                context.Response.Headers.Pragma = "no-cache";
                if (organization_id is null ||
                    context.Request.Query["organization_id"].Count != 1 ||
                    !PublicContentService.IsOrganization(organization_id))
                    return Failure(400, "invalid_organization");
                if (id == Guid.Empty || body.Version < 1 || body.Version == long.MaxValue)
                    return Failure(422, "invalid_hub_return");

                var cookie = context.Request.Cookies[WebAdminServices.CookieName];
                var session = await admin.CurrentAsync(cookie, token);
                if (session is null || !secrets.IsToken(cookie))
                    return Failure(401, "not_authenticated");
                if (!session.User.Organizations.Any(org => org.Id == organization_id) ||
                    session.User.Role is not ("manager" or "super_admin"))
                    return Failure(403, "not_authorized");
                try
                {
                    await using var connection = await connections.OpenAsync(token);
                    await using var command = new NpgsqlCommand(
                        "SELECT hub.return_rejected_admin_draft(@digest,@owner,@id,@version)::text", connection);
                    command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie!));
                    command.Parameters.AddWithValue("owner", organization_id);
                    command.Parameters.AddWithValue("id", id);
                    command.Parameters.AddWithValue("version", body.Version);
                    var raw = await command.ExecuteScalarAsync(token) as string;
                    if (raw is null) return Failure(503, "hub_unavailable");
                    using var parsed = JsonDocument.Parse(raw);
                    var result = parsed.RootElement;
                    if (!result.TryGetProperty("id", out var returnedId) || returnedId.GetGuid() != id ||
                        !result.TryGetProperty("organization_id", out var owner) || owner.GetString() != organization_id ||
                        !result.TryGetProperty("status", out var status) || status.GetString() != "ai_draft" ||
                        !result.TryGetProperty("version", out var version) || version.GetInt64() != body.Version + 1 ||
                        !result.TryGetProperty("requires_revision", out var required) || !required.GetBoolean())
                        return Failure(503, "hub_unavailable");
                    return Results.Content(raw, "application/json");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (PostgresException ex) when (ex.SqlState == "40001")
                { return Failure(409, "hub_return_conflict"); }
                catch (PostgresException ex) when (ex.SqlState == "P0002")
                { return Failure(404, "hub_draft_not_found"); }
                catch (PostgresException ex) when (ex.SqlState == "42501")
                { return Failure(403, "not_authorized"); }
                catch (Exception) { return Failure(503, "hub_unavailable"); }
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("HUB administration")
            .WithName("ReturnAdminHubDraftForRevision")
            .Produces(200, contentType: "application/json")
            .Produces<WebAdminError>(400)
            .Produces<WebAdminError>(401)
            .Produces<WebAdminError>(403)
            .Produces<WebAdminError>(404)
            .Produces<WebAdminError>(409)
            .Produces<WebAdminError>(422)
            .Produces<WebAdminError>(503);

        // Human image rejection only; image approval requires a separate
        // trusted malware scan and controlled moderation workflow.
        endpoints.MapPost("/api/admin/hub/editorial/{id:guid}/images/{mediaId:guid}/reject",
            async (Guid id, Guid mediaId, string? organization_id,
                   HubReviewSubmitCommand body, WebAdminService admin,
                   WebAdminConnections connections, IWebAdminSecrets secrets,
                   HttpContext context, CancellationToken token) =>
            {
                context.Response.Headers.CacheControl = "private, no-store";
                context.Response.Headers.Pragma = "no-cache";
                if (organization_id is null ||
                    context.Request.Query["organization_id"].Count != 1 ||
                    !PublicContentService.IsOrganization(organization_id))
                    return Failure(400, "invalid_organization");
                if (id == Guid.Empty || mediaId == Guid.Empty ||
                    body.Version < 1 || body.Version == long.MaxValue)
                    return Failure(422, "invalid_hub_visual_rejection");
                var cookie = context.Request.Cookies[WebAdminServices.CookieName];
                var session = await admin.CurrentAsync(cookie, token);
                if (session is null || !secrets.IsToken(cookie))
                    return Failure(401, "not_authenticated");
                if (!session.User.Organizations.Any(org => org.Id == organization_id) ||
                    session.User.Role is not ("manager" or "super_admin"))
                    return Failure(403, "not_authorized");
                try
                {
                    await using var connection = await connections.OpenAsync(token);
                    await using var command = new NpgsqlCommand(
                        "SELECT hub.reject_admin_hub_image(@digest,@owner,@id,@version,@media)::text", connection);
                    command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie!));
                    command.Parameters.AddWithValue("owner", organization_id);
                    command.Parameters.AddWithValue("id", id);
                    command.Parameters.AddWithValue("version", body.Version);
                    command.Parameters.AddWithValue("media", mediaId);
                    var raw = await command.ExecuteScalarAsync(token) as string;
                    if (raw is null) return Failure(503, "hub_unavailable");
                    using var parsed = JsonDocument.Parse(raw);
                    var result = parsed.RootElement;
                    if (!result.TryGetProperty("id", out var returnedId) || returnedId.GetGuid() != id ||
                        !result.TryGetProperty("media_id", out var returnedMedia) || returnedMedia.GetGuid() != mediaId ||
                        !result.TryGetProperty("organization_id", out var owner) || owner.GetString() != organization_id ||
                        !result.TryGetProperty("status", out var status) || status.GetString() != "review_required" ||
                        !result.TryGetProperty("visual_review", out var decision) || decision.GetString() != "rejected" ||
                        !result.TryGetProperty("version", out var version) || version.GetInt64() != body.Version + 1)
                        return Failure(503, "hub_unavailable");
                    return Results.Content(raw, "application/json");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (PostgresException ex) when (ex.SqlState == "40001")
                { return Failure(409, "hub_visual_review_conflict"); }
                catch (PostgresException ex) when (ex.SqlState == "P0002")
                { return Failure(404, "hub_image_not_found"); }
                catch (PostgresException ex) when (ex.SqlState == "42501")
                { return Failure(403, "not_authorized"); }
                catch (Exception) { return Failure(503, "hub_unavailable"); }
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("HUB administration")
            .WithName("RejectAdminHubImage")
            .Produces(200, contentType: "application/json")
            .Produces<WebAdminError>(400)
            .Produces<WebAdminError>(401)
            .Produces<WebAdminError>(403)
            .Produces<WebAdminError>(404)
            .Produces<WebAdminError>(409)
            .Produces<WebAdminError>(422)
            .Produces<WebAdminError>(503);

        endpoints.MapPost("/api/admin/hub/editorial/{id:guid}/text-review",
            async (Guid id, string? organization_id, HubTextReviewCommand body,
                   WebAdminService admin, WebAdminConnections connections,
                   IWebAdminSecrets secrets, HttpContext context, CancellationToken token) =>
            {
                context.Response.Headers.CacheControl = "private, no-store";
                context.Response.Headers.Pragma = "no-cache";
                if (organization_id is null ||
                    context.Request.Query["organization_id"].Count != 1 ||
                    !PublicContentService.IsOrganization(organization_id))
                    return Failure(400, "invalid_organization");
                if (id == Guid.Empty || body.Version < 1 ||
                    body.Version == long.MaxValue ||
                    body.Decision is not ("passed" or "rejected"))
                    return Failure(422, "invalid_hub_text_review");

                var cookie = context.Request.Cookies[WebAdminServices.CookieName];
                var session = await admin.CurrentAsync(cookie, token);
                if (session is null || !secrets.IsToken(cookie))
                    return Failure(401, "not_authenticated");
                // Review is intentionally limited to management, not content
                // editors or marketing role, even when they can create drafts.
                if (!session.User.Organizations.Any(org => org.Id == organization_id) ||
                    session.User.Role is not ("manager" or "super_admin"))
                    return Failure(403, "not_authorized");
                try
                {
                    await using var connection = await connections.OpenAsync(token);
                    await using var command = new NpgsqlCommand(
                        "SELECT hub.review_admin_text(@digest,@owner,@id,@version,@decision)::text", connection);
                    command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie!));
                    command.Parameters.AddWithValue("owner", organization_id);
                    command.Parameters.AddWithValue("id", id);
                    command.Parameters.AddWithValue("version", body.Version);
                    command.Parameters.AddWithValue("decision", body.Decision);
                    var raw = await command.ExecuteScalarAsync(token) as string;
                    if (raw is null) return Failure(503, "hub_unavailable");
                    using var parsed = JsonDocument.Parse(raw);
                    var result = parsed.RootElement;
                    if (result.ValueKind != JsonValueKind.Object ||
                        !result.TryGetProperty("id", out var resultId) ||
                        resultId.GetGuid() != id ||
                        !result.TryGetProperty("organization_id", out var resultOwner) ||
                        resultOwner.GetString() != organization_id ||
                        !result.TryGetProperty("status", out var resultStatus) ||
                        resultStatus.GetString() != "review_required" ||
                        !result.TryGetProperty("version", out var version) ||
                        version.GetInt64() != body.Version + 1 ||
                        !result.TryGetProperty("text_review", out var textDecision) ||
                        textDecision.GetString() != body.Decision)
                        return Failure(503, "hub_unavailable");
                    return Results.Content(raw, "application/json");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (PostgresException ex) when (ex.SqlState == "40001")
                { return Failure(409, "hub_review_conflict"); }
                catch (PostgresException ex) when (ex.SqlState == "P0002")
                { return Failure(404, "hub_draft_not_found"); }
                catch (PostgresException ex) when (ex.SqlState == "42501")
                { return Failure(403, "not_authorized"); }
                catch (Exception) { return Failure(503, "hub_unavailable"); }
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("HUB administration")
            .WithName("ReviewAdminHubText")
            .Produces(200, contentType: "application/json")
            .Produces<WebAdminError>(400)
            .Produces<WebAdminError>(401)
            .Produces<WebAdminError>(403)
            .Produces<WebAdminError>(404)
            .Produces<WebAdminError>(409)
            .Produces<WebAdminError>(422)
            .Produces<WebAdminError>(503);

        return endpoints;
    }

    private static IResult Failure(int status, string code) =>
        Results.Json(new WebAdminError("The requested HUB content is unavailable.", code), statusCode: status);
}
