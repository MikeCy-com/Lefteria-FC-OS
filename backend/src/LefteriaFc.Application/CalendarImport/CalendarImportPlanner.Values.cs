using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using LefteriaFc.Domain.Organizations;

namespace LefteriaFc.Application.CalendarImport;

public static partial class CalendarImportPlanner
{
    private static CalendarProjectionInput Projection(JsonElement json,string kind,TimeZoneInfo zone,bool published=false)
    {
        var sourceDate=SourceText(json,kind=="match"?"match_date":"date",80,true)!;
        var time=Text(json,kind=="match"?"match_time":"start_time",500);
        var start=Date(sourceDate,time,zone);
        var sourceEnd=kind=="training"?null:SourceText(json,"end_date",80);
        var end=sourceEnd is null?null:Date(sourceEnd,null,zone);
        if(end is not null&&(end.Day<start.Day||end.At is not null&&start.At is not null&&end.At<start.At))throw Problem("end_before_start");
        var title=kind=="match"?Text(json,"home_team",240,true)+" – "+Text(json,"away_team",240,true):Text(json,"title",500,true)!;
        var venue=Text(json,kind=="training"?"venue":"location",500);
        if(kind=="match")venue=Text(json,"venue",500)??venue;
        var url=Text(json,"location_url",500);
        if(url is not null&&(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme is not ("https" or "http")||!string.IsNullOrEmpty(uri.UserInfo)))throw Problem("location_url");
        var arrival=Text(json,"arrival_time",500);
        int? duration=null;
        if(json.TryGetProperty("duration_minutes",out var minutes)&&minutes.ValueKind!=JsonValueKind.Null)
        {
            if(minutes.ValueKind!=JsonValueKind.Number||!minutes.TryGetInt32(out var number)||number<0||number>(kind=="training"?360:1440))throw Problem("duration_minutes");
            duration=number;
        }
        // Training drafts contain exercises, notes and coaching descriptions. No
        // part of that document is copied into either calendar projection.
        return new(start.Day,start.Time,start.At,sourceDate,title,venue,url,arrival,duration,
            published?Text(json,"member_summary",1200):null,published?Text(json,"session_journey",1200):null,
            published?Text(json,"preparation",600):null,end?.Day,end?.At,sourceEnd,
            kind=="training"?null:Text(json,kind=="match"?"competition":"description",2000));
    }

    private static string? Id(JsonElement row,string field,bool required=false)
    {
        if(!row.TryGetProperty(field,out var value)||value.ValueKind==JsonValueKind.Null)
            return required?throw Problem("id_required"):null;
        if(value.ValueKind!=JsonValueKind.String)throw Problem("id_type");
        var id=value.GetString()!;
        if(!required&&id.Length==0)return null;
        if(id.Length is <1 or >128||id!=id.Trim()||id.Any(char.IsControl))throw Problem("id_value");
        return id;
    }
    private static HashSet<string> Ids(JsonElement row,string field,int maximum)
    {
        var ids=new HashSet<string>(StringComparer.Ordinal);
        if(!row.TryGetProperty(field,out var value)||value.ValueKind==JsonValueKind.Null)return ids;
        if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()>maximum)throw Problem("id_list");
        foreach(var item in value.EnumerateArray())
        {
            if(item.ValueKind!=JsonValueKind.String)throw Problem("id_type");
            var id=item.GetString()!;
            if(id.Length is <1 or >128||id!=id.Trim()||id.Any(char.IsControl))throw Problem("id_value");
            ids.Add(id);
        }
        return ids;
    }
    private static string Organization(JsonElement row,string inferred)
    {
        if(!row.TryGetProperty("organization_id",out var value)||value.ValueKind==JsonValueKind.Null)return inferred;
        if(value.ValueKind!=JsonValueKind.String||!OrganizationIds.IsKnown(value.GetString()))throw Problem("organization_id");
        return value.GetString()!;
    }
    private static bool AcademyMarker(string? value)=>value is not null&&(value.Contains("academy",StringComparison.OrdinalIgnoreCase)||value.Contains("ακαδημ",StringComparison.OrdinalIgnoreCase));
    private static bool Flag(JsonElement row,string field)
    {
        if(!row.TryGetProperty(field,out var value)||value.ValueKind==JsonValueKind.Null)return false;
        if(value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw Problem("boolean_flag");
        return value.GetBoolean();
    }
    private static string? Text(JsonElement row,string field,int maximum,bool required=false)
    {
        if(!row.TryGetProperty(field,out var value)||value.ValueKind==JsonValueKind.Null)return required?throw Problem("string_required"):null;
        if(value.ValueKind!=JsonValueKind.String)throw Problem("string_type");
        var text=value.GetString()!.Trim();
        if(text.Length==0)return required?throw Problem("string_required"):null;
        if(text.Any(character=>char.IsControl(character)&&character is not ('\r' or '\n' or '\t')))throw Problem("string_value");
        return text.Length>maximum?text[..maximum]:text;
    }
    private static string? SourceText(JsonElement row,string field,int maximum,bool required=false)
    {
        if(!row.TryGetProperty(field,out var value)||value.ValueKind==JsonValueKind.Null)return required?throw Problem("date_required"):null;
        if(value.ValueKind!=JsonValueKind.String)throw Problem("date_type");
        var text=value.GetString()!;
        if(text.Length==0&&!required)return null;
        if(text.Length is <1||text.Length>maximum)throw Problem("date_value");
        return text;
    }
    private static string? Status(JsonElement row,string kind)
    {
        var status=Text(row,"status",80);
        if(row.TryGetProperty("status",out var value)&&value.ValueKind==JsonValueKind.String&&status is null)throw Problem(kind=="match"?"match_status_unknown":"event_status_unknown");
        if(status is null)return null;
        status=status.ToLowerInvariant().Replace('_',' ');
        return status switch
        {
            "scheduled"=>"scheduled","live"=>"live","half time"=>"half_time",
            "completed" or "finished" or "ft" or "final"=>"completed","postponed"=>"postponed",
            "cancelled" or "canceled"=>"cancelled",
            _=>throw Problem(kind=="match"?"match_status_unknown":"event_status_unknown")
        };
    }

    private sealed record ScheduleDate(DateOnly Day,string? Time,DateTimeOffset? At);
    private static ScheduleDate Date(string source,string? explicitTime,TimeZoneInfo zone)
    {
        if(!IsoDatePattern().IsMatch(source))throw Problem("date_value");
        var dateOnly=source.Length==10;
        DateTime local;DateTimeOffset? instant=null;
        if(dateOnly)
        {
            if(!DateOnly.TryParseExact(source,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var day))throw Problem("date_value");
            local=day.ToDateTime(TimeOnly.MinValue);
        }
        else if(OffsetPattern().IsMatch(source))
        {
            if(!DateTimeOffset.TryParse(source,CultureInfo.InvariantCulture,DateTimeStyles.None,out var aware))throw Problem("date_value");
            instant=Microseconds(aware);local=TimeZoneInfo.ConvertTime(aware,zone).DateTime;
        }
        else
        {
            if(!DateTime.TryParse(source,CultureInfo.InvariantCulture,DateTimeStyles.None,out local))throw Problem("date_value");
            local=DateTime.SpecifyKind(local,DateTimeKind.Unspecified);
        }
        if(explicitTime is not null)
        {
            if(!LocalTimePattern().IsMatch(explicitTime)||!TimeOnly.TryParse(explicitTime,CultureInfo.InvariantCulture,DateTimeStyles.None,out var time))throw Problem("time_value");
            // The override defines a new club wall time on the converted day.
            // The source offset cannot disambiguate it: resolve it again below,
            // rejecting a DST gap or fold instead of reusing that prior offset.
            local=DateOnly.FromDateTime(local).ToDateTime(time);instant=null;
        }
        else if(dateOnly)return new(DateOnly.FromDateTime(local),null,null);
        if(instant is null)
        {
            local=DateTime.SpecifyKind(local,DateTimeKind.Unspecified);
            if(zone.IsInvalidTime(local))throw Problem("time_zone_gap");
            if(zone.IsAmbiguousTime(local))throw Problem("time_zone_ambiguous");
            try{instant=Microseconds(new DateTimeOffset(local,zone.GetUtcOffset(local)));}
            catch(ArgumentException){throw Problem("date_value");}
        }
        return new(DateOnly.FromDateTime(local),LocalTime(local),instant);
    }
    private static DateTimeOffset UtcInstant(string source)
    {
        if(source.Length>80||!IsoDatePattern().IsMatch(source)||source.Length==10||!OffsetPattern().IsMatch(source)||
            !DateTimeOffset.TryParse(source,CultureInfo.InvariantCulture,DateTimeStyles.None,out var instant))throw Problem("response_timestamp");
        return Microseconds(instant);
    }
    private static DateTimeOffset Microseconds(DateTimeOffset value){value=value.ToUniversalTime();return value.AddTicks(-(value.Ticks%10));}
    private static string LocalTime(DateTime local)=>local.TimeOfDay.Ticks%TimeSpan.TicksPerMinute==0?local.ToString("HH:mm",CultureInfo.InvariantCulture):local.ToString("HH:mm:ss.fffffff",CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
    [GeneratedRegex("\\A[0-9]{4}-[0-9]{2}-[0-9]{2}(?:T[0-9]{2}:[0-9]{2}(?::[0-9]{2}(?:\\.[0-9]{1,7})?)?(?:Z|[+-][0-9]{2}:[0-9]{2})?)?\\z",RegexOptions.CultureInvariant)]private static partial Regex IsoDatePattern();
    [GeneratedRegex("(?:Z|[+-][0-9]{2}:[0-9]{2})\\z",RegexOptions.CultureInvariant)]private static partial Regex OffsetPattern();
    [GeneratedRegex("\\A[0-9]{2}:[0-9]{2}(?::[0-9]{2}(?:\\.[0-9]{1,7})?)?\\z",RegexOptions.CultureInvariant)]private static partial Regex LocalTimePattern();
}
