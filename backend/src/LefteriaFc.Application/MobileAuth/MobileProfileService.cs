using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Mail;
using LefteriaFc.Domain.Identity;

namespace LefteriaFc.Application.MobileAuth;

public sealed partial class MobileAuthService
{
    public async Task<MobileUserDto> UpdateProfileAsync(MobileSession session,
        MobileProfileUpdateCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireSession(session);
        var phone = PhoneNumber.Parse(session.NormalizedPhone);
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        var profiles = ValidateProfiles(
            await transaction.LoadIdentityProfilesForUpdateAsync(phone, cancellationToken), phone);
        RequireSession(session);
        var current = profiles.SingleOrDefault(profile => profile.Id == session.IdentityId) ?? throw AccountUnavailable();
        RequireProfileIdentity(current, phone, session.IdentityId, session.Role);
        if (!IsAvailable(current)) throw AccountUnavailable();

        var now = timeProvider.GetUtcNow();
        var update = ValidateProfileUpdate(command, current, now);
        var updated = await transaction.UpdateProfileAsync(current.Id, update, cancellationToken);
        RequireProfileIdentity(updated, phone, session.IdentityId, session.Role);
        if (!IsAvailable(updated)) throw AccountUnavailable();
        await transaction.AppendAuditAsync(new AuthAuditEntry("mobile.profile.update", "success", now,
            current.Id, session.DeviceSessionId, ReasonCode: "own_profile"), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToUserDto(updated, WireRoles(Available(profiles)), true);
    }

    private static MobileProfileUpdate ValidateProfileUpdate(MobileProfileUpdateCommand command,
        MobileIdentityProfile current, DateTimeOffset now)
    {
        var nameProvided = Accepted(command.Name);
        var emailProvided = Accepted(command.Email);
        var firstProvided = Accepted(command.FirstName);
        var lastProvided = Accepted(command.LastName);
        var birthdayProvided = Accepted(command.Birthday);
        var favouriteProvided = Accepted(command.FavouritePlayerId);
        var preferencesProvided = Accepted(command.NotificationPreferences);
        if (!(nameProvided || emailProvided || firstProvided || lastProvided || birthdayProvided ||
              favouriteProvided || preferencesProvided))
        {
            throw Fault(MobileAuthFaultKind.BadRequest, MobileAuthErrorCode.NoProfileChanges, "No valid fields to update");
        }

        var name = nameProvided ? ValidateName(command.Name.Value!, "name", MobileProfileLimits.Name, false) : null;
        var first = firstProvided ? ValidateName(command.FirstName.Value!, "first_name", MobileProfileLimits.NamePart, true) : null;
        var last = lastProvided ? ValidateName(command.LastName.Value!, "last_name", MobileProfileLimits.NamePart, true) : null;
        if (!nameProvided && (firstProvided || lastProvided))
        {
            var combined = ((firstProvided ? first : current.FirstName) ?? "") + " " +
                ((lastProvided ? last : current.LastName) ?? "");
            combined = combined.Trim();
            if (combined.Length > 0)
            {
                name = ValidateName(combined, "name", MobileProfileLimits.Name, false);
                nameProvided = true;
            }
        }

        string? email = null;
        if (emailProvided)
        {
            var raw = command.Email.Value!;
            email = raw.Trim();
            if (raw.Any(char.IsControl) || email.Length > MobileProfileLimits.Email ||
                email.Length > 0 && (email.Any(char.IsWhiteSpace) || !MailAddress.TryCreate(email, out var address) ||
                    address.DisplayName.Length > 0 || !string.Equals(address.Address, email, StringComparison.Ordinal)))
            {
                throw ProfileFieldFault("email");
            }
            if (email.Length == 0) email = null;
        }

        DateOnly? birthday = null;
        if (birthdayProvided)
        {
            if (!DateOnly.TryParseExact(command.Birthday.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed) || parsed > DateOnly.FromDateTime(now.UtcDateTime))
            {
                throw Fault(MobileAuthFaultKind.Validation, MobileAuthErrorCode.InvalidBirthday,
                    "Η ημερομηνία γέννησης πρέπει να είναι έγκυρη (YYYY-MM-DD).");
            }
            birthday = parsed;
        }

        string? favourite = null;
        if (favouriteProvided)
        {
            var raw = command.FavouritePlayerId.Value!;
            favourite = raw.Trim();
            if (raw.Any(char.IsControl) || favourite.Length > MobileProfileLimits.FavouritePlayerId)
                throw ProfileFieldFault("favourite_player_id");
            if (favourite.Length == 0) favourite = null;
        }

        IReadOnlyDictionary<string, bool>? preferences = null;
        if (preferencesProvided)
        {
            var supplied = command.NotificationPreferences.Value!;
            if (supplied.Count > MobileProfileLimits.NotificationKeys || supplied.Keys.Any(key =>
                    key.Length is 0 or > MobileProfileLimits.NotificationKey || key.Any(char.IsControl)))
            {
                throw NotificationPreferencesFault();
            }
            preferences = new ReadOnlyDictionary<string, bool>(new Dictionary<string, bool>(supplied, StringComparer.Ordinal));
        }

        return new MobileProfileUpdate(nameProvided, name, emailProvided, email,
            firstProvided, first, lastProvided, last, birthdayProvided, birthday,
            favouriteProvided, favourite, preferencesProvided, preferences, now);
    }

    private static bool Accepted<T>(ProfileField<T> field) where T : class => field.IsSupplied && field.Value is not null;

    private static string ValidateName(string raw, string field, int maximum, bool allowEmpty)
    {
        var value = raw.Trim();
        if (raw.Any(char.IsControl) || value.Length > maximum || !allowEmpty && value.Length == 0)
            throw ProfileFieldFault(field);
        return value;
    }

    private static MobileAuthException ProfileFieldFault(string field) =>
        Fault(MobileAuthFaultKind.Validation, MobileAuthErrorCode.InvalidProfileField,
            $"Μη έγκυρο πεδίο προφίλ: {field}.");

    private static MobileAuthException NotificationPreferencesFault() =>
        Fault(MobileAuthFaultKind.Validation, MobileAuthErrorCode.InvalidNotificationPreferences,
            "Οι προτιμήσεις ειδοποιήσεων πρέπει να περιέχουν έως 64 επιλογές με σύντομα ονόματα και τιμές true/false.");
}
