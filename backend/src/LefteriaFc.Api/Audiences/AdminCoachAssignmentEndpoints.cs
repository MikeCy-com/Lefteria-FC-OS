using System.Text.Json;
using System.Text.Json.Serialization;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.Audiences;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;

namespace LefteriaFc.Api.Audiences;

public static class AdminCoachAssignmentEndpoints
{
    private static readonly JsonSerializerOptions Json=new()
    {
        PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower,
        NumberHandling=JsonNumberHandling.Strict,
        UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,
        MaxDepth=6
    };
    public static IEndpointRouteBuilder MapAdminCoachAssignments(this IEndpointRouteBuilder endpoints)
    {
        var route=endpoints.MapPut("/api/admin/football/coach-assignments",
            async (string? organization_id,WebAdminService admin,
                IAdminCoachAssignmentWriter writer,HttpContext context,CancellationToken token)=>
            {
                context.Response.Headers.CacheControl="private, no-store";
                if(organization_id is null || !PublicContentService.IsOrganization(organization_id) ||
                    context.Request.Query["organization_id"].Count!=1)
                    throw new WebAdminException(400,"invalid_organization","Select exact organization.");
                var cookie=context.Request.Cookies[WebAdminServices.CookieName];
                var session=await admin.CurrentAsync(cookie,token);
                if(session is null)
                    throw new WebAdminException(401,"not_authenticated","Sign in again.");
                var capability=organization_id=="lefteria_fc"?"teams:write":"academy:write";
                if(!AdminPermissions.Has(session.User.Role,capability) ||
                    !session.User.Organizations.Any(x=>x.Id==organization_id))
                    throw new WebAdminException(403,"not_authorized","Coach assignment denied.");
                return Results.Ok(await writer.SaveAsync(cookie!,organization_id,
                    await Parse(context,token),token));
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("Football administration")
            .WithName("PutAdminCoachAssignment")
            .Accepts<AdminCoachAssignmentCommand>("application/json")
            .Produces<AdminCoachAssignmentDto>(200);
        foreach(var status in new[]{400,401,403,409,413,415,422,503})
            route.Produces<WebAdminError>(status);
        return endpoints;
    }
    private static async Task<AdminCoachAssignmentCommand> Parse(HttpContext context,CancellationToken token)
    {
        try
        {
            using var doc=await JsonDocument.ParseAsync(context.Request.Body,
                new JsonDocumentOptions{MaxDepth=6},token);
            if(doc.RootElement.ValueKind!=JsonValueKind.Object)throw new JsonException();
            var keys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var property in doc.RootElement.EnumerateObject())
                if(!keys.Add(property.Name))throw new JsonException();
            return doc.Deserialize<AdminCoachAssignmentCommand>(Json)??throw new JsonException();
        }
        catch(JsonException)
        {throw new WebAdminException(400,"invalid_json","Provide one valid coach assignment JSON object.");}
    }
}
