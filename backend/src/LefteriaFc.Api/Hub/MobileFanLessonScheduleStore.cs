using System.Text.Json;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Api.Hub;

public sealed record MobileFanLessonSchedule(
    string OrganizationId,string Audience,DateTimeOffset StartedAt,
    DateTimeOffset NextDueAt,int CadenceDays);
public sealed record MobileFanLessonDelivery(
    Guid Id,DateTimeOffset QueuedAt,string Type,string Audience,Guid ContentId);
public sealed record MobileFanLessonDeliveryPage(
    string OrganizationId,string Audience,IReadOnlyList<MobileFanLessonDelivery> Items);

/// <summary>
/// Fan scheduling cannot be configured by choosing an interest alone.
/// A fresh Payabl-confirmed entitlement gates enrollment and inbox reads.
/// </summary>
public sealed class MobileFanLessonScheduleStore(LefteriaDbContext database)
{
    private static readonly JsonSerializerOptions Json=new(){
        PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower};

    public async Task<MobileFanLessonSchedule> EnrollAsync(
        MobileSession actor,string owner,string audience,CancellationToken ct)
    {
        Check(actor,owner,audience);
        var value=await ExecuteAsync<MobileFanLessonSchedule>(
            "start",actor,owner,audience,ct);
        if(value is null||value.OrganizationId!=owner||
           value.Audience!=audience||value.CadenceDays!=7||
           value.StartedAt==default||value.NextDueAt==default||
           value.StartedAt>DateTimeOffset.UtcNow||
           value.NextDueAt<value.StartedAt)
            throw Unavailable();
        return value;
    }

    public async Task<MobileFanLessonDeliveryPage> InboxAsync(
        MobileSession actor,string owner,string audience,CancellationToken ct)
    {
        Check(actor,owner,audience);
        var value=await ExecuteAsync<MobileFanLessonDeliveryPage>(
            "read",actor,owner,audience,ct);
        if(value is null||value.OrganizationId!=owner||
           value.Audience!=audience||value.Items is null||value.Items.Count>10||
           value.Items.Any(item=>item is null||item.Id==Guid.Empty||
               item.ContentId==Guid.Empty||item.Type!="lesson_ready"||
               item.Audience!=audience||item.QueuedAt==default||
               item.QueuedAt>DateTimeOffset.UtcNow))
            throw Unavailable();
        return value;
    }

    private async Task<T?> ExecuteAsync<T>(
        string operation,MobileSession actor,string owner,
        string audience,CancellationToken ct)
    {
        try
        {
            await database.Database.OpenConnectionAsync(ct);
            try
            {
                await using var command=new NpgsqlCommand(operation=="start"
                    ? "SELECT hub.start_mobile_fan_lesson_schedule(@identity,@phone,@role,@expires,@owner,@audience)::text"
                    : "SELECT hub.read_mobile_fan_lesson_deliveries(@identity,@phone,@role,@expires,@owner,@audience)::text",
                    (NpgsqlConnection)database.Database.GetDbConnection());
                command.Parameters.AddWithValue("identity",actor.IdentityId);
                command.Parameters.AddWithValue("phone",actor.NormalizedPhone);
                command.Parameters.AddWithValue("role","fan");
                command.Parameters.AddWithValue("expires",actor.ExpiresAt);
                command.Parameters.AddWithValue("owner",owner);
                command.Parameters.AddWithValue("audience",audience);
                var raw=await command.ExecuteScalarAsync(ct) as string;
                return raw is null?default:JsonSerializer.Deserialize<T>(raw,Json);
            }
            finally{await database.Database.CloseConnectionAsync();}
        }
        catch(PostgresException ex)
        {
            if(ex.SqlState=="42501")
                throw new MobileAuthException(MobileAuthFaultKind.Forbidden,
                    MobileAuthErrorCode.RoleUnavailable,
                    "A current verified Payabl fan subscription is required.");
            throw Unavailable();
        }
    }

    private static void Check(MobileSession actor,string owner,string audience)
    {
        if(actor.Role!=MobileRole.Fan ||
           owner is not ("lefteria_fc" or "lefteria_fc_academy") ||
           audience is not ("parent" or "player"))
            throw new MobileAuthException(MobileAuthFaultKind.Forbidden,
                MobileAuthErrorCode.RoleUnavailable,"Verified fan role required.");
    }
    private static MobileAuthException Unavailable()=>new(
        MobileAuthFaultKind.ServiceUnavailable,MobileAuthErrorCode.AccountUnavailable,
        "Fan weekly delivery is unavailable.");
}
