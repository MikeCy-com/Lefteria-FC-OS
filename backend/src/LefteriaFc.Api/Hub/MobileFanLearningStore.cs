using System.Text.Json;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Api.Hub;

public sealed record MobileFanNextArticle(
    Guid Id,Guid RevisionId,string OrganizationId,string Audience,
    string Title,string Body,DateTimeOffset PublishedAt);
public sealed record MobileFanNextPage(
    string OrganizationId,string Audience,MobileFanNextArticle? Next);
public sealed record MobileFanLessonCompletion(
    string OrganizationId,string Audience,Guid CompletedId,Guid CompletedRevisionId);
public sealed record MobileFanLessonCompleteBody(
    string OrganizationId,string Audience,Guid ContentId,Guid RevisionId);

/// <summary>
/// Fan lessons are never exposed by mere interest selection. A currently
/// captured and unexpired Payabl subscription is rechecked inside PostgreSQL
/// on every read and explicit completion, not trusted from JWT/UI state.
/// </summary>
public sealed class MobileFanLearningStore(LefteriaDbContext database)
{
    private static readonly JsonSerializerOptions Json=new(){
        PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower};

    public async Task<MobileFanNextPage> NextAsync(
        MobileSession actor,string owner,string audience,CancellationToken ct)
    {
        Check(actor,owner,audience);
        try
        {
            var result=await ExecuteAsync<MobileFanNextPage>(
                "read",actor,owner,audience,Guid.Empty,Guid.Empty,ct);
            if(result is null || result.OrganizationId!=owner ||
               result.Audience!=audience ||
               (result.Next is { } article &&
                (article.Id==Guid.Empty || article.RevisionId==Guid.Empty ||
                 article.OrganizationId!=owner || article.Audience!=audience ||
                 string.IsNullOrWhiteSpace(article.Title) || article.Title.Length>180 ||
                 string.IsNullOrWhiteSpace(article.Body) || article.Body.Length>20000 ||
                 article.PublishedAt==default || article.PublishedAt>DateTimeOffset.UtcNow)))
                throw Unavailable();
            return result;
        }
        catch(PostgresException ex)when(ex.SqlState=="42501"){throw Forbidden();}
        catch(PostgresException){throw Unavailable();}
    }

    public async Task<MobileFanLessonCompletion> CompleteAsync(
        MobileSession actor,string owner,string audience,Guid contentId,
        Guid revisionId,CancellationToken ct)
    {
        Check(actor,owner,audience);
        if(contentId==Guid.Empty||revisionId==Guid.Empty)
            throw new MobileAuthException(MobileAuthFaultKind.Validation,
                MobileAuthErrorCode.InvalidInput,"Invalid fan lesson completion.");
        try
        {
            var result=await ExecuteAsync<MobileFanLessonCompletion>(
                "complete",actor,owner,audience,contentId,revisionId,ct);
            if(result is null || result.OrganizationId!=owner ||
               result.Audience!=audience || result.CompletedId!=contentId ||
               result.CompletedRevisionId!=revisionId)
                throw Unavailable();
            return result;
        }
        catch(PostgresException ex)
        {
            throw ex.SqlState switch {
                "42501"=>Forbidden(),
                "40001" or "23505"=>new MobileAuthException(
                    MobileAuthFaultKind.Conflict,MobileAuthErrorCode.InvalidInput,
                    "Lesson no longer current; refresh your subscription."),
                "22023"=>new MobileAuthException(
                    MobileAuthFaultKind.Validation,MobileAuthErrorCode.InvalidInput,
                    "Invalid fan lesson completion."),
                _=>Unavailable()
            };
        }
    }

    private async Task<T?> ExecuteAsync<T>(
        string operation,MobileSession actor,string owner,string audience,
        Guid content,Guid revision,CancellationToken ct)
    {
        await database.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command=new NpgsqlCommand(operation=="read"
                ? "SELECT hub.read_mobile_fan_next_lesson(@identity,@phone,@role,@expires,@owner,@audience)::text"
                : "SELECT hub.complete_mobile_fan_next_lesson(@identity,@phone,@role,@expires,@owner,@audience,@content,@revision)::text",
                (NpgsqlConnection)database.Database.GetDbConnection());
            command.Parameters.AddWithValue("identity",actor.IdentityId);
            command.Parameters.AddWithValue("phone",actor.NormalizedPhone);
            command.Parameters.AddWithValue("role","fan");
            command.Parameters.AddWithValue("expires",actor.ExpiresAt);
            command.Parameters.AddWithValue("owner",owner);
            command.Parameters.AddWithValue("audience",audience);
            if(operation=="complete"){
                command.Parameters.AddWithValue("content",content);
                command.Parameters.AddWithValue("revision",revision);
            }
            var value=await command.ExecuteScalarAsync(ct) as string;
            return value is null?default:JsonSerializer.Deserialize<T>(value,Json);
        }
        finally{await database.Database.CloseConnectionAsync();}
    }

    private static void Check(MobileSession actor,string owner,string audience)
    {
        if(actor.Role!=MobileRole.Fan ||
           owner is not ("lefteria_fc" or "lefteria_fc_academy") ||
           audience is not ("parent" or "player"))
            throw Forbidden();
    }
    private static MobileAuthException Forbidden()=>new(
        MobileAuthFaultKind.Forbidden,MobileAuthErrorCode.RoleUnavailable,
        "An active verified fan HUB subscription is required.");
    private static MobileAuthException Unavailable()=>new(
        MobileAuthFaultKind.ServiceUnavailable,MobileAuthErrorCode.AccountUnavailable,
        "Fan learning is currently unavailable.");
}
