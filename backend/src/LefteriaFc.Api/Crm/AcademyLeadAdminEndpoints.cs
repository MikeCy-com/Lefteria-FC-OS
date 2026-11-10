using System.Text;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Api.Crm;

public sealed record AcademyLeadFollowUpCommand(string? ExpectedStatus, string? NextStatus);

/// <summary>
/// Private manager-only Academy enquiries. The website may accept a lead,
/// but neither this panel nor a marketing role may register a child, send
/// messages or fabricate a booked training session.
/// </summary>
public static class AcademyLeadAdminEndpoints
{
    public static IEndpointRouteBuilder MapAcademyLeadAdmin(this IEndpointRouteBuilder app)
    {
        var group=app.MapGroup("/api/admin/crm/academy/leads")
            .WithTags("Academy CRM").RequireAuthorization(WebAdminServices.Policy);

        group.MapGet("/", async (
            string? organization_id,int? offset,int? limit,string? status,
            WebAdminService admin,WebAdminConnections db,
            IWebAdminSecrets secrets,HttpContext context,CancellationToken ct) =>
        {
            var cookie=await AuthorizedAsync(organization_id,admin,context,false,ct);
            if(context.Request.Query["offset"].Count>1 ||
                context.Request.Query["limit"].Count>1 ||
                context.Request.Query["status"].Count>1 ||
                offset is < 0 or > 20000 || limit is < 1 or > 50 ||
                (status is not null && !States.Contains(status)))
                throw new WebAdminException(400,"invalid_filters","Invalid CRM filters.");

            await using var connection=await db.OpenAsync(ct);
            await using var query=new NpgsqlCommand("""
                SELECT marketing.read_admin_academy_leads(
                  @digest,@owner,@offset,@limit,@status)::text
                """,connection);
            query.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            query.Parameters.AddWithValue("owner",organization_id!);
            query.Parameters.AddWithValue("offset",offset??0);
            query.Parameters.AddWithValue("limit",limit??25);
            query.Parameters.AddWithValue("status",NpgsqlDbType.Text,
                status is null ? DBNull.Value : status);
            try
            {
                var json=await query.ExecuteScalarAsync(ct) as string;
                return json is null ? Results.Problem(statusCode:503)
                    : Results.Content(json,"application/json",Encoding.UTF8,200);
            }
            catch(PostgresException e) when(e.SqlState is "42501" or "22023")
            {
                throw new WebAdminException(e.SqlState=="42501"?403:400,
                    "crm_read_denied","Academy CRM access denied.");
            }
        }).WithName("ReadAdminAcademyLeads");

        group.MapPost("/{leadId:guid}/status",async (
            Guid leadId,string? organization_id,AcademyLeadFollowUpCommand body,
            WebAdminService admin,WebAdminConnections db,
            IWebAdminSecrets secrets,HttpContext context,CancellationToken ct) =>
        {
            var cookie=await AuthorizedAsync(organization_id,admin,context,true,ct);
            if(leadId==Guid.Empty || body.ExpectedStatus is null ||
               body.NextStatus is null ||
               !((body.ExpectedStatus=="new" &&
                     body.NextStatus is "contacted" or "lost") ||
                 (body.ExpectedStatus=="contacted"&&body.NextStatus=="lost")))
                throw new WebAdminException(422,"invalid_crm_transition",
                    "Only confirmed contact and lost-interest transitions are enabled.");

            await using var connection=await db.OpenAsync(ct);
            await using var query=new NpgsqlCommand("""
                SELECT marketing.update_admin_academy_lead_status(
                  @digest,@owner,@id,@expected,@next)::text
                """,connection);
            query.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            query.Parameters.AddWithValue("owner",organization_id!);
            query.Parameters.AddWithValue("id",leadId);
            query.Parameters.AddWithValue("expected",body.ExpectedStatus);
            query.Parameters.AddWithValue("next",body.NextStatus);
            try
            {
                var json=await query.ExecuteScalarAsync(ct) as string;
                return json is null ? Results.Problem(statusCode:503)
                    : Results.Content(json,"application/json",Encoding.UTF8,200);
            }
            catch(PostgresException e) when(e.SqlState is "42501" or "22023" or "23505")
            {
                throw new WebAdminException(e.SqlState=="23505"?409:
                    e.SqlState=="42501"?403:422,"crm_write_denied",
                    "The lead changed or the requested transition is unavailable.");
            }
        }).WithName("UpdateAdminAcademyLeadStatus")
          .Accepts<AcademyLeadFollowUpCommand>("application/json");

        return app;
    }

    private static readonly HashSet<string> States=[
        "new","contacted","trial_booked","registered","lost"
    ];

    private static async Task<string> AuthorizedAsync(
        string? organization,WebAdminService service,
        HttpContext context,bool write,CancellationToken ct)
    {
        context.Response.Headers.CacheControl="private, no-store";
        context.Response.Headers.Pragma="no-cache";
        if(organization!="lefteria_fc_academy" ||
            context.Request.Query["organization_id"].Count!=1)
            throw new WebAdminException(400,"invalid_organization",
                "Choose exactly one Academy organization.");
        var cookie=context.Request.Cookies[WebAdminServices.CookieName];
        var session=await service.CurrentAsync(cookie,ct);
        if(session is null)
            throw new WebAdminException(401,"not_authenticated","Sign in again.");
        if(session.User.Role is not ("manager" or "super_admin") ||
           !AdminPermissions.Has(session.User.Role,write?"marketing:write":"marketing:read") ||
           !session.User.Organizations.Any(org=>org.Id==organization))
            throw new WebAdminException(403,"not_authorized",
                "Only authorized Academy managers can view private enquiries.");
        return cookie!;
    }
}
