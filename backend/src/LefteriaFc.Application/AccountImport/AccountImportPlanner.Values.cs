using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LefteriaFc.Application.Roster;
using LefteriaFc.Domain.Identity;

namespace LefteriaFc.Application.AccountImport;

public static partial class AccountImportPlanner
{
    private sealed class Row(AccountSourceRow source)
    {
        public AccountSourceRow Source { get; }=source;
        public JsonElement Json { get; }=ReadJson(source.Json);
        public string? LegacyId { get; set; }
        public Guid Id { get; set; }
        public string? CanonicalPhone { get; set; }
        public string? Role { get; set; }
        public string? Username { get; set; }
        public HashSet<string> Issues { get; }=new(StringComparer.Ordinal);
        public bool Valid=>Issues.Count==0;
        public PlannedMobileAccount? Mobile { get; set; }
        public PlannedAdminAccount? Admin { get; set; }
        public PlannedFanEvidence? Fan { get; set; }
        public override string ToString()=>"Protected account planning row";
    }
    private sealed class RowProblem(string code):Exception("Protected account validation issue")
    { public string Code { get; }=code; }
    private static JsonElement ReadJson(string json){using var parsed=JsonDocument.Parse(json);return parsed.RootElement.Clone();}
    private static string? Value(JsonElement row,string key,int maximum,bool required=false)
    {
        if(!row.TryGetProperty(key,out var value)||value.ValueKind==JsonValueKind.Null)
        {if(required)throw Problem("required_value");return null;}
        if(value.ValueKind!=JsonValueKind.String)throw Problem("string_type");
        var text=value.GetString()!;
        if(text.Length>maximum||text.Any(char.IsControl)||required&&string.IsNullOrWhiteSpace(text))throw Problem("string_value");
        return text;
    }
    private static string? IdValue(JsonElement row,string key)
    {
        var value=Value(row,key,128);
        if(value is null or "")return null;
        if(value!=value.Trim())throw Problem("identifier_value");
        return value;
    }
    private static bool Flag(JsonElement row,string key,bool fallback,bool nullMeansMissing=false)
    {
        if(!row.TryGetProperty(key,out var value)||nullMeansMissing&&value.ValueKind==JsonValueKind.Null)return fallback;
        if(value.ValueKind is not(JsonValueKind.True or JsonValueKind.False))throw Problem("boolean_type");
        return value.GetBoolean();
    }
    private static string? Phone(JsonElement row,string key,string origin,bool required=false)
    {
        var raw=Value(row,key,80,required);
        if(string.IsNullOrWhiteSpace(raw)){if(required)throw Problem("phone_required");return null;}
        var country=origin is "staff" or "admin_users"?Value(row,"country",8):null;
        var digits=new string(raw.Where(character=>character is >= '0' and <= '9').ToArray());
        var prefix=raw.TrimStart().StartsWith('+')||raw.TrimStart().StartsWith("00",StringComparison.Ordinal);
        if(!prefix&&(origin is "staff" or "admin_users")&&country is not(null or "" or "CY"))throw Problem("phone_country_ambiguous");
        if(!prefix&&origin=="admin_users"&&digits.Length!=8)throw Problem("phone_origin_ambiguous");
        if(prefix&&(digits.StartsWith("00",StringComparison.Ordinal)?digits.Length-2:digits.Length)==8)throw Problem("phone_origin_ambiguous");
        if(!PhoneNumber.TryParse(raw,out var phone))throw Problem("phone_invalid");
        return phone.Digits;
    }
    private static string? SourceComparisonPhone(JsonElement row)
    {
        if(!row.TryGetProperty("phone",out var value)||value.ValueKind!=JsonValueKind.String)return null;
        var digits=new StringBuilder(17);
        foreach(var rune in value.GetString()!.EnumerateRunes())
        {
            if(!SourceIsDecimalDigit(rune.Value))continue;
            // Python retains Unicode Nd unchanged. Such a key cannot equal a
            // canonical ASCII phone: never drop/transliterate those digits.
            if(rune.Value is <'0' or >'9')return null;
            digits.Append((char)rune.Value);
            if(digits.Length>17)return null;
        }
        var phone=digits.ToString();
        if(phone.StartsWith("00",StringComparison.Ordinal))phone=phone[2..];
        if(phone.Length==8)phone="357"+phone;
        // Compatibility comparison only REFUSES ambiguous fallback. It can
        // never create an eligible normalized contact, identity or grant.
        return phone.Length==0?null:phone;
    }
    private static DateOnly? Birthday(JsonElement row)
    {
        var text=Value(row,"birthday",10);
        if(text is null or "")return null;
        if(!DateOnly.TryParseExact(text,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))throw Problem("birthday_invalid");
        return date;
    }
    private static DateTimeOffset? Timestamp(JsonElement row,string key)
    {
        var text=Value(row,key,64);
        if(text is null or "")return null;
        if(!text.Contains('T')||!DateTimeOffset.TryParse(text,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out var date))throw Problem("timestamp_invalid");
        // PostgreSQL timestamps retain microseconds; the exact source string
        // remains in the protected archive, including any finer precision.
        return date.AddTicks(-(date.Ticks%10));
    }
    private static string? Preferences(JsonElement row)
    {
        if(!row.TryGetProperty("notification_prefs",out var value)||value.ValueKind==JsonValueKind.Null)return null;
        if(value.ValueKind!=JsonValueKind.Object||value.EnumerateObject().Count()>64)throw Problem("preferences_invalid");
        var result=new SortedDictionary<string,bool>(StringComparer.Ordinal);
        foreach(var item in value.EnumerateObject())
        {
            if(item.Name.Length is <1 or >64||item.Name.Any(char.IsControl)||item.Value.ValueKind is not(JsonValueKind.True or JsonValueKind.False))throw Problem("preferences_invalid");
            result.Add(item.Name,item.Value.GetBoolean());
        }
        return JsonSerializer.Serialize(result);
    }
    private static string? Avatar(JsonElement row)
    {var value=Value(row,"avatar_url",2048);if(value is not(null or "")&&!RosterImportValidator.PublicImageUrl(value))throw Problem("avatar_url_invalid");return value;}
    private static string? Alias(JsonElement row,params string[] keys)
    {
        var ids=keys.Select(key=>IdValue(row,key)).Where(id=>id is not null).Distinct(StringComparer.Ordinal).ToArray();
        if(ids.Length>1)throw Problem("conflicting_link_aliases");
        return ids.SingleOrDefault();
    }
    private static string[] Ids(JsonElement row,string key)
    {
        if(!row.TryGetProperty(key,out var values)||values.ValueKind==JsonValueKind.Null)return [];
        if(values.ValueKind!=JsonValueKind.Array||values.GetArrayLength()>128)throw Problem("link_list_invalid");
        var result=new HashSet<string>(StringComparer.Ordinal);
        foreach(var value in values.EnumerateArray())
        {
            if(value.ValueKind!=JsonValueKind.String||value.GetString() is not{Length:>0 and <=128} id||id!=id.Trim()||id.Any(char.IsControl))throw Problem("link_identifier_invalid");
            result.Add(id);
        }
        return result.Order(StringComparer.Ordinal).ToArray();
    }
    private static Guid TargetId(string collection,string id)
    {
        if(Guid.TryParseExact(id,"D",out var value)&&value!=Guid.Empty)return value;
        var bytes=SHA256.HashData(Encoding.UTF8.GetBytes("lefteria-fc-os:legacy-account:v1:"+collection+":"+id));
        bytes[6]=(byte)((bytes[6]&15)|128);bytes[8]=(byte)((bytes[8]&63)|128);
        return new Guid(bytes.AsSpan(0,16),bigEndian:true);
    }
    private static RowProblem Problem(string code)=>new(code);
    [GeneratedRegex("\\A\\$2[aby]\\$(0[4-9]|[12][0-9]|3[01])\\$[./A-Za-z0-9]{53}\\z",RegexOptions.CultureInvariant)]private static partial Regex Bcrypt();
}
