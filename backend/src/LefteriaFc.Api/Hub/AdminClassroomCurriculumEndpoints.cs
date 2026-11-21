using System.Text.Json;
using System.Text.Json.Serialization;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;

namespace LefteriaFc.Api.Hub;

public sealed record AdminClassroomSubjectChange(
    [property:JsonPropertyName("revision_id")] Guid RevisionId,
    [property:JsonPropertyName("expected_version")] long ExpectedVersion,
    [property:JsonPropertyName("topic")] string Topic,
    [property:JsonPropertyName("reason")] string Reason);

/// <summary>
/// Super Admin review of existing, fully approved player lessons; a topic
/// assignment never publishes, sequences or reveals the underlying text.
/// </summary>
public static class AdminClassroomCurriculumEndpoints
{
    private static readonly HashSet<string> Topics=[
        "unassigned","positioning","defending","attacking","transition",
        "passing","movement","teamwork","rules","decision_making",
        "goalkeeping","general"];

    public static IEndpointRouteBuilder MapAdminClassroomCurriculum(
        this IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/api/admin/hub/classroom-curriculum")
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("HUB administration")
            .ExcludeFromDescription();
        group.MapGet("",async(string? organization_id,int? offset,int? limit,
            HttpContext context,WebAdminService admin,WebAdminConnections connections,
            IWebAdminSecrets secrets,CancellationToken token)=>
        {
            NoStore(context);
            var access=await AuthorizeAsync(organization_id,context,admin,secrets,token);
            if(access.Error is { } error)return error;
            if(context.Request.Query["offset"].Count>1 ||
               context.Request.Query["limit"].Count>1 ||
               offset is <0 or >20000 || limit is <1 or >50)
                return Failure(400,"invalid_curriculum_page");
            return await RunAsync("list",access.Digest!,organization_id!,null,
                offset??0,limit??20,null,connections,token);
        });
        group.MapPut("/{contentId:guid}",async(Guid contentId,string? organization_id,
            AdminClassroomSubjectChange payload,HttpContext context,
            WebAdminService admin,WebAdminConnections connections,
            IWebAdminSecrets secrets,CancellationToken token)=>
        {
            NoStore(context);
            var access=await AuthorizeAsync(organization_id,context,admin,secrets,token);
            if(access.Error is { } error)return error;
            if(contentId==Guid.Empty||payload is null||payload.RevisionId==Guid.Empty||
               payload.ExpectedVersion<0||payload.ExpectedVersion>=long.MaxValue-1||
               !Topics.Contains(payload.Topic)||
               payload.Reason is null||payload.Reason.Length is <8 or >250||
               payload.Reason!=payload.Reason.Trim()||payload.Reason.Any(char.IsControl))
                return Failure(422,"invalid_curriculum_assignment");
            return await RunAsync("set",access.Digest!,organization_id!,contentId,
                0,0,payload,connections,token);
        });
        return endpoints;
    }

    private static async Task<(string? Digest,IResult? Error)> AuthorizeAsync(
        string? owner,HttpContext context,WebAdminService admin,
        IWebAdminSecrets secrets,CancellationToken token)
    {
        if(owner is null||!PublicContentService.IsOrganization(owner)||
           context.Request.Query["organization_id"].Count!=1)
            return (null,Failure(400,"invalid_organization"));
        var cookie=context.Request.Cookies[WebAdminServices.CookieName];
        var session=await admin.CurrentAsync(cookie,token);
        if(session is null||!secrets.IsToken(cookie))
            return (null,Failure(401,"not_authenticated"));
        if(session.User.Role!="super_admin"||
           !session.User.Organizations.Any(o=>o.Id==owner)||
           !AdminPermissions.Has(session.User.Role,"news:write"))
            return (null,Failure(403,"not_authorized"));
        return (secrets.TokenDigest(cookie!),null);
    }

    private static async Task<IResult> RunAsync(
        string action,string digest,string owner,Guid? content,
        int offset,int limit,AdminClassroomSubjectChange? payload,
        WebAdminConnections connections,CancellationToken token)
    {
        try
        {
            await using var connection=await connections.OpenAsync(token);
            await using var command=new NpgsqlCommand(action=="list"
                ? "SELECT hub.read_admin_classroom_curriculum(@digest,@owner,@offset,@limit)::text"
                : "SELECT hub.set_admin_classroom_lesson_subject(@digest,@owner,@content,@revision,@version,@topic,@reason)::text",
                connection);
            command.Parameters.AddWithValue("digest",digest);
            command.Parameters.AddWithValue("owner",owner);
            if(action=="list"){
                command.Parameters.AddWithValue("offset",offset);
                command.Parameters.AddWithValue("limit",limit);
            }else{
                command.Parameters.AddWithValue("content",content!.Value);
                command.Parameters.AddWithValue("revision",payload!.RevisionId);
                command.Parameters.AddWithValue("version",payload.ExpectedVersion);
                command.Parameters.AddWithValue("topic",payload.Topic);
                command.Parameters.AddWithValue("reason",payload.Reason);
            }
            var raw=await command.ExecuteScalarAsync(token) as string;
            if(raw is null)return Failure(503,"curriculum_unavailable");
            using var doc=JsonDocument.Parse(raw);
            var body=doc.RootElement;
            if(body.ValueKind!=JsonValueKind.Object ||
               !body.TryGetProperty("organization_id",out var resultOwner)||
               resultOwner.GetString()!=owner)return Failure(503,"curriculum_unavailable");
            if(action=="list"){
                if(!body.TryGetProperty("items",out var items)||
                   items.ValueKind!=JsonValueKind.Array||items.GetArrayLength()>limit||
                   !body.TryGetProperty("total",out var total)||
                   !total.TryGetInt32(out var totalNumber)||totalNumber<0||
                   !body.TryGetProperty("offset",out var cursor)||cursor.GetInt32()!=offset)
                    return Failure(503,"curriculum_unavailable");
            }else if(!body.TryGetProperty("id",out var id)||
                     id.GetString()!=content!.Value.ToString("D")||
                     !body.TryGetProperty("revision_id",out var revision)||
                     revision.GetString()!=payload!.RevisionId.ToString("D")||
                     !body.TryGetProperty("version",out var version)||
                     !version.TryGetInt64(out var number)||number<payload.ExpectedVersion)
                return Failure(503,"curriculum_unavailable");
            return Results.Content(raw,"application/json",statusCode:200);
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
        catch(PostgresException error){
            return error.SqlState switch{
                "42501"=>Failure(403,"not_authorized"),
                "P0002"=>Failure(404,"curriculum_not_published"),
                "40001"=>Failure(409,"curriculum_conflict"),
                "22023" or "23514"=>Failure(422,"invalid_curriculum_assignment"),
                _=>Failure(503,"curriculum_unavailable")
            };
        }
        catch(Exception){return Failure(503,"curriculum_unavailable");}
    }

    private static void NoStore(HttpContext context)
    {
        context.Response.Headers.CacheControl="private, no-store";
        context.Response.Headers.Pragma="no-cache";
    }
    private static IResult Failure(int status,string code)=>
        Results.Json(new WebAdminError("Curriculum operation unavailable.",code),
            statusCode:status);
}
