using LefteriaFc.Application.Family;
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
            await using var reader=await command.ExecuteReaderAsync(token);
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
            var result=await command.ExecuteScalarAsync(token);
            if(result is not true)throw new InvalidOperationException("Invitation cancellation returned no confirmation");
        }
        finally{await database.Database.CloseConnectionAsync();}
    }

    private static void BindSession(NpgsqlCommand command,MobileSession session)
    {
        command.Parameters.AddWithValue("identity",session.IdentityId);
        command.Parameters.AddWithValue("phone",session.NormalizedPhone);
        command.Parameters.AddWithValue("role",session.Role.ToWireValue());
        command.Parameters.AddWithValue("expires",session.ExpiresAt.ToUniversalTime());
        command.Parameters.AddWithValue("device",session.DeviceSessionId);
    }
}
