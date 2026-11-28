using System.Text.Json;
using System.Text.RegularExpressions;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Api.FanEngagement;

public sealed class MobilePotmBallotStore(LefteriaDbContext database)
{
    private static readonly JsonSerializerOptions Json=new()
    {
        PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower
    };

    internal static bool ValidMonth(string value)=>
        Regex.IsMatch(value,@"\A[0-9]{4}-(0[1-9]|1[0-2])\z",RegexOptions.CultureInvariant)
        && !value.StartsWith("0000",StringComparison.Ordinal);

    internal static bool ValidPlayer(string value)=>
        value.Length is >=1 and <=128 && value==value.Trim() && !value.Any(char.IsControl);

    public Task<MobilePotmBallot> ReadAsync(MobileSession actor,CancellationToken token)=>
        ExecuteAsync(actor,null,token);

    public Task<MobilePotmBallot> WriteAsync(MobileSession actor,MobilePotmBallotCommand command,CancellationToken token)
    {
        if(!ValidMonth(command.MonthKey) || command.ExpectedRevision<0 ||
           (command.PlayerId is not null && !ValidPlayer(command.PlayerId)))
            throw new MobileAuthException(MobileAuthFaultKind.BadRequest,
                MobileAuthErrorCode.InvalidPotmInput,"Μη έγκυρα στοιχεία ψηφοφορίας.");
        return ExecuteAsync(actor,command,token);
    }

    private async Task<MobilePotmBallot> ExecuteAsync(MobileSession actor,MobilePotmBallotCommand? command,CancellationToken token)
    {
        if(actor.Role!=MobileRole.Fan || actor.IdentityId==Guid.Empty || actor.DeviceSessionId==Guid.Empty)
            throw Forbidden();
        try
        {
            await database.Database.OpenConnectionAsync(token);
            try
            {
                var statement=command is null
                    ?"SELECT club.read_mobile_potm_ballot(@identity,@phone,@role,@expires)::text"
                    :"SELECT club.write_mobile_potm_ballot(@identity,@phone,@role,@expires,@month,@revision,@player)::text";
                await using var sql=new NpgsqlCommand(statement,(NpgsqlConnection)database.Database.GetDbConnection());
                sql.Parameters.AddWithValue("identity",actor.IdentityId);
                sql.Parameters.AddWithValue("phone",actor.NormalizedPhone);
                sql.Parameters.AddWithValue("role","fan");
                sql.Parameters.AddWithValue("expires",actor.ExpiresAt);
                if(command is not null)
                {
                    sql.Parameters.AddWithValue("month",command.MonthKey);
                    sql.Parameters.AddWithValue("revision",command.ExpectedRevision);
                    sql.Parameters.AddWithValue("player",NpgsqlDbType.Text,(object?)command.PlayerId??DBNull.Value);
                }
                var raw=await sql.ExecuteScalarAsync(token);
                if(raw is not string value)throw Unavailable();
                var result=JsonSerializer.Deserialize<MobilePotmBallot>(value,Json);
                if(result is null || result.MonthKey is null || !ValidMonth(result.MonthKey) || result.Revision<0 ||
                    (result.MyVotePlayerId is not null && (!ValidPlayer(result.MyVotePlayerId)||result.Revision==0)) ||
                    (command is not null && (!result.VotingOpen || result.MonthKey!=command.MonthKey ||
                     result.MyVotePlayerId!=command.PlayerId || result.Revision<command.ExpectedRevision ||
                     (long)result.Revision>(long)command.ExpectedRevision+1)))
                    throw Unavailable();
                return result;
            }
            finally{await database.Database.CloseConnectionAsync();}
        }
        catch(PostgresException error) when(error.SqlState=="42501"){throw Forbidden();}
        catch(PostgresException error) when(error.SqlState=="40001")
        {
            throw new MobileAuthException(MobileAuthFaultKind.Conflict,MobileAuthErrorCode.PotmRevisionConflict,
                "Η ψήφος έχει αλλάξει. Ανανεώστε την κατάσταση πριν προσπαθήσετε ξανά.");
        }
        catch(PostgresException error) when(error.SqlState=="55000")
        {
            throw new MobileAuthException(MobileAuthFaultKind.Conflict,MobileAuthErrorCode.PotmVotingClosed,
                "Η ψηφοφορία δεν είναι ανοικτή.");
        }
        catch(PostgresException error) when(error.SqlState=="22023")
        {
            throw new MobileAuthException(MobileAuthFaultKind.Validation,MobileAuthErrorCode.InvalidPotmInput,
                "Ο μήνας ή ο ποδοσφαιριστής δεν είναι διαθέσιμος για ψηφοφορία.");
        }
        catch(NpgsqlException){throw Unavailable();}
        catch(JsonException){throw Unavailable();}
        catch(TimeoutException){throw Unavailable();}
    }

    private static MobileAuthException Forbidden()=>new(MobileAuthFaultKind.Forbidden,
        MobileAuthErrorCode.RoleUnavailable,"Απαιτείται ενεργός επαληθευμένος λογαριασμός φιλάθλου.");
    private static MobileAuthException Unavailable()=>new(MobileAuthFaultKind.ServiceUnavailable,
        MobileAuthErrorCode.PotmUnavailable,"Η ψηφοφορία δεν είναι προσωρινά διαθέσιμη.");
}
