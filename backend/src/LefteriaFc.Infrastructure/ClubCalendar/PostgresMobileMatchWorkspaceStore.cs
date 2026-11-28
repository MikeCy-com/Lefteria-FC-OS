using System.Text.Json;
using LefteriaFc.Application.ClubCalendar;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Infrastructure.ClubCalendar;

public sealed class PostgresMobileMatchWorkspaceStore(LefteriaDbContext database)
{
    private static readonly JsonSerializerOptions Json=new()
    {
        PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public Task<MobileMatchWorkspaceDto> ReadAsync(MobileSession actor,string match,CancellationToken token)
        => QueryAsync(actor,match,null,token);

    public Task<MobileMatchWorkspaceDto> WriteAsync(MobileSession actor,string match,MobileMatchWorkspaceBody body,CancellationToken token)
    {
        if(body is null||body.Action is not ("save_squad" or "select_squad" or "submit_statistics" or "approve" or "reject" or "confirm_attendance")||
          body.ExpectedVersion is <0 or >999999||body.Squad?.Count>60||body.Statistics?.Count>60||
          body.RotationNote?.Length>500||body.ReviewReason?.Length>500||
          (body.Action=="confirm_attendance"?(body.AttendanceIds is null||body.AttendanceIds.Count is <1 or >60||
             body.Squad is not null||body.Statistics is not null||body.RotationNote is not null||body.ReviewReason is not null||
             body.AttendanceIds.Any(id=>string.IsNullOrWhiteSpace(id)||id.Length>128)):
             body.AttendanceIds is not null)||
          (body.Squad?.Any(row=>row is null||string.IsNullOrWhiteSpace(row.PlayerId)||row.PlayerId.Length>128||
            row.Assignment is not ("starter" or "reserve")||row.Position?.Length>40)??false)||
          (body.Statistics?.Any(row=>row is null||string.IsNullOrWhiteSpace(row.PlayerId)||row.PlayerId.Length>128||
            row.MinutesPlayed is <0 or >180||row.Goals is <0 or >50||row.Assists is <0 or >50)??false))
            throw Invalid();
        return QueryAsync(actor,match,body,token);
    }

    private async Task<MobileMatchWorkspaceDto> QueryAsync(MobileSession actor,string match,MobileMatchWorkspaceBody? body,CancellationToken token)
    {
        if(string.IsNullOrWhiteSpace(match)||match.Length>128||match!=match.Trim()||match.Any(char.IsControl))throw Invalid();
        if(actor.Role is not (MobileRole.Coach or MobileRole.Management))throw Denied();
        try
        {
            await database.Database.OpenConnectionAsync(token);
            try
            {
                var connection=(NpgsqlConnection)database.Database.GetDbConnection();
                var sql=body is null
                    ?"SELECT (club.read_mobile_match_workspace(@actor,@phone,@role,@expires,@match) || jsonb_build_object('timing',club.read_mobile_match_timing(@actor,@phone,@role,@expires,@match)))::text"
                    :body.Action=="confirm_attendance"
                    ?"WITH changed AS MATERIALIZED (SELECT club.confirm_mobile_match_attendance(@actor,@phone,@role,@expires,@match,@expected,@attendance) AS result) SELECT (result || jsonb_build_object('timing',club.read_mobile_match_timing(@actor,@phone,@role,@expires,@match)))::text FROM changed"
                    :"WITH changed AS MATERIALIZED (SELECT club.write_mobile_match_workspace(@actor,@phone,@role,@expires,@match,@action,@expected,@squad,@statistics,@rotation,@review) AS result) SELECT (result || jsonb_build_object('timing',club.read_mobile_match_timing(@actor,@phone,@role,@expires,@match)))::text FROM changed";
                await using var command=new NpgsqlCommand(sql,connection);
                command.Parameters.AddWithValue("actor",actor.IdentityId);
                command.Parameters.AddWithValue("phone",actor.NormalizedPhone);
                command.Parameters.AddWithValue("role",actor.Role.ToWireValue());
                command.Parameters.AddWithValue("expires",actor.ExpiresAt.ToUniversalTime());
                command.Parameters.AddWithValue("match",match);
                if(body is not null)
                {
                    command.Parameters.AddWithValue("expected",body.ExpectedVersion);
                    if(body.Action=="confirm_attendance")
                        command.Parameters.Add("attendance",NpgsqlDbType.Jsonb).Value=JsonSerializer.Serialize(body.AttendanceIds,Json);
                    else
                    {
                    command.Parameters.AddWithValue("action",body.Action!);
                    command.Parameters.Add("squad",NpgsqlDbType.Jsonb).Value=body.Squad is null?DBNull.Value:JsonSerializer.Serialize(body.Squad,Json);
                    command.Parameters.Add("statistics",NpgsqlDbType.Jsonb).Value=body.Statistics is null?DBNull.Value:JsonSerializer.Serialize(body.Statistics,Json);
                    command.Parameters.Add("rotation",NpgsqlDbType.Text).Value=(object?)body.RotationNote??DBNull.Value;
                    command.Parameters.Add("review",NpgsqlDbType.Text).Value=(object?)body.ReviewReason??DBNull.Value;
                    }
                }
                var raw=await command.ExecuteScalarAsync(token) as string;
                return raw is null?throw Unavailable():JsonSerializer.Deserialize<MobileMatchWorkspaceDto>(raw,Json)??throw Unavailable();
            }
            finally{await database.Database.CloseConnectionAsync();}
        }
        catch(PostgresException error)
        {
            throw error.SqlState switch
            {
                "42501"=>Denied(),
                "22023"=>Invalid(),
                "40001" or "23505"=>new MobileAuthException(MobileAuthFaultKind.Conflict,MobileAuthErrorCode.InvalidInput,
                    "Match workspace changed or is locked; refresh and retry."),
                _=>Unavailable()
            };
        }
        catch(System.Data.Common.DbException){throw Unavailable();}
        catch(JsonException){throw Unavailable();}
    }

    private static MobileAuthException Invalid()=>new(MobileAuthFaultKind.Validation,MobileAuthErrorCode.InvalidInput,
        "Invalid match workspace, squad, statistics or rotation justification.");
    private static MobileAuthException Denied()=>new(MobileAuthFaultKind.Forbidden,MobileAuthErrorCode.RoleUnavailable,
        "The match workspace is unavailable to this account.");
    private static MobileAuthException Unavailable()=>new(MobileAuthFaultKind.ServiceUnavailable,MobileAuthErrorCode.CalendarUnavailable,
        "Match workspace is temporarily unavailable.");
}
