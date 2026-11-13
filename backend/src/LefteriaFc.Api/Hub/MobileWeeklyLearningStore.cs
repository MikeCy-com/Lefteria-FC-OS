using System.Text.Json;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Api.Hub;

public sealed record MobileWeeklyLearningJoin(string Audience);
public sealed record MobileWeeklyLearningEnrollment(
    string OrganizationId,string Audience,DateTimeOffset StartedAt,
    DateTimeOffset NextDueAt,int DeliveryWeek,string Status);
public sealed record MobileWeeklyLearningDelivery(
    Guid Id,int Week,Guid ContentId,string Title,string Body,
    DateTimeOffset DeliveredAt);
public sealed record MobileWeeklyLearningInbox(
    string OrganizationId,string Audience,bool Enrolled,
    DateTimeOffset? StartedAt,DateTimeOffset? NextDueAt,int? DeliveryWeek,
    string? Status,IReadOnlyList<MobileWeeklyLearningDelivery> Deliveries);

/// <summary>
/// Only narrow SECURITY DEFINER routines. Never use direct HUB table access
/// or trust a fan's selected content interest as a paid subscription.
/// </summary>
public sealed class MobileWeeklyLearningStore(LefteriaDbContext database)
{
    private static readonly JsonSerializerOptions Json=new()
    {
        PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public Task<MobileWeeklyLearningEnrollment> EnrollAsync(MobileSession actor,
        string organization,string audience,CancellationToken token)=>
        ExecuteAsync<MobileWeeklyLearningEnrollment>(
            "SELECT hub.enroll_mobile_weekly_learning(@identity,@phone,@role,@expires,@owner,@audience)::text",
            actor,organization,audience,token,
            result=>result.OrganizationId==organization && result.Audience==audience &&
                result.StartedAt!=default && result.StartedAt<=DateTimeOffset.UtcNow &&
                result.NextDueAt>=result.StartedAt &&
                result.DeliveryWeek is >=1 and <=5200 &&
                result.Status is "active" or "paused" or "cancelled");

    public Task<MobileWeeklyLearningInbox> InboxAsync(MobileSession actor,
        string organization,string audience,CancellationToken token)=>
        ExecuteAsync<MobileWeeklyLearningInbox>(
            "SELECT hub.read_mobile_weekly_learning(@identity,@phone,@role,@expires,@owner,@audience)::text",
            actor,organization,audience,token,
            result=>result.OrganizationId==organization && result.Audience==audience &&
                result.Deliveries is {Count:<=20} &&
                (!result.Enrolled || (
                    result.StartedAt is { } started && started!=default &&
                    started<=DateTimeOffset.UtcNow &&
                    result.NextDueAt is { } next && next>=started &&
                    result.DeliveryWeek is >=1 and <=5200 &&
                    result.Status is "active" or "paused" or "cancelled")) &&
                result.Deliveries.All(item=>item is not null &&
                    item.Id!=Guid.Empty && item.ContentId!=Guid.Empty &&
                    item.Week is >=1 and <=5200 &&
                    !string.IsNullOrWhiteSpace(item.Title) && item.Title.Length<=180 &&
                    !string.IsNullOrWhiteSpace(item.Body) && item.Body.Length<=20000 &&
                    item.DeliveredAt!=default &&
                    item.DeliveredAt<=DateTimeOffset.UtcNow));

    private async Task<T> ExecuteAsync<T>(string statement,MobileSession actor,
        string organization,string audience,CancellationToken token,
        Func<T,bool> validate)
    {
        ValidateAudience(actor,organization,audience);
        try
        {
            await database.Database.OpenConnectionAsync(token);
            try
            {
                await using var command=new NpgsqlCommand(statement,
                    (NpgsqlConnection)database.Database.GetDbConnection());
                command.Parameters.AddWithValue("identity",actor.IdentityId);
                command.Parameters.AddWithValue("phone",actor.NormalizedPhone);
                command.Parameters.AddWithValue("role",actor.Role switch
                {
                    MobileRole.Parent=>"parent",
                    MobileRole.Player=>"player",
                    MobileRole.Fan=>"fan",
                    _=>throw new MobileAuthException(MobileAuthFaultKind.Forbidden,
                        MobileAuthErrorCode.RoleUnavailable,"Learning role unavailable.")
                });
                command.Parameters.AddWithValue("expires",actor.ExpiresAt);
                command.Parameters.AddWithValue("owner",organization);
                command.Parameters.AddWithValue("audience",audience);
                var raw=await command.ExecuteScalarAsync(token) as string;
                var result=raw is null?default:JsonSerializer.Deserialize<T>(raw,Json);
                if(result is null || !validate(result))throw Unavailable();
                return result;
            }
            finally {await database.Database.CloseConnectionAsync();}
        }
        catch(PostgresException ex)
        {
            if(ex.SqlState=="42501")
                throw new MobileAuthException(MobileAuthFaultKind.Forbidden,
                    MobileAuthErrorCode.RoleUnavailable,
                    "Verified membership or active Payabl subscription required.");
            throw Unavailable();
        }
    }

    private static void ValidateAudience(MobileSession actor,string org,string audience)
    {
        if(org is not ("lefteria_fc" or "lefteria_fc_academy") ||
            !(actor.Role switch
            {
                MobileRole.Parent=>audience=="parent",
                MobileRole.Player=>audience=="player",
                MobileRole.Fan=>audience is "fan_parent" or "fan_player",
                _=>false
            }))
            throw new MobileAuthException(MobileAuthFaultKind.Forbidden,
                MobileAuthErrorCode.RoleUnavailable,"Learning audience unavailable.");
    }

    private static MobileAuthException Unavailable()=>new(
        MobileAuthFaultKind.ServiceUnavailable,MobileAuthErrorCode.AccountUnavailable,
        "Weekly education service temporarily unavailable.");
}
