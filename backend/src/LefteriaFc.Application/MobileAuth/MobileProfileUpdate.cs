namespace LefteriaFc.Application.MobileAuth;

public static class MobileProfileLimits
{
    public const int Name = 200;
    public const int NamePart = 100;
    public const int Email = 254;
    public const int FavouritePlayerId = 128;
    public const int NotificationKeys = 64;
    public const int NotificationKey = 64;
}

// The HTTP mapper treats null common fields as omitted, retaining source semantics.
public readonly record struct ProfileField<T>(bool IsSupplied, T? Value) where T : class;

public sealed record MobileProfileUpdateCommand(
    ProfileField<string> Name = default,
    ProfileField<string> Email = default,
    ProfileField<string> FirstName = default,
    ProfileField<string> LastName = default,
    ProfileField<string> Birthday = default,
    ProfileField<string> FavouritePlayerId = default,
    ProfileField<IReadOnlyDictionary<string, bool>> NotificationPreferences = default);

// Application-validated write set, never deserialized from an HTTP body. A true
// flag with null Email/FavouritePlayerId explicitly clears that preference.
// Other true flags always carry a non-null value. Empty preferences replace the
// whole object. UpdatedAt is written on every accepted update.
public sealed record MobileProfileUpdate(
    bool NameProvided, string? Name,
    bool EmailProvided, string? Email,
    bool FirstNameProvided, string? FirstName,
    bool LastNameProvided, string? LastName,
    bool BirthdayProvided, DateOnly? Birthday,
    bool FavouritePlayerIdProvided, string? FavouritePlayerId,
    bool NotificationPreferencesProvided, IReadOnlyDictionary<string, bool>? NotificationPreferences,
    DateTimeOffset UpdatedAt);
