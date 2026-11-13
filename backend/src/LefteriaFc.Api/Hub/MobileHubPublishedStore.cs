using System.Text.Json;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Api.Hub;

public sealed record MobileHubArticle(
    Guid Id,string OrganizationId,string Title,string Body,
    DateTimeOffset PublishedAt,string Audience,short? AgeMin,short? AgeMax);
public sealed record MobileHubPublishedResult(
    string OrganizationId,string Audience,IReadOnlyList<MobileHubArticle> Items);

/// <summary>
/// No direct table access: the PostgreSQL SECURITY DEFINER reader validates
/// the currently active identity, scope, review state and audience per call.
/// </summary>
public sealed class MobileHubPublishedStore(LefteriaDbContext database)
{
    // Legacy catalogue compatibility: expose ONLY this member's current
    // approved lesson. No caller can page through the global published
    // archive or skip ahead before completing earlier lessons.
    public async Task<MobileHubPublishedResult> ReadAsync(
        MobileSession actor,string organization,int offset,CancellationToken token)
    {
        if(offset is <0 or >1000)
            throw new MobileAuthException(MobileAuthFaultKind.Validation,
                MobileAuthErrorCode.InvalidInput,"Invalid article offset.");
        var page=await NextAsync(actor,organization,token);
        var items=offset==0 && page.Next is { } article
            ? (IReadOnlyList<MobileHubArticle>)[new MobileHubArticle(
                article.Id,article.OrganizationId,article.Title,article.Body,
                article.PublishedAt,article.Audience,article.AgeMin,article.AgeMax)]
            : Array.Empty<MobileHubArticle>();
        return new MobileHubPublishedResult(page.OrganizationId,page.Audience,items);
    }

    // Persistent per-member lesson position, independent of registration date.
    // Unlike the published catalogue, this returns at most ONE next lesson.
    public async Task<MobileHubNextPage> NextAsync(
        MobileSession actor,string organization,CancellationToken token)
    {
        var role=CheckLearningScope(actor,organization);
        try
        {
            await database.Database.OpenConnectionAsync(token);
            try
            {
                await using var command=new NpgsqlCommand("""
                    SELECT hub.read_mobile_next_education(
                      @identity,@phone,@role,@expires,@owner)::text
                    """,(NpgsqlConnection)database.Database.GetDbConnection());
                SetLearningArguments(command,actor,role,organization);
                var raw=await command.ExecuteScalarAsync(token) as string;
                var result=raw is null?null:JsonSerializer.Deserialize<MobileHubNextPage>(
                    raw,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower});
                if(result is null || result.OrganizationId!=organization ||
                    result.Audience!=role || (result.Next is { } item &&
                    (item.Id==Guid.Empty || item.RevisionId==Guid.Empty ||
                     item.OrganizationId!=organization || item.Audience!=role ||
                     (item.Kind!="article" && !(role=="player" && item.Kind=="course")) ||
                     string.IsNullOrWhiteSpace(item.Title) || item.Title.Length>180 ||
                     string.IsNullOrWhiteSpace(item.Body) || item.Body.Length>20000 ||
                     item.PublishedAt==default || item.PublishedAt>DateTimeOffset.UtcNow ||
                     item.AgeMin is not null || item.AgeMax is not null)))
                    throw Unavailable();
                return result;
            }
            finally {await database.Database.CloseConnectionAsync();}
        }
        catch(PostgresException ex)
        {
            if(ex.SqlState=="42501")
                throw new MobileAuthException(MobileAuthFaultKind.Forbidden,
                    MobileAuthErrorCode.RoleUnavailable,"Learning access is no longer authorized.");
            throw Unavailable();
        }
    }

    public async Task<MobileHubCompletionResult> CompleteNextAsync(
        MobileSession actor,string organization,Guid contentId,Guid revisionId,
        CancellationToken token)
    {
        var role=CheckLearningScope(actor,organization);
        if(contentId==Guid.Empty || revisionId==Guid.Empty)
            throw new MobileAuthException(MobileAuthFaultKind.Validation,
                MobileAuthErrorCode.InvalidInput,"Invalid lesson completion.");
        try
        {
            await database.Database.OpenConnectionAsync(token);
            try
            {
                await using var command=new NpgsqlCommand("""
                    SELECT hub.complete_mobile_next_education(
                      @identity,@phone,@role,@expires,@owner,@content,@revision)::text
                    """,(NpgsqlConnection)database.Database.GetDbConnection());
                SetLearningArguments(command,actor,role,organization);
                command.Parameters.AddWithValue("content",contentId);
                command.Parameters.AddWithValue("revision",revisionId);
                var raw=await command.ExecuteScalarAsync(token) as string;
                var result=raw is null?null:JsonSerializer.Deserialize<MobileHubCompletionResult>(
                    raw,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower});
                if(result is null || result.OrganizationId!=organization ||
                    result.Audience!=role || result.CompletedId!=contentId ||
                    result.CompletedRevisionId!=revisionId)
                    throw Unavailable();
                return result;
            }
            finally {await database.Database.CloseConnectionAsync();}
        }
        catch(PostgresException ex)
        {
            throw ex.SqlState switch
            {
                "42501"=>new MobileAuthException(MobileAuthFaultKind.Forbidden,
                    MobileAuthErrorCode.RoleUnavailable,"Learning access is no longer authorized."),
                "40001" or "23505"=>new MobileAuthException(MobileAuthFaultKind.Conflict,
                    MobileAuthErrorCode.InvalidInput,"Lesson changed; refresh your pathway."),
                "22023"=>new MobileAuthException(MobileAuthFaultKind.Validation,
                    MobileAuthErrorCode.InvalidInput,"Invalid lesson completion."),
                _=>Unavailable()
            };
        }
    }

    private static string CheckLearningScope(MobileSession actor,string organization)
    {
        if(actor.Role is not (MobileRole.Parent or MobileRole.Player) ||
           organization is not ("lefteria_fc" or "lefteria_fc_academy"))
            throw new MobileAuthException(MobileAuthFaultKind.Forbidden,
                MobileAuthErrorCode.RoleUnavailable,"Learning audience is not available.");
        return actor.Role==MobileRole.Parent?"parent":"player";
    }

    private static void SetLearningArguments(NpgsqlCommand command,MobileSession actor,
        string role,string organization)
    {
        command.Parameters.AddWithValue("identity",actor.IdentityId);
        command.Parameters.AddWithValue("phone",actor.NormalizedPhone);
        command.Parameters.AddWithValue("role",role);
        command.Parameters.AddWithValue("expires",actor.ExpiresAt);
        command.Parameters.AddWithValue("owner",organization);
    }

    private static MobileAuthException Unavailable()=>new(
        MobileAuthFaultKind.ServiceUnavailable,MobileAuthErrorCode.AccountUnavailable,
        "Published education is temporarily unavailable.");
}

public sealed record MobileHubNextArticle(
    Guid Id,string OrganizationId,string Audience,string Kind,string Title,string Body,
    DateTimeOffset PublishedAt,Guid RevisionId,short? AgeMin,short? AgeMax);
public sealed record MobileHubNextPage(
    string OrganizationId,string Audience,MobileHubNextArticle? Next);
public sealed record MobileHubCompletionResult(
    string OrganizationId,string Audience,Guid CompletedId,Guid CompletedRevisionId);
