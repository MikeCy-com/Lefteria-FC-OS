using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Domain.Organizations;
using LefteriaFc.Identity.IntegrationTests;

namespace LefteriaFc.MobilePreview;

// Only fixed checks, phase names and numeric HTTP statuses belong here. Never
// wrap an external exception message, token, response body or request selector.
internal sealed class PreviewSmokeException(string check, HttpStatusCode? status = null)
    : Exception(check + (status is null ? string.Empty : $" Observed HTTP {(int)status.Value}."));

internal static class PreviewSmoke
{
    private enum Failure { InvalidJson, MissingJsonField, UnexpectedJsonTypeOrState, HttpTransport, RequestCancelled, Unexpected }

    private enum Phase
    {
        Initialization, Readiness, AnonymousIdentity, OtpIssue, RolePicker, FanLogin,
        OwnIdentity, OversizedProfile, ProfileUpdate, PreferenceUpdate, RoleSwitch,
        OrganizationRead, OrganizationSelection, Calendar, WrongHost, WrongOrigin, Stop
    }

    public static async Task RunAsync(PreviewOptions options, PhoneNumber phone, CalendarDatabaseFixture? calendar = null)
    {
        var phase = Phase.Initialization;
        try
        {
            await RunScenarioAsync(options, phone, calendar, value => phase = value);
        }
        catch (PreviewSmokeException) { throw; }
        catch (Exception error)
        {
            // Classify using closed labels only; never include external messages,
            // response values, cookies, SQL or tokens in diagnostics.
            var failure = error switch
            {
                JsonException => Failure.InvalidJson,
                KeyNotFoundException => Failure.MissingJsonField,
                HttpRequestException => Failure.HttpTransport,
                OperationCanceledException => Failure.RequestCancelled,
                InvalidOperationException => Failure.UnexpectedJsonTypeOrState,
                _ => Failure.Unexpected
            };
            throw new PreviewSmokeException($"Preview smoke {failure} in {phase}.");
        }
    }

    private static async Task RunScenarioAsync(PreviewOptions options, PhoneNumber phone,
        CalendarDatabaseFixture? calendar, Action<Phase> phase)
    {
        Check(options.Origin.Host == "127.0.0.1", "Preview must stay on loopback.");
        using var client = new HttpClient(new SocketsHttpHandler
        {
            UseProxy = false, UseCookies = false, AllowAutoRedirect = false
        }) { BaseAddress = options.Origin, Timeout = TimeSpan.FromSeconds(10) };
        phase(Phase.Readiness);
        using (var ready = await client.GetAsync("/api/health/ready"))
            Check(ready.StatusCode == HttpStatusCode.OK, "API readiness failed.", ready.StatusCode);
        phase(Phase.AnonymousIdentity);
        using (var anonymous = await client.GetAsync("/api/mobile/auth/me"))
            Check(anonymous.StatusCode == HttpStatusCode.Unauthorized, "Anonymous authentication boundary failed.", anonymous.StatusCode);
        phase(Phase.OtpIssue);
        using var issue = await JsonAsync(client, HttpMethod.Post, "/api/mobile/auth/request-otp", new { phone = phone.WireValue });
        Check(issue.RootElement.GetProperty("expires_in").GetInt32() == 300, "OTP expiration differs.");
        phase(Phase.RolePicker);
        using var picker = await JsonAsync(client, HttpMethod.Post, "/api/mobile/auth/verify-otp",
            new { phone = phone.WireValue, code = FixedFixtureOtpProtector.Code, remember_days = (int?)null });
        Check(picker.RootElement.GetProperty("identities").GetArrayLength() == (calendar is null ? 5 : 2) &&
              !picker.RootElement.TryGetProperty("token", out _), "Role picker contract failed.");
        phase(Phase.FanLogin);
        using var login = await JsonAsync(client, HttpMethod.Post, "/api/mobile/auth/verify-otp",
            new { phone = phone.WireValue, code = FixedFixtureOtpProtector.Code, role = "fan", remember_days = (int?)null });
        var fanToken = login.RootElement.GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fanToken);
        phase(Phase.OwnIdentity);
        using var me = await JsonAsync(client, HttpMethod.Get, "/api/mobile/auth/me");
        Check(me.RootElement.GetProperty("role").GetString() == "fan", "Fan identity failed.");
        // Keep the same HTTP client and immediately follow each rejected upload
        // with a real mutation. Repetition exercises connection reuse; these are
        // independent assertions, never retries of a failed request.
        for (var cycle = 0; cycle < 3; cycle++)
        {
            phase(Phase.OversizedProfile);
            // JsonContent has no precomputed length, so the middle request also
            // exercises the proxy's chunked-body read before forwarding.
            using (HttpContent oversized = cycle == 1
                       ? JsonContent.Create(new { email = new string('x', 17 * 1024) })
                       : new StringContent(JsonSerializer.Serialize(new { email = new string('x', 17 * 1024) }), Encoding.UTF8, "application/json"))
            {
                using var rejected = await client.PutAsync("/api/mobile/profile", oversized);
                Check(rejected.StatusCode == HttpStatusCode.RequestEntityTooLarge, "The API body limit was lost through the proxy.", rejected.StatusCode);
            }
            phase(Phase.ProfileUpdate);
            var expectedEmail = $"edited-{cycle}@example.invalid";
            using var profile = await JsonAsync(client, HttpMethod.Put, "/api/mobile/profile",
                new { first_name = "Δοκιμή", last_name = "Προεπισκόπησης", email = expectedEmail });
            Check(profile.RootElement.ValueKind == JsonValueKind.Object, "Profile update returned a non-object JSON response.");
            Check(profile.RootElement.TryGetProperty("email", out var email), "Profile update response omitted the email field.");
            Check(email.ValueKind == JsonValueKind.String, "Profile update response email was not a JSON string.");
            Check(email.GetString() == expectedEmail, "Profile update returned an unchanged or different email.");
        }
        phase(Phase.PreferenceUpdate);
        using var preference = await JsonAsync(client, HttpMethod.Put, "/api/mobile/auth/me", new { favorite_team = "academy" });
        Check(preference.RootElement.GetProperty("user").GetProperty("favorite_team").GetString() == "academy", "Preference update failed.");
        phase(Phase.RoleSwitch);
        var previousToken = calendar is null ? fanToken : await LoginAsync(client, calendar.Accounts.StaffPhone, "coach");
        using var switched = await JsonAsync(client, HttpMethod.Post, "/api/mobile/auth/switch-role", new { role = "management" });
        var managementToken = switched.RootElement.GetProperty("token").GetString()!;
        Check(Expiration(previousToken) == Expiration(managementToken), "Role switch extended session expiration.");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", managementToken);
        phase(Phase.OrganizationRead);
        using var organization = await JsonAsync(client, HttpMethod.Get, "/api/mobile/management/organization");
        Check(organization.RootElement.GetProperty("requires_organization_selection").GetBoolean() == (calendar is null), "Organization choice differs from the fixture.");
        phase(Phase.OrganizationSelection);
        using var selected = await JsonAsync(client, HttpMethod.Post, "/api/mobile/management/organization",
            new { organization_id = OrganizationIds.Club });
        Check(selected.RootElement.GetProperty("organization_id").GetString() == OrganizationIds.Club, "Organization selection failed.");
        phase(Phase.Calendar);
        if (calendar is not null) await CalendarAsync(client, calendar);
        phase(Phase.WrongHost);
        using (var wrongHost = new HttpRequestMessage(HttpMethod.Get, "/api/health/ready"))
        {
            wrongHost.Headers.Host = "untrusted.invalid";
            using var rejected = await client.SendAsync(wrongHost);
            Check(rejected.StatusCode == HttpStatusCode.Forbidden, "Nonlocal Host was accepted.", rejected.StatusCode);
        }
        phase(Phase.WrongOrigin);
        using (var crossOrigin = new HttpRequestMessage(HttpMethod.Post, "/__fixture__/stop"))
        {
            crossOrigin.Headers.Add("Origin", "http://untrusted.invalid");
            using var rejected = await client.SendAsync(crossOrigin);
            Check(rejected.StatusCode == HttpStatusCode.Forbidden, "Cross-origin stop was accepted.", rejected.StatusCode);
        }
        phase(Phase.Stop);
        using var stop = await client.PostAsync("/__fixture__/stop", null);
        Check(stop.StatusCode == HttpStatusCode.Accepted, "Graceful stop failed.", stop.StatusCode);
    }

    private static async Task CalendarAsync(HttpClient client, CalendarDatabaseFixture fixture)
    {
        const string monthPath = "/api/mobile/calendar?month=2026-10";
        using var club = await JsonAsync(client, HttpMethod.Get, monthPath);
        Check(club.RootElement.GetProperty("role").GetString() == "management" &&
              club.RootElement.GetProperty("organization_id").GetString() == OrganizationIds.Club &&
              Contains(club, fixture.Id("shared")) && !Contains(club, fixture.Id("host")), "Management club calendar scope failed.");
        using var selected = await JsonAsync(client, HttpMethod.Post, "/api/mobile/management/organization", new { organization_id = OrganizationIds.Academy });
        using var academy = await JsonAsync(client, HttpMethod.Get, monthPath);
        Check(academy.RootElement.GetProperty("organization_id").GetString() == OrganizationIds.Academy &&
              Contains(academy, fixture.Id("host")) && !Contains(academy, fixture.Id("shared")), "Management Academy calendar scope failed.");

        await LoginAsync(client, fixture.Accounts.ParentPhone, "parent");
        using var parent = await JsonAsync(client, HttpMethod.Get, monthPath);
        Check(parent.RootElement.GetProperty("players").GetArrayLength() == 3 &&
              parent.RootElement.GetProperty("selected_player_id").GetString() == fixture.Accounts.ChildId &&
              Contains(parent, fixture.Id("published")) && !Contains(parent, fixture.Id("draft")), "Parent children or published calendar failed.");
        var secondPath = monthPath + "&player_id=" + Uri.EscapeDataString(fixture.SecondChild);
        using var second = await JsonAsync(client, HttpMethod.Get, secondPath);
        Check(second.RootElement.GetProperty("selected_player_id").GetString() == fixture.SecondChild &&
              Contains(second, fixture.Id("group-two")) && !Contains(second, fixture.Id("published")), "Second-child calendar selection failed.");
        using var saved = await JsonAsync(client, HttpMethod.Post, "/api/mobile/availability",
            new { event_id = fixture.Id("host"), event_type = "event", player_id = fixture.SecondChild, status = "maybe" });
        Check(saved.RootElement.GetProperty("player_id").GetString() == fixture.SecondChild, "Second-child RSVP target changed.");
        using var confirmed = await JsonAsync(client, HttpMethod.Get, secondPath);
        var response = confirmed.RootElement.GetProperty("items").EnumerateArray().Single(row => row.GetProperty("id").GetString() == fixture.Id("host"))
            .GetProperty("responses").EnumerateArray().Single();
        Check(response.GetProperty("player_id").GetString() == fixture.SecondChild && response.GetProperty("status").GetString() == "maybe", "Fresh calendar did not confirm the own-child RSVP.");
        using var cancelled = await client.PostAsJsonAsync("/api/mobile/availability",
            new { event_id = fixture.Id("cancelled"), event_type = "event", player_id = fixture.Accounts.ChildId, status = "going" });
        Check(cancelled.StatusCode == HttpStatusCode.Forbidden, "Cancelled event accepted RSVP.", cancelled.StatusCode);

        await LoginAsync(client, fixture.Accounts.StaffPhone, "coach");
        using var coach = await JsonAsync(client, HttpMethod.Get, monthPath);
        Check(coach.RootElement.GetProperty("role").GetString() == "coach" && Contains(coach, fixture.Id("host")) &&
              Contains(coach, fixture.Id("shared")) && !Contains(coach, fixture.Id("group-two")), "Coach assignments failed.");
        var repliesPath = "/api/mobile/calendar/event/" + Uri.EscapeDataString(fixture.Id("host")) + "/responses";
        using var firstReplies = await JsonAsync(client, HttpMethod.Get, repliesPath);
        using var lastReplies = await JsonAsync(client, HttpMethod.Get, repliesPath + "?offset=200");
        var replyPlayers = firstReplies.RootElement.GetProperty("players").EnumerateArray()
            .Concat(lastReplies.RootElement.GetProperty("players").EnumerateArray()).ToArray();
        Check(firstReplies.RootElement.GetProperty("next_offset").GetInt32() == 200 && replyPlayers.Length == 201 &&
              replyPlayers.Single(row => row.GetProperty("id").GetString() == fixture.SecondChild).GetProperty("status").GetString() == "maybe", "Scoped staff response pagination failed.");
        using var staffWrite = await client.PostAsJsonAsync("/api/mobile/availability",
            new { event_id = fixture.Id("host"), event_type = "event", player_id = fixture.Accounts.ChildId, status = "going" });
        Check(staffWrite.StatusCode == HttpStatusCode.Forbidden, "Read-only coach view accepted own-player RSVP.", staffWrite.StatusCode);

        await LoginAsync(client, fixture.Accounts.PlayerPhone, "player");
        using var player = await JsonAsync(client, HttpMethod.Get, monthPath);
        Check(player.RootElement.GetProperty("role").GetString() == "player" &&
              player.RootElement.GetProperty("selected_player_id").GetString() == fixture.Accounts.PlayerId &&
              Contains(player, fixture.Id("shared")) && !Contains(player, fixture.Id("host")), "Player calendar crossed its scope.");
        using var playerSaved = await JsonAsync(client, HttpMethod.Post, "/api/mobile/availability",
            new { event_id = fixture.Id("shared"), event_type = "match", player_id = fixture.Accounts.PlayerId, status = "not_going" });
        Check(playerSaved.RootElement.GetProperty("event_type").GetString() == "match", "Player RSVP lost the kind/id boundary.");
    }

    private static bool Contains(JsonDocument calendar, string eventId) => calendar.RootElement.GetProperty("items")
        .EnumerateArray().Any(row => row.GetProperty("id").GetString() == eventId);

    private static async Task<string> LoginAsync(HttpClient client, PhoneNumber phone, string role)
    {
        client.DefaultRequestHeaders.Authorization = null;
        using var issue = await JsonAsync(client, HttpMethod.Post, "/api/mobile/auth/request-otp", new { phone = phone.WireValue });
        using var login = await JsonAsync(client, HttpMethod.Post, "/api/mobile/auth/verify-otp",
            new { phone = phone.WireValue, code = FixedFixtureOtpProtector.Code, role, remember_days = (int?)null });
        var token = login.RootElement.GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return token;
    }

    private static async Task<JsonDocument> JsonAsync(HttpClient client, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request);
        Check(response.StatusCode == HttpStatusCode.OK, Operation(method, path) + " failed.", response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static string Operation(HttpMethod method, string path)
    {
        var route = path.Split('?', 2)[0];
        return (method.Method, route) switch
        {
            ("POST", "/api/mobile/auth/request-otp") => "Preview OTP issue",
            ("POST", "/api/mobile/auth/verify-otp") => "Preview OTP verification",
            ("GET", "/api/mobile/auth/me") => "Preview own identity read",
            ("PUT", "/api/mobile/auth/me") => "Preview preference update",
            ("PUT", "/api/mobile/profile") => "Preview profile update",
            ("POST", "/api/mobile/auth/switch-role") => "Preview role switch",
            ("GET", "/api/mobile/management/organization") => "Preview organization read",
            ("POST", "/api/mobile/management/organization") => "Preview organization selection",
            ("GET", "/api/mobile/calendar") => "Preview calendar read",
            ("POST", "/api/mobile/availability") => "Preview RSVP update",
            _ => "Preview protected API operation"
        };
    }

    private static long Expiration(string token)
    {
        var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
        using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
        return document.RootElement.GetProperty("exp").GetInt64();
    }

    private static void Check(bool value, string message, HttpStatusCode? status = null)
    {
        if (!value) throw new PreviewSmokeException(message, status);
    }
}
