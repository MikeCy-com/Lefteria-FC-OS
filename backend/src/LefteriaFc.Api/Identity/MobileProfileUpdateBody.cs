using System.Text.Json;
using System.Text.Json.Serialization;
using LefteriaFc.Application.MobileAuth;

namespace LefteriaFc.Api.Identity;

public sealed record MobileProfileUpdateBody
{
    public JsonElement Name { get; init; }
    public JsonElement Email { get; init; }
    [JsonPropertyName("first_name")]
    public JsonElement FirstName { get; init; }
    [JsonPropertyName("last_name")]
    public JsonElement LastName { get; init; }
    public JsonElement Birthday { get; init; }
    [JsonPropertyName("favourite_player_id")]
    public JsonElement FavouritePlayerId { get; init; }
    [JsonPropertyName("notification_prefs")]
    public JsonElement NotificationPreferences { get; init; }

    public MobileProfileUpdateCommand ToCommand() => new(
        StringField(Name, "name"), StringField(Email, "email"),
        StringField(FirstName, "first_name"), StringField(LastName, "last_name"),
        StringField(Birthday, "birthday"), StringField(FavouritePlayerId, "favourite_player_id"),
        Preferences(NotificationPreferences));

    private static ProfileField<string> StringField(JsonElement value, string field) => value.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null => default,
        JsonValueKind.String => new(true, value.GetString()),
        _ => throw new MobileAuthException(MobileAuthFaultKind.Validation, MobileAuthErrorCode.InvalidProfileField,
            $"Μη έγκυρο πεδίο προφίλ: {field}.")
    };

    private static ProfileField<IReadOnlyDictionary<string, bool>> Preferences(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return default;
        if (value.ValueKind != JsonValueKind.Object) throw InvalidPreferences();
        var preferences = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (preferences.Count >= MobileProfileLimits.NotificationKeys ||
                property.Name.Length is 0 or > MobileProfileLimits.NotificationKey || property.Name.Any(char.IsControl) ||
                property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !preferences.TryAdd(property.Name, property.Value.GetBoolean()))
            {
                throw InvalidPreferences();
            }
        }
        return new(true, preferences);
    }

    private static MobileAuthException InvalidPreferences() => new(MobileAuthFaultKind.Validation,
        MobileAuthErrorCode.InvalidNotificationPreferences,
        "Οι προτιμήσεις ειδοποιήσεων πρέπει να περιέχουν έως 64 επιλογές με σύντομα ονόματα και τιμές true/false.");
}
