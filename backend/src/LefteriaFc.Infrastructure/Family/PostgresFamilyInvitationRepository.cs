using LefteriaFc.Application.Family;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Infrastructure.Family;

// Only narrowly granted security-definer functions can read or change the
// phone-bearing invitation slot. No direct table DML is possible as app_role.
public sealed class PostgresFamilyInvitationRepository(LefteriaDbContext database)
    : IFamilyInvitationRepository
{
    public async Task<PendingFamilyInvitationDto> IssueAsync(MobileSession session,
        string childId, string normalizedTargetPhone, Guid invitationId, CancellationToken token)
    {
        await database.Database.OpenConnectionAsync(token);
        try
        {
            await using var command = new NpgsqlCommand("""
                SELECT issued_id, issued_at, valid_until
                FROM identity.issue_family_invitation(
                  @identity,@phone,@role,@expires,@device,@player,@target,@invitation)
                """,(NpgsqlConnection)database.Database.GetDbConnection());
            BindSession(command,session);
            command.Parameters.AddWithValue("player",childId);
            command.Parameters.AddWithValue("target",normalizedTargetPhone);
            command.Parameters.AddWithValue("invitation",invitationId);
            await using var reader=await ExecuteIssuanceAsync(command,token);
            if(!await reader.ReadAsync(token))throw new InvalidOperationException("Invitation function returned no record");
            var id=reader.GetGuid(0);
            var created=reader.GetFieldValue<DateTimeOffset>(1);
            var expires=reader.GetFieldValue<DateTimeOffset>(2);
            if(await reader.ReadAsync(token))throw new InvalidOperationException("Invitation function returned multiple records");
            return new(id,"pending",created,expires);
        }
        finally{await database.Database.CloseConnectionAsync();}
    }

    public async Task CancelAsync(MobileSession session,Guid invitationId,CancellationToken token)
    {
        await database.Database.OpenConnectionAsync(token);
        try
        {
            await using var command = new NpgsqlCommand("""
                SELECT identity.cancel_family_invitation(@identity,@phone,@role,@expires,@device,@invitation)
                """,(NpgsqlConnection)database.Database.GetDbConnection());
            BindSession(command,session);
            command.Parameters.AddWithValue("invitation",invitationId);
            var result=await ExecuteCancellationAsync(command,token);
            if(result is not true)throw new InvalidOperationException("Invitation cancellation returned no confirmation");
        }
        finally{await database.Database.CloseConnectionAsync();}
    }

    public async Task<IReadOnlyList<ParentOutgoingInvitationDto>> ReadOutgoingAsync(
        MobileSession session, CancellationToken token)
    {
        await database.Database.OpenConnectionAsync(token);
        try
        {
            await using var command = new NpgsqlCommand("""
                SELECT invitation_id,player_id,organization_id,created_at,expires_at
                FROM identity.read_parent_family_outgoing(@identity,@phone,@role,@expires)
                """, (NpgsqlConnection)database.Database.GetDbConnection());
            command.Parameters.AddWithValue("identity", session.IdentityId);
            command.Parameters.AddWithValue("phone", session.NormalizedPhone);
            command.Parameters.AddWithValue("role", session.Role.ToWireValue());
            command.Parameters.AddWithValue("expires", session.ExpiresAt.ToUniversalTime());
            await using var reader = await ExecuteOutgoingAsync(command, token);
            var rows = new List<ParentOutgoingInvitationDto>();
            while (await reader.ReadAsync(token))
            {
                if (rows.Count >= 101)
                    throw Unavailable();
                rows.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                    "pending", reader.GetFieldValue<DateTimeOffset>(3),
                    reader.GetFieldValue<DateTimeOffset>(4)));
            }
            return rows;
        }
        finally { await database.Database.CloseConnectionAsync(); }
    }

    public async Task<IReadOnlyList<RecipientPendingInvitationDto>> ReadRecipientAsync(
        MobileSession session, CancellationToken token)
    {
        await database.Database.OpenConnectionAsync(token);
        try
        {
            await using var command = new NpgsqlCommand("""
                SELECT invitation_id,organization_id,created_at,expires_at
                FROM identity.read_recipient_family_invitations(@identity,@phone,@role,@expires)
                """, (NpgsqlConnection)database.Database.GetDbConnection());
            command.Parameters.AddWithValue("identity", session.IdentityId);
            command.Parameters.AddWithValue("phone", session.NormalizedPhone);
            command.Parameters.AddWithValue("role", session.Role.ToWireValue());
            command.Parameters.AddWithValue("expires", session.ExpiresAt.ToUniversalTime());
            await using var reader = await ExecuteOutgoingAsync(command, token);
            var rows = new List<RecipientPendingInvitationDto>();
            while (await reader.ReadAsync(token))
            {
                if (rows.Count >= 2) throw Unavailable();
                rows.Add(new(reader.GetGuid(0), reader.GetString(1), "pending",
                    reader.GetFieldValue<DateTimeOffset>(2),
                    reader.GetFieldValue<DateTimeOffset>(3)));
            }
            return rows;
        }
        finally { await database.Database.CloseConnectionAsync(); }
    }

    private static async Task<NpgsqlDataReader> ExecuteOutgoingAsync(
        NpgsqlCommand command, CancellationToken token)
    {
        try { return await command.ExecuteReaderAsync(token); }
        catch (PostgresException issue) { throw Fault(issue); }
    }

    private static async Task<NpgsqlDataReader> ExecuteIssuanceAsync(NpgsqlCommand command,CancellationToken token)
    {
        try {return await command.ExecuteReaderAsync(token);}
        catch(PostgresException issue){throw Fault(issue);}
    }

    private static async Task<object?> ExecuteCancellationAsync(NpgsqlCommand command,CancellationToken token)
    {
        try {return await command.ExecuteScalarAsync(token);}
        catch(PostgresException issue){throw Fault(issue);}
    }

    private static MobileAuthException Fault(PostgresException issue)
    {
        if(issue.SqlState=="P0001")
            return issue.MessageText switch
            {
                "family_invitation_denied"=>new(MobileAuthFaultKind.Forbidden,
                    MobileAuthErrorCode.AccountUnavailable,"Family access is unavailable."),
                "family_invitation_unavailable"=>Unavailable(),
                "family_invitation_missing"=>new(MobileAuthFaultKind.NotFound,
                    MobileAuthErrorCode.InvalidInput,"Invitation not found."),
                "family_invitation_conflict"=>new(MobileAuthFaultKind.Conflict,
                    MobileAuthErrorCode.FamilyInvitationConflict,"The invitation is no longer available."),
                _=>Unavailable()
            };
        if(issue.SqlState is "23505" or "23503")
            return new(MobileAuthFaultKind.Conflict,MobileAuthErrorCode.FamilyInvitationConflict,
                "The invitation is no longer available.");
        return Unavailable();
    }

    private static MobileAuthException Unavailable()=>new(MobileAuthFaultKind.ServiceUnavailable,
        MobileAuthErrorCode.FamilyUnavailable,"Family invitations are temporarily unavailable.");

    private static void BindSession(NpgsqlCommand command,MobileSession session)
    {
        command.Parameters.AddWithValue("identity",session.IdentityId);
        command.Parameters.AddWithValue("phone",session.NormalizedPhone);
        command.Parameters.AddWithValue("role",session.Role.ToWireValue());
        command.Parameters.AddWithValue("expires",session.ExpiresAt.ToUniversalTime());
        command.Parameters.AddWithValue("device",session.DeviceSessionId);
    }
}
