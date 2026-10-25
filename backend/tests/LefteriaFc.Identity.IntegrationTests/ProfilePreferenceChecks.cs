using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LefteriaFc.Domain.Identity;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    private async Task ProfilePreferenceAsync()
    {
        using var anonymous = Client();
        await PutAsync(anonymous, new { favorite_team = "first_team" }, 401);

        using var client = Client();
        var phone = database.NewPhone();
        var fanId = await database.SeedIdentityAsync(phone, MobileRole.Fan, "Independent profile fixture");
        var parentId = await database.SeedIdentityAsync(phone, MobileRole.Parent);
        await database.SetFavoriteTeamAsync(parentId, "both");
        var foreignPhone = database.NewPhone();
        var foreignId = await database.SeedIdentityAsync(foreignPhone, MobileRole.Fan);
        await database.SetFavoriteTeamAsync(foreignId, "academy");
        var login = await LoginAsync(client, phone, "fan");
        var originalToken = login.GetProperty("token").GetString()!;
        var before = await database.CountSuccessfulAuditsAsync(fanId);

        var updated = await PutAsync(client, new
        {
            favorite_team = "first_team", id = foreignId, role = "management",
            phone = foreignPhone.WireValue, name = "Untrusted replacement", avatar_url = "https://example.invalid/ignored"
        }, 200);
        Check.True(updated.GetProperty("ok").GetBoolean(), "Preference update did not return ok:true.");
        var user = updated.GetProperty("user");
        Check.Equal(fanId, user.GetProperty("id").GetGuid(), "Body identifiers changed the authorized identity.");
        Check.Equal("fan", user.GetProperty("role").GetString(), "Body role changed the authorized identity.");
        Check.Equal(phone.WireValue, user.GetProperty("phone").GetString(), "Body phone changed the authorized identity.");
        Check.Equal("first_team", user.GetProperty("favorite_team").GetString(), "Response lost the saved preference.");
        Check.Equal("Independent profile fixture", (await database.IdentityAsync(phone, MobileRole.Fan))!.DisplayName,
            "Unknown fields escaped the profile allowlist.");
        Check.Equal("both", (await database.IdentityAsync(phone, MobileRole.Parent))!.FavoriteTeam,
            "Updating fan preference changed another same-phone role.");
        Check.Equal("academy", (await database.IdentityAsync(foreignPhone, MobileRole.Fan))!.FavoriteTeam,
            "Body identifiers modified a foreign identity.");
        Check.Equal(before + 1, await database.CountSuccessfulAuditsAsync(fanId), "Successful preference update did not append its actor audit.");
        Check.Equal(0, await database.CountSuccessfulAuditsAsync(foreignId), "Untrusted body identity became the audit actor.");

        foreach (var value in new[] { "academy", "both" })
        {
            var response = await PutAsync(client, new { favorite_team = value }, 200);
            Check.True(response.GetProperty("ok").GetBoolean(), "Allowed preference update failed.");
            Check.Equal(value, response.GetProperty("user").GetProperty("favorite_team").GetString(), "Allowed preference differs.");
        }
        var cleared = await PutAsync(client, new { favorite_team = (string?)null }, 200);
        Check.True(cleared.GetProperty("ok").GetBoolean(), "Explicit null did not clear the preference.");
        Check.True(await FavoriteAsync(phone, MobileRole.Fan) is null, "Explicit null was treated as an omitted field.");
        var afterClear = await database.CountSuccessfulAuditsAsync(fanId);
        await PutAsync(client, new { }, 400);
        await PutAsync(client, new { id = foreignId, role = "parent", phone = foreignPhone.WireValue }, 400);
        foreach (var json in new[] { "\"foreign-club\"", "\"Academy\"", "true", "3", "{}", "[]" })
        {
            using var document = JsonDocument.Parse(json);
            await PutAsync(client, new Dictionary<string, object?> { ["favorite_team"] = document.RootElement.Clone() }, 422);
        }
        Check.True(await FavoriteAsync(phone, MobileRole.Fan) is null, "Invalid preference input changed stored state.");
        Check.Equal(afterClear, await database.CountSuccessfulAuditsAsync(fanId), "Rejected preference input emitted a success audit.");

        // JWTs have valid test signatures; their mismatched identity bindings must still fail.
        foreach (var token in new[]
        {
            SignedToken(fanId, phone, IdentityApplicationFactory.Issuer, IdentityApplicationFactory.Audience, "mobile_access", "parent", false),
            SignedToken(fanId, foreignPhone, IdentityApplicationFactory.Issuer, IdentityApplicationFactory.Audience, "mobile_access", "fan", false),
            SignedToken(foreignId, phone, IdentityApplicationFactory.Issuer, IdentityApplicationFactory.Audience, "mobile_access", "fan", false)
        })
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            await PutDeniedAsync(client);
        }
        client.DefaultRequestHeaders.Authorization = new("Bearer", originalToken);
        Check.True(await FavoriteAsync(phone, MobileRole.Fan) is null, "Mismatched signed identity updated the preference.");
        Check.Equal("academy", await FavoriteAsync(foreignPhone, MobileRole.Fan), "Mismatched signed subject updated foreign profile data.");

        await PutAsync(client, new { favorite_team = "first_team" }, 200);
        var parent = await PostAsync(client, "/api/mobile/auth/switch-role", new { role = "parent" }, 200);
        Check.Equal("both", parent.GetProperty("user").GetProperty("favorite_team").GetString(), "Role switch merged preferences.");
        client.DefaultRequestHeaders.Authorization = new("Bearer", parent.GetProperty("token").GetString());
        await PutAsync(client, new { favorite_team = "academy" }, 200);
        var fan = await PostAsync(client, "/api/mobile/auth/switch-role", new { role = "fan" }, 200);
        Check.Equal("first_team", fan.GetProperty("user").GetProperty("favorite_team").GetString(), "Another role's write changed fan preference.");
        client.DefaultRequestHeaders.Authorization = new("Bearer", fan.GetProperty("token").GetString());

        await database.SetIdentityActiveAsync(fanId, false);
        var beforeRevokedAttempt = await database.CountSuccessfulAuditsAsync(fanId);
        await PutDeniedAsync(client);
        Check.Equal("first_team", await FavoriteAsync(phone, MobileRole.Fan), "Revoked role mutated its preference.");
        Check.Equal("academy", await FavoriteAsync(phone, MobileRole.Parent), "Revoked role mutated another role.");
        Check.Equal(beforeRevokedAttempt, await database.CountSuccessfulAuditsAsync(fanId), "Revoked role emitted a success audit.");
    }

    private async Task<string?> FavoriteAsync(PhoneNumber phone, MobileRole role) =>
        (await database.IdentityAsync(phone, role))!.FavoriteTeam;

    private static async Task<JsonElement> PutAsync(HttpClient client, object body, int status)
    {
        using var response = await client.PutAsJsonAsync("/api/mobile/auth/me", body);
        return await ExpectAsync(response, status);
    }

    private static async Task PutDeniedAsync(HttpClient client)
    {
        using var response = await client.PutAsJsonAsync("/api/mobile/auth/me", new { favorite_team = "both" });
        Check.True((int)response.StatusCode is 401 or 403, "An unavailable or mismatched signed identity was permitted to write.");
    }
}
