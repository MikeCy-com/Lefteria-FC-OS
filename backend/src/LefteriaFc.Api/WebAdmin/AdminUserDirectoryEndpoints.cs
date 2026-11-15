using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Infrastructure.WebAdmin;

namespace LefteriaFc.Api.WebAdmin;

/// <summary>Read-only and strictly super-admin-only identity summary.
/// No password hashes, phone numbers, invite secrets or account mutations.
/// This uses the separately credentialed web-admin DB connection.</summary>
public static class AdminUserDirectoryEndpoints
{
    public static IEndpointRouteBuilder MapAdminUserDirectory(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/access/users", async (
            int? offset, int? limit, HttpContext context, WebAdminService service,
            WebAdminConnections connections, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers.Pragma = "no-cache";

            var session=await service.CurrentAsync(context.Request.Cookies[WebAdminServices.CookieName], token);
            if(session is null)
                return Results.Json(new WebAdminError("Sign in again.", "not_authenticated"),statusCode:401);
            if(session.User.Role!="super_admin")
                return Results.Json(new WebAdminError("Super admin access required.", "not_authorized"),statusCode:403);
            if(context.Request.Query["offset"].Count>1 || context.Request.Query["limit"].Count>1 ||
                offset is <0 or >20000 || limit is <1 or >50)
                return Results.Json(new WebAdminError("Invalid page.", "invalid_filters"),statusCode:400);

            var take=limit??20;
            var skip=offset??0;
            await using var connection=await connections.OpenAsync(token);
            await using var command=connection.CreateCommand();
            command.CommandText="""
                SELECT a.id,a.display_name,a.role,a.is_active,a.invite_pending,
                    COALESCE(array_agg(g.organization_id ORDER BY g.organization_id)
                      FILTER (WHERE g.organization_id IN ('lefteria_fc','lefteria_fc_academy')),ARRAY[]::text[]) AS organizations,
                    (count(*) OVER ())::integer AS total
                FROM identity.admin_accounts a
                LEFT JOIN identity.admin_organization_grants g ON g.admin_account_id=a.id
                GROUP BY a.id,a.display_name,a.role,a.is_active,a.invite_pending
                ORDER BY lower(a.display_name),a.id
                LIMIT @take OFFSET @skip
                """;
            command.Parameters.AddWithValue("take",take);
            command.Parameters.AddWithValue("skip",skip);
            var accounts=new List<object>();
            int total=0;
            await using (var reader=await command.ExecuteReaderAsync(token))
            {
                while(await reader.ReadAsync(token))
                {
                    total=reader.GetInt32(6);
                    accounts.Add(new {
                        id=reader.GetGuid(0),
                        name=reader.GetString(1),
                        role=reader.GetString(2),
                        active=reader.GetBoolean(3),
                        invitation_pending=reader.GetBoolean(4),
                        organizations=reader.GetFieldValue<string[]>(5)
                    });
                }
            }
            // A page past the end still reports the authoritative total.
            if(accounts.Count==0 && skip>0)
            {
                await using var count=connection.CreateCommand();
                count.CommandText="SELECT count(*)::integer FROM identity.admin_accounts";
                total=Convert.ToInt32(await count.ExecuteScalarAsync(token));
            }
            return Results.Json(new {offset=skip,limit=take,total,accounts});
        })
        .RequireAuthorization(WebAdminServices.Policy)
        .WithName("GetAdminUsersDirectory")
        .WithTags("Web administration")
        // Do not silently change the frozen canonical API contract in this slice.
        // A versioned OpenAPI contract update is a separate integration review.
        .ExcludeFromDescription();
        return endpoints;
    }
}
