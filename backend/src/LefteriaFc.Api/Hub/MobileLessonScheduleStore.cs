using System.Text.Json;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Api.Hub;

public sealed record MobileLessonSchedule(
    string OrganizationId,string Audience,DateTimeOffset StartedAt,
    DateTimeOffset NextDueAt,int CadenceDays);
public sealed record MobileLessonDelivery(
    Guid Id,DateTimeOffset QueuedAt,string Type,string Audience,Guid ContentId);
public sealed record MobileLessonDeliveryInbox(
    string OrganizationId,string Audience,IReadOnlyList<MobileLessonDelivery> Items);

/// <summary>
/// A verified parent/player opts into independent weekly in-app delivery.
/// Notifications have no lesson text and are filtered against CURRENT
/// approval and roster scope, so withdrawn material is never delivered.
/// </summary>
public sealed class MobileLessonScheduleStore(LefteriaDbContext database)
{
    private static readonly JsonSerializerOptions Json=new(){
        PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower};

    public async Task<MobileLessonSchedule> EnrollAsync(
        MobileSession actor,string owner,CancellationToken token)
    {
        Check(actor,owner);
        var value=await ExecuteAsync<MobileLessonSchedule>(
            "start",actor,owner,token);
        if(value is null||value.OrganizationId!=owner||
            value.Audience!=Role(actor)||value.CadenceDays!=7||
            value.StartedAt==default||value.NextDueAt==default||
            value.StartedAt>DateTimeOffset.UtcNow||
            value.NextDueAt<value.StartedAt)
            throw Unavailable();
        return value;
    }

    public async Task<MobileLessonDeliveryInbox> InboxAsync(
        MobileSession actor,string owner,CancellationToken token)
    {
        Check(actor,owner);
        var value=await ExecuteAsync<MobileLessonDeliveryInbox>(
            "read",actor,owner,token);
        if(value is null||value.OrganizationId!=owner||
            value.Audience!=Role(actor)||value.Items is null||
            value.Items.Count>10||
            value.Items.Any(item=>item is null||item.Id==Guid.Empty||
                item.ContentId==Guid.Empty||item.Type!="lesson_ready"||
                item.Audience!=Role(actor)||item.QueuedAt==default||
                item.QueuedAt>DateTimeOffset.UtcNow))
            throw Unavailable();
        return value;
    }

    private async Task<T?> ExecuteAsync<T>(string action,MobileSession actor,
        string owner,CancellationToken token)
    {
        try
        {
            await database.Database.OpenConnectionAsync(token);
            try
            {
                await using var command=new NpgsqlCommand(action=="start"
                    ? "SELECT hub.start_mobile_lesson_schedule(@identity,@phone,@role,@expires,@owner)::text"
                    : "SELECT hub.read_mobile_lesson_deliveries(@identity,@phone,@role,@expires,@owner)::text",
                    (NpgsqlConnection)database.Database.GetDbConnection());
                command.Parameters.AddWithValue("identity",actor.IdentityId);
                command.Parameters.AddWithValue("phone",actor.NormalizedPhone);
                command.Parameters.AddWithValue("role",Role(actor));
                command.Parameters.AddWithValue("expires",actor.ExpiresAt);
                command.Parameters.AddWithValue("owner",owner);
                var raw=await command.ExecuteScalarAsync(token) as string;
                return raw is null?default:JsonSerializer.Deserialize<T>(raw,Json);
            }
            finally{await database.Database.CloseConnectionAsync();}
        }
        catch(PostgresException ex)
        {
            if(ex.SqlState=="42501")
                throw new MobileAuthException(MobileAuthFaultKind.Forbidden,
                    MobileAuthErrorCode.RoleUnavailable,
                    "Current learning delivery scope not authorized.");
            throw Unavailable();
        }
    }

    private static void Check(MobileSession actor,string owner)
    {
        if(actor.Role is not (MobileRole.Parent or MobileRole.Player)||
           owner is not ("lefteria_fc" or "lefteria_fc_academy"))
            throw new MobileAuthException(MobileAuthFaultKind.Forbidden,
                MobileAuthErrorCode.RoleUnavailable,"Verified learning role required.");
    }
    private static string Role(MobileSession actor)=>
        actor.Role==MobileRole.Parent?"parent":"player";
    private static MobileAuthException Unavailable()=>new(
        MobileAuthFaultKind.ServiceUnavailable,MobileAuthErrorCode.AccountUnavailable,
        "Scheduled learning is temporarily unavailable.");
}
