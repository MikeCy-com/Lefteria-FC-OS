using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Api.Audiences;

/// <summary>
/// Original-field, one-transaction creation. A request ID is retained across
/// network retries so a dropped response cannot create another group.
/// </summary>
public static class AdminProfiledGroupCreateEndpoints
{
    public sealed record TeamProfile(
        [property:JsonPropertyName("level")] string Level,
        [property:JsonPropertyName("description")] string Description,
        [property:JsonPropertyName("banner_url")] string BannerUrl);
    public sealed record AcademyProfile(
        [property:JsonPropertyName("age_range")] string AgeRange,
        [property:JsonPropertyName("coach_name")] string CoachName,
        [property:JsonPropertyName("training_schedule")] string TrainingSchedule,
        [property:JsonPropertyName("description")] string Description,
        [property:JsonPropertyName("max_players")] int MaxPlayers,
        [property:JsonPropertyName("season")] string Season,
        [property:JsonPropertyName("banner_url")] string BannerUrl,
        [property:JsonPropertyName("display_order")] int DisplayOrder);
    public sealed record Create(
        [property:JsonPropertyName("name")] string Name,
        [property:JsonPropertyName("request_id")] Guid RequestId,
        [property:JsonPropertyName("team_profile")] TeamProfile? TeamProfile,
        [property:JsonPropertyName("academy_profile")] AcademyProfile? AcademyProfile);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        MaxDepth = 6
    };

    public static IEndpointRouteBuilder MapAdminProfiledGroupCreates(
        this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/admin/football/groups/with-profile",
            async (string? organization_id, WebAdminService admin,
                WebAdminConnections connections, IWebAdminSecrets secrets,
                HttpContext context, CancellationToken ct) =>
            {
                context.Response.Headers.CacheControl = "private, no-store";
                context.Response.Headers.Pragma = "no-cache";
                if (context.Request.Query["organization_id"].Count != 1 ||
                    organization_id is not ("lefteria_fc" or "lefteria_fc_academy"))
                    throw new WebAdminException(400,"invalid_organization",
                        "Select one exact First Team or Academy organization.");
                var cookie=context.Request.Cookies[WebAdminServices.CookieName];
                var session=await admin.CurrentAsync(cookie,ct);
                if(session is null)
                    throw new WebAdminException(401,"not_authenticated","Sign in again.");
                var permission=organization_id=="lefteria_fc" ?
                    "teams:write" : "academy:write";
                if(session.User.Role is not ("manager" or "super_admin") ||
                    !AdminPermissions.Has(session.User.Role,permission) ||
                    !session.User.Organizations.Any(o=>o.Id==organization_id))
                    throw new WebAdminException(403,"not_authorized",
                        "You cannot create groups for this organization.");

                Create request;
                try
                {
                    using var doc=await JsonDocument.ParseAsync(context.Request.Body,
                        new JsonDocumentOptions{MaxDepth=6},ct);
                    if(doc.RootElement.ValueKind!=JsonValueKind.Object)
                        throw new JsonException();
                    ValidateUniqueKeys(doc.RootElement);
                    request=doc.Deserialize<Create>(Json) ?? throw new JsonException();
                }
                catch(JsonException)
                {
                    throw new WebAdminException(400,"invalid_group_json",
                        "Provide one valid original-profiled group JSON object.");
                }
                if(request.RequestId==Guid.Empty ||
                    request.Name is not {Length:>=1 and <=120} ||
                    request.Name!=request.Name.Trim() ||
                    request.Name.Any(char.IsControl) ||
                    (organization_id=="lefteria_fc" ?
                        request.TeamProfile is null || request.AcademyProfile is not null :
                        request.AcademyProfile is null || request.TeamProfile is not null))
                    throw new WebAdminException(422,"invalid_group_profile",
                        "Group name, selected organization or profile is invalid.");

                var profile=organization_id=="lefteria_fc" ?
                    JsonSerializer.Serialize(request.TeamProfile,Json) :
                    JsonSerializer.Serialize(request.AcademyProfile,Json);
                var id="grp_"+Guid.NewGuid().ToString("N");
                await using var connection=await connections.OpenAsync(ct);
                await using var command=new NpgsqlCommand(
                    "SELECT club.create_admin_group_with_profile(@digest,@owner,@id,@name,@request,@profile)::text",
                    connection);
                command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie!));
                command.Parameters.AddWithValue("owner",organization_id);
                command.Parameters.AddWithValue("id",id);
                command.Parameters.AddWithValue("name",request.Name);
                command.Parameters.AddWithValue("request",request.RequestId);
                command.Parameters.AddWithValue("profile",NpgsqlDbType.Jsonb,profile);
                try
                {
                    var saved=await command.ExecuteScalarAsync(ct) as string;
                    if(saved is null)throw new WebAdminException(503,"group_unavailable",
                        "Creation could not be verified.");
                    using var output=JsonDocument.Parse(saved);
                    var root=output.RootElement;
                    if(root.GetProperty("name").GetString()!=request.Name ||
                        root.GetProperty("kind").GetString()!=(organization_id=="lefteria_fc"?"team":"academy_group") ||
                        root.GetProperty("active_players").GetInt32()!=0 ||
                        root.GetProperty("profile").ValueKind!=JsonValueKind.Object ||
                        !root.GetProperty("id").GetString()!.StartsWith("grp_",StringComparison.Ordinal))
                        throw new WebAdminException(503,"group_response_invalid",
                            "The created group response is invalid.");
                    return Results.Content(saved,"application/json",Encoding.UTF8,200);
                }
                catch(PostgresException ex) when(ex.SqlState is "23505" or "23514")
                {
                    throw new WebAdminException(409,"group_creation_conflict",
                        "Group request conflicts with existing data. Refresh before retrying.");
                }
                catch(PostgresException ex) when(ex.SqlState=="42501")
                {
                    throw new WebAdminException(403,"group_creation_denied",
                        "Group access was revoked or is unavailable.");
                }
                catch(PostgresException ex) when(ex.SqlState=="22023")
                {
                    throw new WebAdminException(422,"invalid_group_profile",
                        "The group details are invalid.");
                }
                catch(PostgresException ex) when(ex.SqlState is "42883" or "3F000" or "55000" or "54000")
                {
                    throw new WebAdminException(503,"group_creation_unavailable",
                        "Full group creation is not enabled.");
                }
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("Football administration")
            .WithName("CreateAdminProfiledGroup");
        return app;
    }

    private static void ValidateUniqueKeys(JsonElement node)
    {
        if(node.ValueKind==JsonValueKind.Object)
        {
            var keys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var item in node.EnumerateObject())
            {
                if(!keys.Add(item.Name))throw new JsonException("Duplicate field.");
                ValidateUniqueKeys(item.Value);
            }
        }
        else if(node.ValueKind==JsonValueKind.Array)
        {
            foreach(var item in node.EnumerateArray())
                ValidateUniqueKeys(item);
        }
    }
}