using System.Text;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Api.Attendance;

public static class AdminAcademyDrillEndpoints
{
    public sealed record DrillCommand(string Action,int ExpectedVersion,string? Title,
        string? Category,string? AgeBand,int? DurationMinutes,string? Objective,string? Instructions);

    public static IEndpointRouteBuilder MapAdminAcademyDrills(this IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/api/admin/academy/drills")
            .WithTags("Central Academy coaching library")
            .RequireAuthorization(WebAdminServices.Policy);
        group.MapGet("/",async (string? search,string? category,string? age,string? status,int? offset,
            WebAdminService auth,WebAdminConnections connections,IWebAdminSecrets secrets,
            HttpContext context,CancellationToken ct)=>{
            var cookie=await Authorized(auth,context,false,ct);
            if(new[]{"search","category","age","status","offset"}.Any(k=>context.Request.Query[k].Count>1)
                || context.Request.Query.Keys.Any(k=>k is not ("search" or "category" or "age" or "status" or "offset")))
                throw new WebAdminException(400,"invalid_drill_filters","Invalid central library filters.");
            await using var connection=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand("""
                SELECT club.read_central_academy_drills(@digest,@search,@category,@age,@status,@offset)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("search",search??"");
            command.Parameters.AddWithValue("category",category??"all");
            command.Parameters.AddWithValue("age",age??"any");
            command.Parameters.AddWithValue("status",status??"approved");
            command.Parameters.AddWithValue("offset",offset??0);
            return await Run(command,ct);
        }).WithName("ReadCentralAcademyDrills");

        group.MapPost("/",async (DrillCommand body,WebAdminService auth,WebAdminConnections connections,
            IWebAdminSecrets secrets,HttpContext context,CancellationToken ct)=>{
            var cookie=await Authorized(auth,context,true,ct);
            if(body.Action!="create"||body.ExpectedVersion!=0||context.Request.Query.Count!=0)
                throw new WebAdminException(422,"invalid_drill_create","Create a new private draft.");
            return await Write(Guid.NewGuid(),body,cookie,connections,secrets,ct,201);
        }).WithName("CreateCentralAcademyDrill").Accepts<DrillCommand>("application/json");

        group.MapPut("/{drillId:guid}",async (Guid drillId,DrillCommand body,WebAdminService auth,
            WebAdminConnections connections,IWebAdminSecrets secrets,HttpContext context,CancellationToken ct)=>{
            var cookie=await Authorized(auth,context,true,ct);
            if(drillId==Guid.Empty||body.ExpectedVersion is <1 or >9999||
                body.Action is not ("edit" or "approve" or "archive" or "restore")||context.Request.Query.Count!=0)
                throw new WebAdminException(422,"invalid_drill_change","Select the exact current drill version.");
            return await Write(drillId,body,cookie,connections,secrets,ct,200);
        }).WithName("ChangeCentralAcademyDrill").Accepts<DrillCommand>("application/json");
        return endpoints;
    }

    private static async Task<string> Authorized(WebAdminService auth,HttpContext context,bool write,CancellationToken ct)
    {
        context.Response.Headers.CacheControl="private, no-store";
        context.Response.Headers.Pragma="no-cache";
        var cookie=context.Request.Cookies[WebAdminServices.CookieName];
        var session=await auth.CurrentAsync(cookie,ct);
        if(session is null)
            throw new WebAdminException(401,"not_authenticated","Sign in again.");
        var role=session.User.Role;
        if(!session.User.Organizations.Any(x=>x.Id=="lefteria_fc_academy")||
            (write ? role!="super_admin"||!AdminPermissions.Has(role,"training:write")
                   : role is not ("super_admin" or "manager" or "coach")||
                     !(AdminPermissions.Has(role,"training:read")||AdminPermissions.Has(role,"training:read_assigned"))))
            throw new WebAdminException(403,"central_drill_denied","Academy coaching library access is not granted.");
        return cookie!;
    }

    private static async Task<IResult> Write(Guid id,DrillCommand body,string cookie,WebAdminConnections connections,
        IWebAdminSecrets secrets,CancellationToken ct,int status)
    {
        await using var connection=await connections.OpenAsync(ct);
        await using var command=new NpgsqlCommand("""
            SELECT club.write_central_academy_drill(@digest,@id,@expected,@action,@title,@category,@age,@duration,@objective,@instructions)::text
            """,connection);
        command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
        command.Parameters.AddWithValue("id",id);
        command.Parameters.AddWithValue("expected",body.ExpectedVersion);
        command.Parameters.AddWithValue("action",body.Action);
        command.Parameters.AddWithValue("title",NpgsqlDbType.Text,(object?)body.Title??DBNull.Value);
        command.Parameters.AddWithValue("category",NpgsqlDbType.Text,(object?)body.Category??DBNull.Value);
        command.Parameters.AddWithValue("age",NpgsqlDbType.Text,(object?)body.AgeBand??DBNull.Value);
        command.Parameters.AddWithValue("duration",NpgsqlDbType.Integer,(object?)body.DurationMinutes??DBNull.Value);
        command.Parameters.AddWithValue("objective",NpgsqlDbType.Text,(object?)body.Objective??DBNull.Value);
        command.Parameters.AddWithValue("instructions",NpgsqlDbType.Text,(object?)body.Instructions??DBNull.Value);
        return await Run(command,ct,status);
    }

    private static async Task<IResult> Run(NpgsqlCommand command,CancellationToken ct,int status=200)
    {
        try
        {
            var raw=await command.ExecuteScalarAsync(ct) as string;
            return raw is null?Results.Problem(statusCode:503):Results.Content(raw,"application/json",Encoding.UTF8,status);
        }
        catch(PostgresException e) when(e.SqlState is "42501" or "22023" or "23505" or "42883")
        {
            var code=e.SqlState switch {"42501"=>403,"22023"=>422,"23505"=>409,_=>503};
            throw new WebAdminException(code,"central_drill_unavailable","The central drill or requested revision is unavailable.");
        }
    }
}
