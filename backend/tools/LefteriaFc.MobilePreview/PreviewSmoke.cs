using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Domain.Organizations;

namespace LefteriaFc.MobilePreview;

internal static class PreviewSmoke
{
    public static async Task RunAsync(PreviewOptions options, PhoneNumber phone)
    {
        Check(options.Origin.Host == "127.0.0.1", "Preview must stay on loopback.");
        using var client = new HttpClient(new SocketsHttpHandler
        {
            UseProxy = false, UseCookies = false, AllowAutoRedirect = false
        }) { BaseAddress = options.Origin, Timeout = TimeSpan.FromSeconds(10) };
        using (var ready = await client.GetAsync("/api/health/ready"))
            Check(ready.StatusCode == HttpStatusCode.OK, "API readiness failed.");
        using (var anonymous = await client.GetAsync("/api/mobile/auth/me"))
            Check(anonymous.StatusCode == HttpStatusCode.Unauthorized, "Anonymous authentication boundary failed.");
        using var issue = await JsonAsync(client, HttpMethod.Post, "/api/mobile/auth/request-otp", new { phone = phone.WireValue });
        Check(issue.RootElement.GetProperty("expires_in").GetInt32() == 300, "OTP expiration differs.");
        using var picker = await JsonAsync(client, HttpMethod.Post, "/api/mobile/auth/verify-otp",
            new { phone = phone.WireValue, code = FixedFixtureOtpProtector.Code, remember_days = (int?)null });
        Check(picker.RootElement.GetProperty("identities").GetArrayLength() == 5 &&
              !picker.RootElement.TryGetProperty("token", out _), "Role picker contract failed.");
        using var login = await JsonAsync(client, HttpMethod.Post, "/api/mobile/auth/verify-otp",
            new { phone = phone.WireValue, code = FixedFixtureOtpProtector.Code, role = "fan", remember_days = (int?)null });
        var fanToken = login.RootElement.GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fanToken);
        using var me = await JsonAsync(client, HttpMethod.Get, "/api/mobile/auth/me");
        Check(me.RootElement.GetProperty("role").GetString() == "fan", "Fan identity failed.");
        using (var oversized = new StringContent(JsonSerializer.Serialize(new { email = new string('x', 17 * 1024) }),
                   Encoding.UTF8, "application/json"))
        {
            using var rejected = await client.PutAsync("/api/mobile/profile", oversized);
            Check(rejected.StatusCode == HttpStatusCode.RequestEntityTooLarge, "The API body limit was lost through the proxy.");
        }
        using var profile = await JsonAsync(client, HttpMethod.Put, "/api/mobile/profile",
            new { first_name = "Δοκιμή", last_name = "Προεπισκόπησης", email = "edited@example.invalid" });
        Check(profile.RootElement.GetProperty("email").GetString() == "edited@example.invalid", "Profile update failed.");
        using var preference = await JsonAsync(client, HttpMethod.Put, "/api/mobile/auth/me", new { favorite_team = "academy" });
        Check(preference.RootElement.GetProperty("user").GetProperty("favorite_team").GetString() == "academy", "Preference update failed.");
        using var switched = await JsonAsync(client, HttpMethod.Post, "/api/mobile/auth/switch-role", new { role = "management" });
        var managementToken = switched.RootElement.GetProperty("token").GetString()!;
        Check(Expiration(fanToken) == Expiration(managementToken), "Role switch extended session expiration.");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", managementToken);
        using var organization = await JsonAsync(client, HttpMethod.Get, "/api/mobile/management/organization");
        Check(organization.RootElement.GetProperty("requires_organization_selection").GetBoolean(), "Organization choice missing.");
        using var selected = await JsonAsync(client, HttpMethod.Post, "/api/mobile/management/organization",
            new { organization_id = OrganizationIds.Club });
        Check(selected.RootElement.GetProperty("organization_id").GetString() == OrganizationIds.Club, "Organization selection failed.");
        using (var wrongHost = new HttpRequestMessage(HttpMethod.Get, "/api/health/ready"))
        {
            wrongHost.Headers.Host = "untrusted.invalid";
            using var rejected = await client.SendAsync(wrongHost);
            Check(rejected.StatusCode == HttpStatusCode.Forbidden, "Nonlocal Host was accepted.");
        }
        using (var crossOrigin = new HttpRequestMessage(HttpMethod.Post, "/__fixture__/stop"))
        {
            crossOrigin.Headers.Add("Origin", "http://untrusted.invalid");
            using var rejected = await client.SendAsync(crossOrigin);
            Check(rejected.StatusCode == HttpStatusCode.Forbidden, "Cross-origin stop was accepted.");
        }
        using var stop = await client.PostAsync("/__fixture__/stop", null);
        Check(stop.StatusCode == HttpStatusCode.Accepted, "Graceful stop failed.");
    }

    private static async Task<JsonDocument> JsonAsync(HttpClient client, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request);
        Check(response.StatusCode == HttpStatusCode.OK, "An authenticated preview API operation failed.");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static long Expiration(string token)
    {
        var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
        using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
        return document.RootElement.GetProperty("exp").GetInt64();
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
