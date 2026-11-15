using System.Text;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;

namespace LefteriaFc.Api.Marketing;

/// <summary>
/// Server-authorized Academy intake queue. Only super-admins can triage
/// unassigned applications. This never returns encrypted medical/signature
/// evidence, raw consent records or private child documents.
/// </summary>
public static class AcademyIntakeAdminEndpoints
{
    public static IEndpointRouteBuilder MapAcademyIntakeAdmin(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/academy/intake",ReadAsync)
            .WithTags("Academy intake")
            .RequireAuthorization(WebAdminServices.Policy)
            .ExcludeFromDescription();
        return app;
    }

    private static async Task<IResult> ReadAsync(
        int? offset,int? limit,WebAdminService admin,
        WebAdminConnections db,IWebAdminSecrets secrets,
        HttpContext context,CancellationToken ct)
    {
        context.Response.Headers.CacheControl="private, no-store";
        context.Response.Headers.Pragma="no-cache";
        if(offset is < 0 or > 20000 || limit is < 1 or > 50 ||
           context.Request.Query.Keys.Any(key=>key is not ("offset" or "limit")) ||
           context.Request.Query["offset"].Count>1 ||
           context.Request.Query["limit"].Count>1)
            throw new WebAdminException(422,"invalid_intake_filters","Invalid intake pagination.");

        var cookie=context.Request.Cookies[WebAdminServices.CookieName];
        var session=await admin.CurrentAsync(cookie,ct);
        if(session is null)
            throw new WebAdminException(401,"not_authenticated","Sign in again.");
        if(session.User.Role!="super_admin" ||
           !AdminPermissions.Has(session.User.Role,"academy:read") ||
           !session.User.Organizations.Any(org=>org.Id=="lefteria_fc_academy"))
            throw new WebAdminException(403,"intake_access_denied","Academy super-admin access required.");

        await using var conn=await db.OpenAsync(ct);
        await using var cmd=new NpgsqlCommand("""
            SELECT academy_intake.read_admin_registration_queue(
              @digest,@limit,@offset)::text
            """,conn);
        cmd.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie!));
        cmd.Parameters.AddWithValue("limit",limit??25);
        cmd.Parameters.AddWithValue("offset",offset??0);
        try
        {
            var json=await cmd.ExecuteScalarAsync(ct) as string;
            return json is null ? Results.Problem(statusCode:503) :
                Results.Content(json,"application/json",Encoding.UTF8,200);
        }
        catch(PostgresException error) when(error.SqlState is "42501" or "22023")
        {
            throw new WebAdminException(error.SqlState=="42501"?403:422,
                "intake_read_denied","Academy intake access denied.");
        }
    }
}
