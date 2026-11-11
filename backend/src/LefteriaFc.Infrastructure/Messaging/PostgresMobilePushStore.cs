using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LefteriaFc.Application.Messaging;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Infrastructure.Messaging;

/// <summary>
/// The runtime cannot SELECT raw subscription URLs/keys or delivery rows.
/// Only revocable, current-identity SQL functions return bounded data.
/// </summary>
public sealed class PostgresMobilePushStore(LefteriaDbContext database)
{
    private static readonly JsonSerializerOptions Json=new(){
        PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };
    private static string Hash(string endpoint)=>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint)));

    public Task<MobilePushDeviceCountDto> StatusAsync(MobileSession session,CancellationToken token)=>
        SessionAsync<MobilePushDeviceCountDto>("""
            SELECT club.mobile_push_subscription_status(
              @identity,@phone,@role,@expires)::text
            """,session,[],token);

    public Task<MobilePushSubscriptionStateDto> SubscribeAsync(
        MobileSession session,string endpoint,string p256dh,string auth,CancellationToken token)=>
        SessionAsync<MobilePushSubscriptionStateDto>("""
            SELECT club.save_mobile_push_subscription(
              @identity,@phone,@role,@expires,@hash,@endpoint,@p256dh,@auth)::text
            """,session,[
                new("hash",Hash(endpoint)),new("endpoint",endpoint),
                new("p256dh",p256dh),new("auth",auth)
            ],token);

    public Task<MobilePushSubscriptionStateDto> UnsubscribeAsync(
        MobileSession session,string endpoint,CancellationToken token)=>
        SessionAsync<MobilePushSubscriptionStateDto>("""
            SELECT club.remove_mobile_push_subscription(
              @identity,@phone,@role,@expires,@hash)::text
            """,session,[new("hash",Hash(endpoint))],token);

    private async Task<T> SessionAsync<T>(
        string sql,MobileSession actor,NpgsqlParameter[] parameters,CancellationToken token)
    {
        try
        {
            await database.Database.OpenConnectionAsync(token);
            try
            {
                var connection=(NpgsqlConnection)database.Database.GetDbConnection();
                await using var command=new NpgsqlCommand(sql,connection);
                command.Parameters.AddWithValue("identity",actor.IdentityId);
                command.Parameters.AddWithValue("phone",actor.NormalizedPhone);
                command.Parameters.AddWithValue("role",actor.Role.ToString().ToLowerInvariant());
                command.Parameters.AddWithValue("expires",actor.ExpiresAt);
                foreach(var parameter in parameters)command.Parameters.Add(parameter);
                var raw=await command.ExecuteScalarAsync(token) as string;
                return raw is not null&&JsonSerializer.Deserialize<T>(raw,Json) is { } result
                    ?result:throw Unavailable();
            }
            finally{await database.Database.CloseConnectionAsync();}
        }
        catch(PostgresException error)
        {
            throw error.SqlState switch {
                "42501" or "23505"=>new MobileAuthException(
                    MobileAuthFaultKind.Forbidden,MobileAuthErrorCode.RoleUnavailable,
                    "This device cannot subscribe for this identity."),
                "22023" or "54000"=>Invalid(),
                "55000"=>Unavailable(),
                _=>Unavailable()
            };
        }
    }

    public Task<MobilePushLeaseBatchDto> ClaimAsync(int limit,CancellationToken token)=>
        WorkerJsonAsync<MobilePushLeaseBatchDto>(
            "SELECT club.claim_app_announcement_push_attempts(@limit)::text",
            [new("limit",limit)],token);
    public Task<bool> RecheckAsync(Guid id,Guid lease,CancellationToken token)=>
        WorkerBooleanAsync("SELECT club.check_app_announcement_push_lease(@id,@lease)",
            [new("id",id),new("lease",lease)],token);
    public Task<bool> FinishAsync(Guid id,Guid lease,string outcome,int? http,CancellationToken token)=>
        WorkerBooleanAsync("SELECT club.finish_app_announcement_push_attempt(@id,@lease,@outcome,@http)",
            [new("id",id),new("lease",lease),new("outcome",outcome),
             new("http",NpgsqlTypes.NpgsqlDbType.Integer){Value=(object?)http??DBNull.Value}],token);

    private async Task<T> WorkerJsonAsync<T>(
        string sql,NpgsqlParameter[] args,CancellationToken token)
    {
        var raw=await WorkerScalarAsync(sql,args,token) as string;
        return raw is not null&&JsonSerializer.Deserialize<T>(raw,Json) is { } value
            ?value:throw Unavailable();
    }
    private async Task<bool> WorkerBooleanAsync(
        string sql,NpgsqlParameter[] args,CancellationToken token)=>
        await WorkerScalarAsync(sql,args,token) is bool value&&value;

    private async Task<object?> WorkerScalarAsync(
        string sql,NpgsqlParameter[] args,CancellationToken token)
    {
        await database.Database.OpenConnectionAsync(token);
        try
        {
            await using var command=new NpgsqlCommand(sql,
                (NpgsqlConnection)database.Database.GetDbConnection());
            foreach(var argument in args)command.Parameters.Add(argument);
            return await command.ExecuteScalarAsync(token);
        }
        finally{await database.Database.CloseConnectionAsync();}
    }

    private static MobileAuthException Invalid()=>new(
        MobileAuthFaultKind.Validation,MobileAuthErrorCode.InvalidInput,
        "Invalid or excessive push subscriptions.");
    private static MobileAuthException Unavailable()=>new(
        MobileAuthFaultKind.ServiceUnavailable,MobileAuthErrorCode.AccountUnavailable,
        "Push subscriptions are unavailable.");
}
