using System.Net.Http.Headers;
using System.Text.Json;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    // Same physical account, different server-issued role tokens. Cached role
    // labels or selected-child query strings are never permission grants.
    private async Task CalendarRoleSwitchBoundaryChecksAsync(
        CalendarDatabaseFixture input)
    {
        using var client = Client();
        await LoginAsync(client, input.Accounts.ParentPhone, "parent");
        const string family = "/api/mobile/family";
        const string calendar = "/api/mobile/calendar?month=2026-10";
        const string responses = "/api/mobile/my-availability";
        var parentFamily = await CalendarRequestAsync(client, HttpMethod.Get, family, null, 200);
        Check.True(parentFamily.GetProperty("children").GetArrayLength() == 3,
            "The parent session lost its verified children before role switching.");

        var fanSwitch = await PostAsync(client, "/api/mobile/auth/switch-role",
            new { role = "fan" }, 200);
        Check.Equal("fan", fanSwitch.GetProperty("user").GetProperty("role").GetString(),
            "Role switch did not issue a fan identity.");
        var fanToken = fanSwitch.GetProperty("token").GetString();
        Check.True(!string.IsNullOrWhiteSpace(fanToken),
            "Fan role switch did not issue a replacement token.");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", fanToken);

        var fanMe = await GetAsync(client, "/api/mobile/auth/me", 200);
        Check.Equal("fan", fanMe.GetProperty("role").GetString(),
            "Current verified identity remained a parent after fan switch.");
        foreach (var path in new[]
        {
            family,
            calendar,
            calendar + "&player_id=" + input.Accounts.ChildId,
            responses
        })
        {
            var denied = await CalendarRequestAsync(client, HttpMethod.Get, path, null, 403);
            Check.True(!denied.TryGetProperty("children", out _) &&
                       !denied.TryGetProperty("players", out _) &&
                       !denied.TryGetProperty("items", out _),
                "The fan token disclosed guardian or calendar data at " + path);
        }

        var deniedWrite = await CalendarRequestAsync(client, HttpMethod.Post,
            "/api/mobile/availability",
            new { event_id = input.Id("host"), player_id = input.Accounts.ChildId,
                  status = "going", role = "parent" }, 403);
        Check.True(!deniedWrite.TryGetProperty("status", out _) &&
                   !deniedWrite.TryGetProperty("player_id", out _),
            "Unsigned parent role or child hints enabled a fan RSVP.");

        var parentSwitch = await PostAsync(client, "/api/mobile/auth/switch-role",
            new { role = "parent" }, 200);
        Check.Equal("parent", parentSwitch.GetProperty("user").GetProperty("role").GetString(),
            "Verified parent role could not be restored after fan mode.");
        var parentToken = parentSwitch.GetProperty("token").GetString();
        Check.True(!string.IsNullOrWhiteSpace(parentToken),
            "Parent role recovery did not issue a replacement token.");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", parentToken);

        var recoveredFamily = await CalendarRequestAsync(client, HttpMethod.Get,
            family, null, 200);
        Check.True(recoveredFamily.GetProperty("children").GetRawText() ==
                   parentFamily.GetProperty("children").GetRawText(),
            "Server-verified parent role recovery changed the authorized child roster.");
        var recoveredCalendar = await CalendarRequestAsync(client, HttpMethod.Get,
            calendar + "&player_id=" + input.Accounts.ChildId, null, 200);
        Check.Equal(input.Accounts.ChildId,
            recoveredCalendar.GetProperty("selected_player_id").GetString(),
            "Revalidated parent session lost its scoped child calendar.");
    }
}
