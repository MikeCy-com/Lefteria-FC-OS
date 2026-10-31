using System.Text.Json;
using LefteriaFc.Application.CalendarImport;
using LefteriaFc.Application.ClubCalendar;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Infrastructure.ClubCalendar;

public sealed partial class PostgresMobileCalendarRepository(LefteriaDbContext database):IMobileCalendarRepository
{
    public const int PageSize=200;
    private NpgsqlCommand Command(string sql,MobileSession session,string? player=null,string? eventId=null,string? kind=null,int offset=0)
    {
        var command=new NpgsqlCommand(sql,(NpgsqlConnection)database.Database.GetDbConnection())
        {Transaction=database.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction};
        command.Parameters.AddWithValue("identity",session.IdentityId);command.Parameters.AddWithValue("sid",session.DeviceSessionId);
        command.Parameters.AddWithValue("phone",session.NormalizedPhone);command.Parameters.AddWithValue("role",session.Role.ToWireValue());
        command.Parameters.AddWithValue("expires",session.ExpiresAt.ToUniversalTime());
        command.Parameters.Add("player",NpgsqlDbType.Text).Value=(object?)player??DBNull.Value;
        command.Parameters.Add("event",NpgsqlDbType.Text).Value=(object?)eventId??DBNull.Value;
        command.Parameters.Add("kind",NpgsqlDbType.Text).Value=(object?)kind??DBNull.Value;
        command.Parameters.AddWithValue("offset",offset);return command;
    }
    private static string? OptionalString(NpgsqlDataReader reader,int ordinal)=>reader.IsDBNull(ordinal)?null:reader.GetString(ordinal);
    private static T ReadJson<T>(string json)
    {
        try{return JsonSerializer.Deserialize<T>(json,CalendarImportRows.JsonOptions)??throw InvalidProjection();}
        catch(JsonException){throw InvalidProjection();}
    }
    private static MobileAuthException InvalidProjection()=>new(MobileAuthFaultKind.ServiceUnavailable,MobileAuthErrorCode.CalendarUnavailable,"The calendar is temporarily unavailable.");
    private static MobileCalendarItem Item(JsonElement row)
    {
        var projection=ReadJson<CalendarProjectionInput>(row.GetProperty("projection").GetRawText());
        if(string.IsNullOrWhiteSpace(projection.Title))throw InvalidProjection();
        string? Text(string key)=>row.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;
        return new(row.GetProperty("id").GetString()!,row.GetProperty("kind").GetString()!,row.GetProperty("event_type").GetString()!,
            row.GetProperty("organization_id").GetString()!,projection.Title,projection.Day,projection.EndDay??projection.Day,
            projection.LocalTime,projection.StartsAt,projection.EndsAt,projection.ArrivalTime,projection.DurationMinutes,
            projection.Location,projection.LocationUrl,projection.Description,projection.Summary,projection.Journey,projection.Preparation,
            Text("status"),row.GetProperty("is_cancelled").GetBoolean(),Text("cancel_reason"),Text("content_status"),
            ReadJson<CalendarOwnResponse[]>(row.GetProperty("responses").GetRawText()));
    }
}
