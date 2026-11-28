using System.Text;
using System.Text.Json.Serialization;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;

namespace LefteriaFc.Api.Audiences;

public static class AdminGroupRenameEndpoints
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record RenameCommand(
        [property:JsonPropertyName("expected_name")] string ExpectedName,
        [property:JsonPropertyName("name")] string Name);

    public static IEndpointRouteBuilder MapAdminGroupRename(this IEndpointRouteBuilder app)
    {
        app.MapPut("/api/admin/football/groups/{groupId}/name",async (
            string groupId,string? organization_id,RenameCommand body,
            WebAdminService admin,WebAdminConnections connections,IWebAdminSecrets secrets,
            HttpContext context,CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl="private, no-store";
            context.Response.Headers.Pragma="no-cache";
            if(organization_id is not ("lefteria_fc" or "lefteria_fc_academy") ||
                context.Request.Query["organization_id"].Count!=1)
                throw new WebAdminException(400,"invalid_organization","Select exactly one organization.");
            var cookie=context.Request.Cookies[WebAdminServices.CookieName];
            var session=await admin.CurrentAsync(cookie,ct);
            if(session is null)
                throw new WebAdminException(401,"not_authenticated","Sign in again.");
            if(session.User.Role is not ("manager" or "super_admin") ||
                !AdminPermissions.Has(session.User.Role,organization_id=="lefteria_fc"?"teams:write":"academy:write") ||
                !session.User.Organizations.Any(o=>o.Id==organization_id))
                throw new WebAdminException(403,"group_rename_denied","Group access denied.");
            if(groupId.Length is <1 or >128 || !groupId.All(c=>char.IsAsciiLetterOrDigit(c)||c is '_' or '-') ||
                body.ExpectedName is null || body.ExpectedName.Length is <1 or >120 ||
                body.Name is null || body.Name.Length is <1 or >120 ||
                body.Name!=body.Name.Trim() || body.Name.Any(char.IsControl))
                throw new WebAdminException(422,"group_rename_invalid","Enter a valid group name.");
            await using var connection=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand(
                "SELECT club.rename_admin_group(@digest,@owner,@id,@expected,@name)::text",connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie!));
            command.Parameters.AddWithValue("owner",organization_id!);
            command.Parameters.AddWithValue("id",groupId);
            command.Parameters.AddWithValue("expected",body.ExpectedName);
            command.Parameters.AddWithValue("name",body.Name);
            try
            {
                var json=await command.ExecuteScalarAsync(ct) as string;
                return json is null?Results.Problem(statusCode:503):
                    Results.Content(json,"application/json",Encoding.UTF8,200);
            }
            catch(PostgresException ex) when(ex.SqlState is "23505" or "23514")
            {throw new WebAdminException(409,"group_rename_conflict","Name already used or changed. Reload and retry.");}
            catch(PostgresException ex) when(ex.SqlState is "42501")
            {throw new WebAdminException(403,"group_rename_denied","Group unavailable or access revoked.");}
            catch(PostgresException ex) when(ex.SqlState is "22023")
            {throw new WebAdminException(422,"group_rename_invalid","Invalid group name.");}
            catch(PostgresException ex) when(ex.SqlState is "42883" or "3F000")
            {throw new WebAdminException(503,"group_rename_not_enabled","Group rename is not yet enabled.");}
        }).RequireAuthorization(WebAdminServices.Policy).WithTags("Football administration")
          .WithName("RenameAdminFootballGroup").Accepts<RenameCommand>("application/json").ExcludeFromDescription();
        return app;
    }
}
