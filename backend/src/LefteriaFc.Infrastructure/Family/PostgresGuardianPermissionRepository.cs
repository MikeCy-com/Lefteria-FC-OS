using System.Text.Json;
using System.Text.Json.Serialization;
using LefteriaFc.Application.Family;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Infrastructure.Family;

/// <summary>
/// Calls restricted security-definer functions only. No direct permission
/// table reads or writes are granted to the application role.
/// </summary>
public sealed class PostgresGuardianPermissionRepository(LefteriaDbContext database)
    : IGuardianPermissionRepository
{
    public async Task<GuardianPermissionDto> IssueAsync(MobileSession session,
        string childId, string phone, Guid requestId, CancellationToken ct)
    {
        await database.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = new NpgsqlCommand("""
              SELECT identity.issue_guardian_permission(
                @identity,@phone,@role,@expires,@device,@player,@target,@request)::text
              """,(NpgsqlConnection)database.Database.GetDbConnection());
            Bind(command,session);
            command.Parameters.AddWithValue("player",childId);
            command.Parameters.AddWithValue("target",phone);
            command.Parameters.AddWithValue("request",requestId);
            var json = await Execute(command,ct);
            var record = JsonSerializer.Deserialize<GuardianPermissionDto>(json);
            return record ?? throw new InvalidOperationException("Invalid guardian permission result.");
        }
        finally { await database.Database.CloseConnectionAsync(); }
    }

    public async Task<GuardianBulkResult> IssueManyAsync(MobileSession session,
        string phone, IReadOnlyList<GuardianBulkChild> children, CancellationToken ct)
    {
        await database.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command=new NpgsqlCommand("""
              SELECT identity.issue_many_guardian_permissions(
                @identity,@phone,@role,@expires,@device,@target,@items)::text
              """,(NpgsqlConnection)database.Database.GetDbConnection());
            Bind(command,session);
            command.Parameters.AddWithValue("target",phone);
            command.Parameters.Add("items",NpgsqlTypes.NpgsqlDbType.Jsonb).Value=
                JsonSerializer.Serialize(children,new JsonSerializerOptions
                { PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower });
            var json=await Execute(command,ct);
            return JsonSerializer.Deserialize<GuardianBulkResult>(json) ??
                throw new InvalidOperationException("Invalid bulk permission response.");
        }
        finally {await database.Database.CloseConnectionAsync();}
    }

    public async Task<GuardianPermissionsResult> ListAsync(MobileSession session,CancellationToken ct)
    {
        await database.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = new NpgsqlCommand("""
              SELECT identity.read_issued_guardian_permissions(
                @identity,@phone,@role,@expires)::text
              """,(NpgsqlConnection)database.Database.GetDbConnection());
            Bind(command,session);
            var json = await Execute(command,ct);
            return JsonSerializer.Deserialize<GuardianPermissionsResult>(json) ??
                throw new InvalidOperationException("Invalid guardian permission list.");
        }
        finally { await database.Database.CloseConnectionAsync(); }
    }

    public async Task RevokeAsync(MobileSession session,Guid permissionId,CancellationToken ct)
    {
        await database.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = new NpgsqlCommand("""
              SELECT identity.revoke_issued_guardian_permission(
                @identity,@phone,@role,@expires,@device,@permission)
              """,(NpgsqlConnection)database.Database.GetDbConnection());
            Bind(command,session);
            command.Parameters.AddWithValue("permission",permissionId);
            try
            {
                var response = await command.ExecuteScalarAsync(ct);
                if (response is not bool success || !success)
                    throw new InvalidOperationException("Guardian permission revocation not verified.");
            }
            catch(PostgresException issue) { throw Translate(issue); }
        }
        finally { await database.Database.CloseConnectionAsync(); }
    }

    private static async Task<string> Execute(NpgsqlCommand command,CancellationToken ct)
    {
        try
        {
            var result=await command.ExecuteScalarAsync(ct);
            return result as string ??
                throw new InvalidOperationException("Guardian permission response unavailable.");
        }
        catch(PostgresException issue) { throw Translate(issue); }
    }

    private static MobileAuthException Translate(PostgresException issue) =>
        issue.SqlState switch
        {
            "42501" => new(MobileAuthFaultKind.Forbidden,
                MobileAuthErrorCode.AccountUnavailable,"Family permission access unavailable."),
            "23505" => new(MobileAuthFaultKind.Conflict,
                MobileAuthErrorCode.FamilyInvitationConflict,"The permission already exists."),
            "54000" => new(MobileAuthFaultKind.Conflict,
                MobileAuthErrorCode.FamilyInvitationConflict,"Too many permissions."),
            _ => new(MobileAuthFaultKind.ServiceUnavailable,
                MobileAuthErrorCode.FamilyUnavailable,"Family permission service unavailable.")
        };

    private static void Bind(NpgsqlCommand command,MobileSession session)
    {
        command.Parameters.AddWithValue("identity",session.IdentityId);
        command.Parameters.AddWithValue("phone",session.NormalizedPhone);
        command.Parameters.AddWithValue("role",session.Role.ToWireValue());
        command.Parameters.AddWithValue("expires",session.ExpiresAt.ToUniversalTime());
        command.Parameters.AddWithValue("device",session.DeviceSessionId);
    }
}