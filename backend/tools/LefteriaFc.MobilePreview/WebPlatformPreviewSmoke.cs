using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LefteriaFc.Identity.IntegrationTests;

namespace LefteriaFc.MobilePreview;

internal static class WebPlatformPreviewSmoke
{
    public static async Task RunAsync(WebPlatformPreviewOptions options, string username)
    {
        try { await ScenarioAsync(options, username); }
        catch (PreviewSmokeException) { throw; }
        catch (Exception) { throw new PreviewSmokeException("Unexpected web-platform preview smoke transport or response contract."); }
    }

    private static async Task ScenarioAsync(WebPlatformPreviewOptions options, string username)
    {
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false, UseCookies = false, AllowAutoRedirect = false })
        { BaseAddress = options.Origin, Timeout = TimeSpan.FromSeconds(15) };
        using (var health = await client.GetAsync("/api/health/ready")) Expect(health, HttpStatusCode.OK, "Web-platform readiness failed.");
        using (var anonymous = await client.GetAsync("/api/admin/auth/me")) Expect(anonymous, HttpStatusCode.Unauthorized, "Anonymous admin boundary failed.");
        foreach (var origin in new string?[] { null, "https://attacker.invalid" })
        {
            using var denied = Login(username, origin, marker: true);
            using var response = await client.SendAsync(denied);
            Expect(response, HttpStatusCode.Forbidden, "The preview manufactured a trusted admin origin.");
        }
        using (var denied = Login(username, options.Origin.GetLeftPart(UriPartial.Authority), marker: false))
        using (var response = await client.SendAsync(denied)) Expect(response, HttpStatusCode.Forbidden, "The API custom-header gate was bypassed.");
        string cookie;
        using (var login = Login(username, options.Origin.GetLeftPart(UriPartial.Authority), marker: true))
        using (var response = await client.SendAsync(login))
        {
            Expect(response, HttpStatusCode.OK, "Real BCrypt admin login failed.");
            var cookies = response.Headers.GetValues("Set-Cookie").ToArray();
            Check(cookies.Length == 1 && cookies[0].StartsWith(WebAdminApplicationFactory.CookieName + "=", StringComparison.Ordinal) &&
                cookies[0].Contains("secure", StringComparison.OrdinalIgnoreCase) && cookies[0].Contains("httponly", StringComparison.OrdinalIgnoreCase),
                "The preview weakened or omitted the admin cookie.");
            cookie = cookies[0].Split(';')[0];
            using var body = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
            var user = body.RootElement.GetProperty("user");
            Check(user.GetProperty("username").GetString() == username && user.GetProperty("role").GetString() == "manager" &&
                user.GetProperty("organizations").GetArrayLength() == 2 && !body.RootElement.TryGetProperty("token", out _), "Admin session projection differed.");
        }
        using (var me = new HttpRequestMessage(HttpMethod.Get, "/api/admin/auth/me"))
        {
            me.Headers.Add("Cookie", cookie);
            using var response = await client.SendAsync(me);
            Expect(response, HttpStatusCode.OK, "Forwarded cookie authentication failed.");
        }
        using (var wrongOrigin = new HttpRequestMessage(HttpMethod.Post, "/api/admin/auth/logout"))
        {
            wrongOrigin.Headers.Add("Cookie", cookie); wrongOrigin.Headers.Add("Origin", "https://attacker.invalid");
            wrongOrigin.Headers.Add("X-Lefteria-Admin", "1"); wrongOrigin.Content = JsonContent.Create(new { });
            using var response = await client.SendAsync(wrongOrigin);
            Expect(response, HttpStatusCode.Forbidden, "Cross-origin logout was accepted.");
        }
        using (var logout = new HttpRequestMessage(HttpMethod.Post, "/api/admin/auth/logout"))
        {
            logout.Headers.Add("Cookie", cookie); logout.Headers.Add("Origin", options.Origin.GetLeftPart(UriPartial.Authority));
            logout.Headers.Add("X-Lefteria-Admin", "1"); logout.Content = JsonContent.Create(new { });
            using var response = await client.SendAsync(logout);
            Expect(response, HttpStatusCode.NoContent, "Cookie logout failed.");
        }
        using (var copied = new HttpRequestMessage(HttpMethod.Get, "/api/admin/auth/me"))
        {
            copied.Headers.Add("Cookie", cookie);
            using var response = await client.SendAsync(copied);
            Expect(response, HttpStatusCode.Unauthorized, "A copied cookie survived logout.");
        }
        using (var mobile = await client.GetAsync("/api/mobile/auth/me")) Expect(mobile, HttpStatusCode.NotFound, "Unapproved mobile API was exposed.");
        using (var wrongHost = new HttpRequestMessage(HttpMethod.Get, "/api/health/live"))
        {
            wrongHost.Headers.Host = "attacker.invalid:" + options.Port;
            using var response = await client.SendAsync(wrongHost);
            Expect(response, HttpStatusCode.Forbidden, "Nonlocal Host was accepted.");
        }
        using var stop = new HttpRequestMessage(HttpMethod.Post, "/__fixture__/stop");
        stop.Headers.Add("Origin", options.Origin.GetLeftPart(UriPartial.Authority)); stop.Headers.Add("X-Lefteria-Preview", "stop");
        using var stopped = await client.SendAsync(stop);
        Expect(stopped, HttpStatusCode.Accepted, "Guarded fixture stop failed.");
    }

    private static HttpRequestMessage Login(string username, string? origin, bool marker)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/auth/login")
        { Content = JsonContent.Create(new { username, password = WebAdminDatabaseFixture.Password, remember_days = (int?)null }) };
        if (origin is not null) request.Headers.Add("Origin", origin);
        if (marker) request.Headers.Add("X-Lefteria-Admin", "1");
        return request;
    }
    private static void Expect(HttpResponseMessage response, HttpStatusCode status, string check)
    { if (response.StatusCode != status) throw new PreviewSmokeException(check, response.StatusCode); }
    private static void Check(bool condition, string check) { if (!condition) throw new PreviewSmokeException(check); }
}
