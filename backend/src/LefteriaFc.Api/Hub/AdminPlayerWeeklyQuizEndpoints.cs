using System.Text.Json;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Api.Hub;

public sealed record AdminPlayerQuizQuestion(
    string Prompt,IReadOnlyList<string> Choices,int CorrectIndex,string? Explanation);
public sealed record AdminPlayerQuizCreate(
    int WeekNumber,string Topic,string Position,string Title,
    IReadOnlyList<AdminPlayerQuizQuestion> Questions);

/// <summary>
/// Human-authenticated quiz drafting, independent review and publication.
/// No public, mobile or AI worker access to unpublished keys.
/// </summary>
public static class AdminPlayerWeeklyQuizEndpoints
{
    private static readonly JsonSerializerOptions BodyJson=new(){
        PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower};
    private static readonly string[] Topics=[
        "positioning","defending","attacking","transition","passing",
        "movement","teamwork","rules","decision_making","goalkeeping","general"];
    private static readonly string[] Positions=[
        "all","goalkeeper","defender","fullback","winger","midfielder","striker"];

    public static IEndpointRouteBuilder MapAdminPlayerWeeklyQuiz(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/hub/player-quizzes",
            async (string? organization_id,HttpContext context,
                   WebAdminService admin,WebAdminConnections connections,
                   IWebAdminSecrets secrets,CancellationToken token)=>
            {
                NoStore(context);
                var access=await AccessAsync(organization_id,context,admin,secrets,
                    false,false,token);
                if(access.Error is { } error)return error;
                return await RunAsync("list",access.Digest!,organization_id!,
                    Guid.Empty,null,connections,token);
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("HUB administration")
            .WithName("ListAdminPlayerWeeklyQuizzes")
            .Produces(200,contentType:"application/json")
            .Produces<WebAdminError>(400).Produces<WebAdminError>(401)
            .Produces<WebAdminError>(403).Produces<WebAdminError>(503);

        endpoints.MapGet("/api/admin/hub/player-quizzes/{id:guid}",
            async (Guid id,string? organization_id,HttpContext context,
                   WebAdminService admin,WebAdminConnections connections,
                   IWebAdminSecrets secrets,CancellationToken token)=>
            {
                NoStore(context);
                var access=await AccessAsync(organization_id,context,admin,secrets,
                    true,false,token);
                if(access.Error is { } error)return error;
                return await RunAsync("detail",access.Digest!,organization_id!,
                    id,null,connections,token);
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("HUB administration")
            .WithName("DetailAdminPlayerWeeklyQuiz")
            .Produces(200,contentType:"application/json")
            .Produces<WebAdminError>(400).Produces<WebAdminError>(401)
            .Produces<WebAdminError>(403).Produces<WebAdminError>(404)
            .Produces<WebAdminError>(503);

        endpoints.MapPost("/api/admin/hub/player-quizzes",
            async (string? organization_id,AdminPlayerQuizCreate body,
                   HttpContext context,WebAdminService admin,
                   WebAdminConnections connections,IWebAdminSecrets secrets,
                   CancellationToken token)=>
            {
                NoStore(context);
                var access=await AccessAsync(organization_id,context,admin,secrets,
                    false,true,token);
                if(access.Error is { } error)return error;
                if(!ValidDraft(body))return Failure(422,"invalid_quiz_draft");
                return await RunAsync("create",access.Digest!,organization_id!,
                    Guid.NewGuid(),body,connections,token);
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("HUB administration")
            .WithName("CreateAdminPlayerWeeklyQuiz")
            .Produces(201,contentType:"application/json")
            .Produces<WebAdminError>(400).Produces<WebAdminError>(401)
            .Produces<WebAdminError>(403).Produces<WebAdminError>(409)
            .Produces<WebAdminError>(422).Produces<WebAdminError>(503);

        endpoints.MapPost("/api/admin/hub/player-quizzes/{id:guid}/approve",
            async (Guid id,string? organization_id,HttpContext context,
                   WebAdminService admin,WebAdminConnections connections,
                   IWebAdminSecrets secrets,CancellationToken token)=>
            {
                NoStore(context);
                var access=await AccessAsync(organization_id,context,admin,secrets,
                    true,true,token);
                if(access.Error is { } error)return error;
                return await RunAsync("approve",access.Digest!,organization_id!,
                    id,null,connections,token);
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("HUB administration")
            .WithName("ApproveAdminPlayerWeeklyQuiz")
            .Produces(200,contentType:"application/json")
            .Produces<WebAdminError>(400).Produces<WebAdminError>(401)
            .Produces<WebAdminError>(403).Produces<WebAdminError>(404)
            .Produces<WebAdminError>(409).Produces<WebAdminError>(503);

        endpoints.MapPost("/api/admin/hub/player-quizzes/{id:guid}/publish",
            async (Guid id,string? organization_id,HttpContext context,
                   WebAdminService admin,WebAdminConnections connections,
                   IWebAdminSecrets secrets,CancellationToken token)=>
            {
                NoStore(context);
                var access=await AccessAsync(organization_id,context,admin,secrets,
                    true,true,token);
                if(access.Error is { } error)return error;
                return await RunAsync("publish",access.Digest!,organization_id!,
                    id,null,connections,token);
            })
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("HUB administration")
            .WithName("PublishAdminPlayerWeeklyQuiz")
            .Produces(200,contentType:"application/json")
            .Produces<WebAdminError>(400).Produces<WebAdminError>(401)
            .Produces<WebAdminError>(403).Produces<WebAdminError>(404)
            .Produces<WebAdminError>(409).Produces<WebAdminError>(503);
        return endpoints;
    }

    private static async Task<(string? Digest,IResult? Error)> AccessAsync(
        string? organization,HttpContext context,WebAdminService admin,
        IWebAdminSecrets secrets,bool manager,bool write,CancellationToken token)
    {
        if(organization is null ||
            context.Request.Query["organization_id"].Count!=1 ||
            !PublicContentService.IsOrganization(organization))
            return (null,Failure(400,"invalid_organization"));
        var cookie=context.Request.Cookies[WebAdminServices.CookieName];
        var session=await admin.CurrentAsync(cookie,token);
        if(session is null || !secrets.IsToken(cookie))
            return (null,Failure(401,"not_authenticated"));
        if(!session.User.Organizations.Any(o=>o.Id==organization) ||
           (manager && session.User.Role is not ("manager" or "super_admin")) ||
           !AdminPermissions.Has(session.User.Role,write?"news:write":"news:read"))
            return (null,Failure(403,"not_authorized"));
        return (secrets.TokenDigest(cookie!),null);
    }

    private static bool ValidDraft(AdminPlayerQuizCreate? draft)
    {
        if(draft is null || draft.WeekNumber is <1 or >520 ||
           !Topics.Contains(draft.Topic) || !Positions.Contains(draft.Position) ||
           draft.Title is null || draft.Title.Length is <5 or >180 ||
           draft.Title!=draft.Title.Trim() || draft.Title.Any(char.IsControl) ||
           draft.Questions is null || draft.Questions.Count is <3 or >20)
            return false;
        return draft.Questions.All(q=>q is not null &&
            q.Prompt is {Length:>=10 and <=500} &&
            q.Prompt==q.Prompt.Trim() && !q.Prompt.Any(char.IsControl) &&
            q.Choices is {Count:4} &&
            q.Choices.All(c=>c is {Length:>=1 and <=500} &&
                c==c.Trim() && !c.Any(char.IsControl)) &&
            q.Choices.Distinct(StringComparer.Ordinal).Count()==4 &&
            q.CorrectIndex is >=0 and <=3 &&
            (q.Explanation is null ||
                (q.Explanation.Length<=2000 &&
                 !q.Explanation.Any(char.IsControl))));
    }

    private static async Task<IResult> RunAsync(
        string operation,string digest,string owner,Guid id,
        AdminPlayerQuizCreate? body,WebAdminConnections connections,
        CancellationToken token)
    {
        if(operation is not "list" && id==Guid.Empty)
            return Failure(422,"invalid_quiz_id");
        try
        {
            await using var connection=await connections.OpenAsync(token);
            string sql=operation switch {
                "list"=>"SELECT hub.list_admin_player_quizzes(@digest,@owner)::text",
                "detail"=>"SELECT hub.detail_admin_player_quiz(@digest,@owner,@id)::text",
                "create"=>"SELECT hub.create_admin_player_quiz(@digest,@owner,@id,@week,@topic,@position,@title,@questions)::text",
                "approve"=>"SELECT hub.approve_admin_player_quiz(@digest,@owner,@id)::text",
                "publish"=>"SELECT hub.publish_admin_player_quiz(@digest,@owner,@id)::text",
                _=>throw new InvalidOperationException("Unsupported quiz action.")
            };
            await using var command=new NpgsqlCommand(sql,connection);
            command.Parameters.AddWithValue("digest",digest);
            command.Parameters.AddWithValue("owner",owner);
            if(operation!="list")command.Parameters.AddWithValue("id",id);
            if(operation=="create"&&body is not null)
            {
                command.Parameters.AddWithValue("week",body.WeekNumber);
                command.Parameters.AddWithValue("topic",body.Topic);
                command.Parameters.AddWithValue("position",body.Position);
                command.Parameters.AddWithValue("title",body.Title);
                command.Parameters.Add("questions",NpgsqlDbType.Jsonb).Value=
                    JsonSerializer.Serialize(body.Questions,BodyJson);
            }
            var raw=await command.ExecuteScalarAsync(token) as string;
            if(raw is null)return Failure(503,"quiz_unavailable");
            using var parsed=JsonDocument.Parse(raw);
            var root=parsed.RootElement;
            if(root.ValueKind!=JsonValueKind.Object ||
               !root.TryGetProperty("organization_id",out var returnedOwner) ||
               returnedOwner.GetString()!=owner)
                return Failure(503,"quiz_unavailable");
            if(operation=="list")
            {
                if(!root.TryGetProperty("items",out var items) ||
                    items.ValueKind!=JsonValueKind.Array ||
                    items.GetArrayLength()>100)
                    return Failure(503,"quiz_unavailable");
            }
            else if(!root.TryGetProperty("id",out var returnedId) ||
                returnedId.GetGuid()!=id)
                return Failure(503,"quiz_unavailable");
            return Results.Content(raw,"application/json",statusCode:operation=="create"?201:200);
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
        catch(PostgresException ex)
        {
            return ex.SqlState switch {
                "42501"=>Failure(403,"not_authorized"),
                "P0002"=>Failure(404,"quiz_not_found"),
                "23505" or "23503" or "40001"=>Failure(409,"quiz_conflict"),
                "22023" or "23514"=>Failure(422,"invalid_quiz_draft"),
                _=>Failure(503,"quiz_unavailable")
            };
        }
        catch(Exception){return Failure(503,"quiz_unavailable");}
    }

    private static void NoStore(HttpContext context)
    {
        context.Response.Headers.CacheControl="private, no-store";
        context.Response.Headers.Pragma="no-cache";
    }
    private static IResult Failure(int status,string code)=>
        Results.Json(new WebAdminError(
            "The requested quiz operation is unavailable.",code),statusCode:status);
}
