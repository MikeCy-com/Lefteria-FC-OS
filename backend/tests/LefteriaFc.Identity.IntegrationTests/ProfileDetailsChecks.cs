using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Domain.Organizations;
using LefteriaFc.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    private const string ProfileDetailsPath = "/api/mobile/profile";

    private async Task ProfileDetailsAllowlistAsync()
    {
        using var anonymous = Client();
        await RejectProfileDetailsAsync(anonymous, new { name = "Anonymous write" }, 401);

        using var client = Client();
        var phone = database.NewPhone();
        var fanId = await database.SeedIdentityAsync(phone, MobileRole.Fan, "Original fan name");
        await database.SeedIdentityAsync(phone, MobileRole.Parent, "Independent parent name");
        var foreignPhone = database.NewPhone();
        var foreignId = await database.SeedIdentityAsync(foreignPhone, MobileRole.Fan, "Foreign profile name");
        await database.SetFavoriteTeamAsync(fanId, "both");
        var login = await LoginAsync(client, phone, "fan");
        var sessionId = Guid.Parse(Claims(login.GetProperty("token").GetString()!).GetProperty("sid").GetString()!);
        var beforeAudit = await database.CountSuccessfulAuditsAsync(fanId);
        var before = (await database.IdentityAsync(phone, MobileRole.Fan))!;
        var started = DateTimeOffset.UtcNow.AddSeconds(-2);

        var user = await PutProfileDetailsAsync(client, new
        {
            name = "Explicit profile name", email = "supporter@example.invalid",
            first_name = "Ανδρέας", last_name = "Προφίλ", birthday = "1990-04-05",
            favourite_player_id = "legacy-roster-player:unresolved",
            notification_prefs = new Dictionary<string, bool> { ["club_news"] = true, ["future_boolean_setting"] = false },
            id = foreignId, role = "management", phone = foreignPhone.WireValue,
            is_active = false, legacy_id = "untrusted-legacy-id", linked_player_id = "untrusted-player",
            linked_player_ids = new[] { "untrusted-player" }, linked_staff_id = "untrusted-staff",
            management_admin_id = Guid.NewGuid(), organization_id = OrganizationIds.Academy,
            academy_group_ids = new[] { "untrusted-group" }, team_id = "untrusted-team",
            favorite_team = "academy", avatar_url = "https://example.invalid/untrusted-avatar"
        });

        Check.Equal(fanId, user.GetProperty("id").GetGuid(), "Profile body changed the authorized subject.");
        Check.Equal("fan", user.GetProperty("role").GetString(), "Profile body changed the authorized role.");
        Check.Equal(phone.WireValue, user.GetProperty("phone").GetString(), "Profile body changed the authorized phone.");
        AssertProfileText(user, "name", "Explicit profile name");
        AssertProfileText(user, "email", "supporter@example.invalid");
        AssertProfileText(user, "first_name", "Ανδρέας");
        AssertProfileText(user, "last_name", "Προφίλ");
        AssertProfileText(user, "birthday", "1990-04-05");
        AssertProfileText(user, "favourite_player_id", "legacy-roster-player:unresolved");
        AssertProfilePreferences(user, new() { ["club_news"] = true, ["future_boolean_setting"] = false });
        Check.Equal(0, user.GetProperty("linked_player_ids").GetArrayLength(), "A favourite or body field granted player ownership.");

        var row = (await database.IdentityAsync(phone, MobileRole.Fan))!;
        Check.Equal(fanId, row.Id, "Stored profile identity changed.");
        Check.Equal("fan", row.Role, "Stored profile role changed.");
        Check.Equal(phone.Digits, row.NormalizedPhone, "Stored profile phone changed.");
        Check.True(row.IsActive && row.LegacyId is null && row.ManagementAdminId is null,
            "Protected profile/link fields escaped the allowlist.");
        Check.Equal("both", row.FavoriteTeam, "Details update changed the separate team preference.");
        Check.Equal(before.AvatarUrl, row.AvatarUrl, "Details update accepted an unverified avatar URL.");
        Check.Equal(before.CreatedAt, row.CreatedAt, "Profile editing rewrote creation time.");
        Check.Equal(before.LastLogin, row.LastLogin, "Profile editing rewrote login time.");
        Check.Equal("Explicit profile name", row.DisplayName, "Explicit name lost to derived name.");
        Check.Equal("supporter@example.invalid", row.Email, "Email was not persisted.");
        Check.Equal("Ανδρέας", row.FirstName, "First name was not persisted.");
        Check.Equal("Προφίλ", row.LastName, "Last name was not persisted.");
        Check.Equal<DateOnly?>(new DateOnly(1990, 4, 5), row.Birthday, "Birthday was not persisted.");
        Check.Equal("legacy-roster-player:unresolved", row.FavouritePlayerId, "Favourite ID was not persisted as an opaque preference.");
        Check.True(row.UpdatedAt is { } updated && updated >= started && updated <= DateTimeOffset.UtcNow.AddSeconds(2),
            "Profile updated_at was not set to the current UTC operation time.");
        // PostgreSQL timestamps have microsecond precision; the in-request DTO
        // can retain the final .NET tick until the next database read.
        Check.True((row.UpdatedAt!.Value - user.GetProperty("updated_at").GetDateTimeOffset()).Duration() <= TimeSpan.FromTicks(10),
            "Response updated_at differs from persisted state.");
        Check.Equal("Independent parent name", (await database.IdentityAsync(phone, MobileRole.Parent))!.DisplayName,
            "Fan details write changed a same-phone role.");
        var foreign = (await database.IdentityAsync(foreignPhone, MobileRole.Fan))!;
        Check.True(foreign.DisplayName == "Foreign profile name" && foreign.Email is null && foreign.UpdatedAt is null,
            "Body identifiers changed another person's profile.");
        Check.Equal(beforeAudit + 1, await database.CountSuccessfulAuditsAsync(fanId), "Profile write lost its atomic success audit.");
        Check.Equal(0, await database.CountSuccessfulAuditsAsync(foreignId), "Foreign body identifier became audit actor.");
        await using (var context = database.CreateContext())
        {
            var audit = await context.SecurityAuditEvents.AsNoTracking().SingleAsync(entry =>
                entry.ActorIdentityId == fanId && entry.Action == "mobile.profile.update");
            Check.Equal("success", audit.Outcome, "Profile audit outcome differs.");
            Check.Equal("own_profile", audit.ReasonCode, "Profile audit reason differs.");
            Check.Equal(sessionId, audit.DeviceSessionId, "Profile audit lost the signed session binding.");
            Check.Equal(row.UpdatedAt!.Value, audit.OccurredAt, "Profile state and audit used different operation timestamps.");
        }
        var me = await GetAsync(client, "/api/mobile/auth/me", 200);
        AssertProfileText(me, "email", "supporter@example.invalid");
        AssertProfileText(me, "favourite_player_id", "legacy-roster-player:unresolved");
        AssertProfilePreferences(me, new() { ["club_news"] = true, ["future_boolean_setting"] = false });
    }

    private async Task ProfileDetailsMergeAsync()
    {
        using var client = Client();
        var phone = database.NewPhone();
        await database.SeedIdentityAsync(phone, MobileRole.Fan, "Original display");
        await LoginAsync(client, phone, "fan");
        var user = await PutProfileDetailsAsync(client, new
        {
            first_name = "First", last_name = "Last", birthday = "1992-02-29",
            email = "profile@example.invalid", favourite_player_id = "unresolved-player",
            notification_prefs = new Dictionary<string, bool> { ["news"] = true, ["matches"] = false }
        });
        AssertProfileText(user, "name", "First Last");
        user = await PutProfileDetailsAsync(client, new { first_name = "Updated" });
        AssertProfileText(user, "name", "Updated Last");
        user = await PutProfileDetailsAsync(client, new { last_name = "Surname" });
        AssertProfileText(user, "name", "Updated Surname");
        user = await PutProfileDetailsAsync(client, new { name = "Explicit winner", first_name = "Changed", last_name = "Parts" });
        AssertProfileText(user, "name", "Explicit winner");
        user = await PutProfileDetailsAsync(client, new Dictionary<string, object?> { ["name"] = null, ["last_name"] = "Merged" });
        AssertProfileText(user, "name", "Changed Merged");

        user = await PutProfileDetailsAsync(client, new Dictionary<string, object?>
        {
            ["name"] = null, ["email"] = null, ["first_name"] = null, ["last_name"] = null,
            ["birthday"] = null, ["favourite_player_id"] = null,
            ["notification_prefs"] = new Dictionary<string, bool> { ["future_unknown_boolean"] = true }
        });
        AssertProfileText(user, "name", "Changed Merged");
        AssertProfileText(user, "first_name", "Changed");
        AssertProfileText(user, "last_name", "Merged");
        AssertProfileText(user, "email", "profile@example.invalid");
        AssertProfileText(user, "birthday", "1992-02-29");
        AssertProfileText(user, "favourite_player_id", "unresolved-player");
        AssertProfilePreferences(user, new() { ["future_unknown_boolean"] = true });
        user = await PutProfileDetailsAsync(client, new { email = "", favourite_player_id = "", notification_prefs = new Dictionary<string, bool>() });
        Check.True(ProfileNullableText(user, "email") is null or "", "Empty email did not clear the preference.");
        Check.True(ProfileNullableText(user, "favourite_player_id") is null or "", "Empty favourite ID did not clear the preference.");
        AssertProfilePreferences(user, new());
        var row = (await database.IdentityAsync(phone, MobileRole.Fan))!;
        Check.True(string.IsNullOrEmpty(row.Email) && string.IsNullOrEmpty(row.FavouritePlayerId), "Cleared optional values remained stored.");

        user = await PutProfileDetailsAsync(client, new { first_name = "", last_name = "" });
        AssertProfileText(user, "name", "Changed Merged");
        user = await PutProfileDetailsAsync(client, new { name = new string('N', 200), first_name = new string('F', 100), last_name = new string('L', 100) });
        Check.Equal(200, user.GetProperty("name").GetString()!.Length, "Maximum explicit name was rejected or truncated.");
        var preferences = Enumerable.Range(0, 63).ToDictionary(value => "setting_" + value, value => value % 2 == 0);
        preferences[new string('k', 64)] = true;
        user = await PutProfileDetailsAsync(client, new { favourite_player_id = new string('p', 128), notification_prefs = preferences });
        Check.Equal(128, user.GetProperty("favourite_player_id").GetString()!.Length, "Maximum opaque favourite ID was truncated.");
        AssertProfilePreferences(user, preferences);

        await PutProfileDetailsAsync(client, new { first_name = "Before", last_name = "Concurrent" });
        await Task.WhenAll(
            PutProfileDetailsAsync(client, new { first_name = "ConcurrentFirst" }),
            PutProfileDetailsAsync(client, new { last_name = "ConcurrentLast" }));
        var merged = await GetAsync(client, "/api/mobile/auth/me", 200);
        AssertProfileText(merged, "first_name", "ConcurrentFirst");
        AssertProfileText(merged, "last_name", "ConcurrentLast");
        AssertProfileText(merged, "name", "ConcurrentFirst ConcurrentLast");
    }

    private async Task ProfileDetailsValidationAsync()
    {
        using var client = Client();
        var phone = database.NewPhone();
        var id = await database.SeedIdentityAsync(phone, MobileRole.Fan, "Unchanged validated profile");
        await LoginAsync(client, phone, "fan");
        await PutProfileDetailsAsync(client, new
        {
            first_name = "Existing", last_name = "Person", birthday = "1990-01-01",
            email = "existing@example.invalid", notification_prefs = new Dictionary<string, bool> { ["news"] = true }
        });
        var before = ProfileDetailsSnapshot((await database.IdentityAsync(phone, MobileRole.Fan))!);
        var audits = await database.CountSuccessfulAuditsAsync(id);
        await RejectProfileDetailsAsync(client, new { }, 400);
        await RejectProfileDetailsAsync(client, new { id = Guid.NewGuid(), role = "management", phone = "+35799123456", favorite_team = "both" }, 400);
        await RejectProfileDetailsAsync(client, new Dictionary<string, object?>
        {
            ["name"] = null, ["email"] = null, ["first_name"] = null, ["last_name"] = null,
            ["birthday"] = null, ["favourite_player_id"] = null, ["notification_prefs"] = null
        }, 400);

        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var invalidFields = new (string Field, object Value)[]
        {
            ("name", ""), ("name", "   "), ("name", new string('n', 201)), ("name", "Invalid\nName"),
            ("first_name", new string('f', 101)), ("last_name", new string('l', 101)),
            ("first_name", "Invalid\u0000First"), ("last_name", "Invalid\u007fLast"),
            ("birthday", "1990-02-29"), ("birthday", "01/02/1990"), ("birthday", ""), ("birthday", tomorrow),
            ("email", "not-an-email"), ("email", "Display Name <email@example.invalid>"),
            ("email", "a\nb@example.invalid"), ("email", new string('x', 255) + "@example.invalid"),
            ("favourite_player_id", new string('p', 129)), ("favourite_player_id", "invalid\nplayer")
        };
        foreach (var (field, value) in invalidFields)
        {
            await RejectProfileDetailsAsync(client, new Dictionary<string, object?> { [field] = value, ["notification_prefs"] = new Dictionary<string, bool> { ["should_not_commit"] = true } }, 422);
        }
        await RejectProfileDetailsAsync(client, new { first_name = new string('f', 100), last_name = new string('l', 100) }, 422);
        foreach (var malformed in new object[] { true, 42, "true", new[] { true }, new Dictionary<string, object?> { ["news"] = "false" }, new Dictionary<string, object?> { ["news"] = 0 }, new Dictionary<string, object?> { ["news"] = null } })
            await RejectProfileDetailsAsync(client, new { name = "Must not partially commit", notification_prefs = malformed }, 400, 422);
        await RejectProfileDetailsAsync(client, new { notification_prefs = Enumerable.Range(0, 65).ToDictionary(value => "key_" + value, _ => true) }, 400, 422);
        await RejectProfileDetailsAsync(client, new { notification_prefs = new Dictionary<string, bool> { [new string('k', 65)] = true } }, 400, 422);
        await RejectProfileDetailsAsync(client, new { notification_prefs = new Dictionary<string, bool> { ["invalid\nkey"] = true } }, 400, 422);
        foreach (var field in new[] { "name", "email", "first_name", "last_name", "birthday", "favourite_player_id" })
            await RejectProfileDetailsAsync(client, new Dictionary<string, object?> { [field] = new[] { "wrong-type" } }, 400, 422);

        using (var body = new StringContent(JsonSerializer.Serialize(new { name = "Must not commit", ignored = new string('x', 17 * 1024) }), Encoding.UTF8, "application/json"))
        using (var response = await client.PutAsync(ProfileDetailsPath, body))
            Check.Equal(413, (int)response.StatusCode, "Profile details exceeded its bounded JSON body size.");
        Check.Equal(before, ProfileDetailsSnapshot((await database.IdentityAsync(phone, MobileRole.Fan))!),
            "A rejected details request partially changed stored profile fields or updated_at.");
        Check.Equal(audits, await database.CountSuccessfulAuditsAsync(id), "Rejected profile details appended a success audit.");
    }

    private async Task ProfileDetailsRoleBindingAsync()
    {
        using var client = Client();
        var phone = database.NewPhone();
        var admin = await database.SeedAdminAsync(OrganizationIds.Club, OrganizationIds.Academy);
        var roles = new[] { MobileRole.Fan, MobileRole.Parent, MobileRole.Coach, MobileRole.Player, MobileRole.Management };
        var identities = new Dictionary<MobileRole, Guid>();
        foreach (var role in roles)
            identities[role] = await database.SeedIdentityAsync(phone, role, "Initial " + role.ToWireValue(), role == MobileRole.Management ? admin : null);
        var foreignPhone = database.NewPhone();
        var foreign = await database.SeedIdentityAsync(foreignPhone, MobileRole.Fan, "Separate person");
        var login = await LoginAsync(client, phone, "fan");
        var fanToken = login.GetProperty("token").GetString()!;
        string? managementToken = null;
        foreach (var role in roles)
        {
            var wire = role.ToWireValue();
            if (role != MobileRole.Fan)
            {
                var switched = await PostAsync(client, "/api/mobile/auth/switch-role", new { role = wire }, 200);
                var token = switched.GetProperty("token").GetString()!;
                client.DefaultRequestHeaders.Authorization = new("Bearer", token);
                if (role == MobileRole.Management) managementToken = token;
            }
            var user = await PutProfileDetailsAsync(client, new
            {
                name = "Updated " + wire, email = wire + "@example.invalid", favourite_player_id = "unresolved-" + wire,
                id = foreign, role = "fan", phone = foreignPhone.WireValue, organization_id = OrganizationIds.Academy,
                linked_player_ids = new[] { "forbidden-link" }
            });
            Check.Equal(identities[role], user.GetProperty("id").GetGuid(), "An active role wrote another identity.");
            Check.Equal(wire, user.GetProperty("role").GetString(), "Profile update changed the signed active role.");
            Check.Equal(0, user.GetProperty("linked_player_ids").GetArrayLength(), "A profile preference granted player links.");
        }
        foreach (var role in roles)
        {
            var row = (await database.IdentityAsync(phone, role))!;
            Check.Equal("Updated " + role.ToWireValue(), row.DisplayName, "A same-phone role write overwrote another profile.");
            Check.Equal(role.ToWireValue() + "@example.invalid", row.Email, "Email leaked between role profiles.");
        }
        Check.True(await database.ActiveOrganizationAsync(admin) is null, "Profile body selected a management organization.");
        var fanSnapshot = ProfileDetailsSnapshot((await database.IdentityAsync(phone, MobileRole.Fan))!);
        var foreignSnapshot = ProfileDetailsSnapshot((await database.IdentityAsync(foreignPhone, MobileRole.Fan))!);
        var beforeDeniedAudits = await database.CountSuccessfulAuditsAsync(identities[MobileRole.Fan]);
        foreach (var token in new[]
        {
            SignedToken(identities[MobileRole.Fan], phone, IdentityApplicationFactory.Issuer, IdentityApplicationFactory.Audience, "mobile_access", "parent", false),
            SignedToken(identities[MobileRole.Fan], foreignPhone, IdentityApplicationFactory.Issuer, IdentityApplicationFactory.Audience, "mobile_access", "fan", false),
            SignedToken(foreign, phone, IdentityApplicationFactory.Issuer, IdentityApplicationFactory.Audience, "mobile_access", "fan", false)
        })
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            await RejectProfileDetailsAsync(client, new { name = "Mismatched session" }, 401, 403);
        }
        Check.Equal(fanSnapshot, ProfileDetailsSnapshot((await database.IdentityAsync(phone, MobileRole.Fan))!), "Mismatched signed claims changed the selected profile.");
        Check.Equal(foreignSnapshot, ProfileDetailsSnapshot((await database.IdentityAsync(foreignPhone, MobileRole.Fan))!), "Mismatched signed claims changed another person.");
        Check.Equal(beforeDeniedAudits, await database.CountSuccessfulAuditsAsync(identities[MobileRole.Fan]), "Mismatched claims appended a success audit.");

        client.DefaultRequestHeaders.Authorization = new("Bearer", fanToken);
        await database.SetIdentityActiveAsync(identities[MobileRole.Fan], false);
        var revoked = ProfileDetailsSnapshot((await database.IdentityAsync(phone, MobileRole.Fan))!);
        await RejectProfileDetailsAsync(client, new { first_name = "Revoked" }, 401, 403);
        Check.Equal(revoked, ProfileDetailsSnapshot((await database.IdentityAsync(phone, MobileRole.Fan))!), "Revoked role wrote profile details.");
        Check.Equal(beforeDeniedAudits, await database.CountSuccessfulAuditsAsync(identities[MobileRole.Fan]), "Revoked role appended a success audit.");
        client.DefaultRequestHeaders.Authorization = new("Bearer", managementToken ?? throw new InvalidOperationException("Management token was not issued."));
        await database.SetAdminActiveAsync(admin, false);
        var management = ProfileDetailsSnapshot((await database.IdentityAsync(phone, MobileRole.Management))!);
        var managementAudits = await database.CountSuccessfulAuditsAsync(identities[MobileRole.Management]);
        await RejectProfileDetailsAsync(client, new { name = "Revoked administrator" }, 401, 403);
        Check.Equal(management, ProfileDetailsSnapshot((await database.IdentityAsync(phone, MobileRole.Management))!), "Revoked linked admin wrote profile details.");
        Check.Equal(managementAudits, await database.CountSuccessfulAuditsAsync(identities[MobileRole.Management]), "Revoked admin appended a success audit.");
    }

    private static async Task<JsonElement> PutProfileDetailsAsync(HttpClient client, object body)
    {
        using var response = await client.PutAsJsonAsync(ProfileDetailsPath, body);
        Check.True(response.Headers.CacheControl?.NoStore == true, "Profile details response must be no-store.");
        return await ExpectAsync(response, 200);
    }

    private static async Task RejectProfileDetailsAsync(HttpClient client, object body, params int[] statuses)
    {
        using var response = await client.PutAsJsonAsync(ProfileDetailsPath, body);
        Check.True(statuses.Contains((int)response.StatusCode),
            $"Profile details accepted invalid or unauthorized input: observed HTTP {(int)response.StatusCode}.");
        Check.True(response.Headers.CacheControl?.NoStore == true, "Rejected profile details response must be no-store.");
    }

    private static void AssertProfileText(JsonElement user, string property, string expected) =>
        Check.Equal(expected, user.GetProperty(property).GetString(), "Profile response differs for " + property + ".");

    private static string? ProfileNullableText(JsonElement user, string property) =>
        user.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;

    private static void AssertProfilePreferences(JsonElement user, Dictionary<string, bool> expected)
    {
        var preferences = user.GetProperty("notification_prefs");
        Check.Equal(expected.Count, preferences.EnumerateObject().Count(), "Notification preferences were merged instead of replaced.");
        foreach (var (key, value) in expected)
            Check.Equal(value, preferences.GetProperty(key).GetBoolean(), "Notification preference was not stored as a boolean.");
    }

    private static string ProfileDetailsSnapshot(MobileIdentityRecord row) => JsonSerializer.Serialize(new
    {
        row.Id, row.NormalizedPhone, row.Role, row.IsActive, row.LegacyId, row.ManagementAdminId,
        row.DisplayName, row.FirstName, row.LastName, row.Birthday, row.Email, row.FavouritePlayerId,
        row.NotificationPreferencesJson, row.FavoriteTeam, row.AvatarUrl, row.CreatedAt, row.UpdatedAt, row.LastLogin, row.Platform
    });
}
