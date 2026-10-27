namespace LefteriaFc.Application.AccountImport;

public static partial class AccountImportPlanner
{
    private static PlannedFanEvidence ParseFan(Row row,DateTimeOffset boundary)
    {
        var first=Value(row.Json,"first_name",100,true)!;var last=Value(row.Json,"last_name",100,true)!;
        var birthday=Birthday(row.Json)??throw Problem("fan_birthday_required");
        if(birthday>DateOnly.FromDateTime(boundary.UtcDateTime))throw Problem("birthday_future");
        var active=AccountActive(row.Json);
        return new(row.Id,row.LegacyId!,row.CanonicalPhone!,first,last,birthday,active,Avatar(row.Json),Value(row.Json,"favourite_player_id",128),Preferences(row.Json));
    }
    private static PlannedMobileAccount ParseMobile(Row row,DateTimeOffset boundary,PlanningIndex index)
    {
        var source=row.Json;var first=Value(source,"first_name",100);var last=Value(source,"last_name",100);
        var birthday=Birthday(source);var favourite=Value(source,"favourite_player_id",128);var preferences=Preferences(source);
        if(row.Role=="fan"&&(birthday is null||string.IsNullOrEmpty(first)))
        {
            var mirror=index.FansByPhone[row.CanonicalPhone!].SingleOrDefault()?.Fan;
            if(mirror is not null)
            {
                if(string.IsNullOrEmpty(first))first=mirror.FirstName;
                if(string.IsNullOrEmpty(last))last=mirror.LastName;
                birthday??=mirror.Birthday;
                if(string.IsNullOrEmpty(favourite))favourite=mirror.FavouritePlayerId;
                if(preferences is null or "{}")preferences=mirror.NotificationPreferencesJson;
            }
        }
        if(birthday>DateOnly.FromDateTime(boundary.UtcDateTime))throw Problem("birthday_future");
        var active=AccountActive(source);
        var team=Value(source,"favorite_team",32);
        if(team is not(null or "" or "first_team" or "academy" or "both"))throw Problem("favorite_team_invalid");
        return new(row.Id,row.LegacyId!,row.CanonicalPhone!,row.Role!,Value(source,"name",200,true)!,active,null,
            first,last,birthday,Avatar(source),team,Value(source,"email",254),favourite,preferences,
            Value(source,"platform",24)??"Άλλο",Timestamp(source,"created_at")??boundary,
            Timestamp(source,"last_login"),Timestamp(source,"updated_at"));
    }
    private static bool AccountActive(System.Text.Json.JsonElement source)
    {
        var active=Flag(source,"is_active",true,true);
        var status=Value(source,"account_status",32);
        if(status is null or "")return active;
        // Source casefolds known revoked states without trimming. Trimming
        // revocations and quarantining unknown states deliberately tighten it.
        return status.Trim().ToLowerInvariant() switch
        {
            "disabled" or "inactive" or "suspended"=>false,
            "active"=>active,
            _=>throw Problem("account_status_unknown")
        };
    }
}
