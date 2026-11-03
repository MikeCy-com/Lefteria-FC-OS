using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Identity.IntegrationTests;

namespace LefteriaFc.MobilePreview;

internal static class WebPlatformPreviewSmoke
{
    private enum Phase { Health, Anonymous, LoginBoundary, Login, Session, ClubRead, ClubBoundary, AuthBodyLimit, ClubBodyLimit,
        ClubSave, PublicRead, ClubIsolation, ClubConflict, ClubRoutes, ClubRevocation, Logout, HostBoundary, Shutdown }
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private const string FcPath = "/api/admin/club?organization_id=lefteria_fc";
    private const string AcademyPath = "/api/admin/club?organization_id=lefteria_fc_academy";

    public static async Task RunAsync(WebPlatformPreviewOptions options, WebAdminDatabaseFixture.Account account, WebAdminDatabaseFixture database)
    {
        var phase = Phase.Health;
        try { await ScenarioAsync(options, account, database, value => phase = value); }
        catch (PreviewSmokeException) { throw; }
        catch (HttpRequestException error) { throw new PreviewSmokeException($"Web-platform preview HTTP transport failed during {phase}: {error.HttpRequestError}, {TransportKind(error.InnerException)}."); }
        catch (JsonException) { throw new PreviewSmokeException($"Web-platform preview JSON contract failed during {phase}."); }
        catch (Exception) { throw new PreviewSmokeException($"Unexpected web-platform preview smoke transport or response contract during {phase}."); }
    }

    private static async Task ScenarioAsync(WebPlatformPreviewOptions options, WebAdminDatabaseFixture.Account account, WebAdminDatabaseFixture database, Action<Phase> phase)
    {
        var username = account.Username;
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false, UseCookies = false, AllowAutoRedirect = false })
        { BaseAddress = options.Origin, Timeout = TimeSpan.FromSeconds(15) };
        using (var health = await client.GetAsync("/api/health/ready")) Expect(health, HttpStatusCode.OK, "Web-platform readiness failed.");
        phase(Phase.Anonymous);
        using (var anonymous = await client.GetAsync("/api/admin/auth/me")) Expect(anonymous, HttpStatusCode.Unauthorized, "Anonymous admin boundary failed.");
        phase(Phase.LoginBoundary);
        foreach (var origin in new string?[] { null, "https://attacker.invalid" })
        {
            using var denied = Login(username, origin, marker: true);
            using var response = await client.SendAsync(denied);
            Expect(response, HttpStatusCode.Forbidden, "The preview manufactured a trusted admin origin.");
        }
        using (var denied = Login(username, options.Origin.GetLeftPart(UriPartial.Authority), marker: false))
        using (var response = await client.SendAsync(denied)) Expect(response, HttpStatusCode.Forbidden, "The API custom-header gate was bypassed.");
        string cookie;
        phase(Phase.Login);
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
        phase(Phase.Session);
        using (var me = new HttpRequestMessage(HttpMethod.Get, "/api/admin/auth/me"))
        {
            me.Headers.Add("Cookie", cookie);
            using var response = await client.SendAsync(me);
            Expect(response, HttpStatusCode.OK, "Forwarded cookie authentication failed.");
        }
        await ClubScenarioAsync(client, options, cookie, account, database, phase);
        phase(Phase.Logout);
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
        phase(Phase.HostBoundary);
        using (var mobile = await client.GetAsync("/api/mobile/auth/me")) Expect(mobile, HttpStatusCode.NotFound, "Unapproved mobile API was exposed.");
        using (var wrongHost = new HttpRequestMessage(HttpMethod.Get, "/api/health/live"))
        {
            wrongHost.Headers.Host = "attacker.invalid:" + options.Port;
            using var response = await client.SendAsync(wrongHost);
            Expect(response, HttpStatusCode.Forbidden, "Nonlocal Host was accepted.");
        }
        phase(Phase.Shutdown);
        using var stop = new HttpRequestMessage(HttpMethod.Post, "/__fixture__/stop");
        stop.Headers.Add("Origin", options.Origin.GetLeftPart(UriPartial.Authority)); stop.Headers.Add("X-Lefteria-Preview", "stop");
        using var stopped = await client.SendAsync(stop);
        Expect(stopped, HttpStatusCode.Accepted, "Guarded fixture stop failed.");
    }

    private static async Task ClubScenarioAsync(HttpClient client, WebPlatformPreviewOptions options, string cookie,
        WebAdminDatabaseFixture.Account account, WebAdminDatabaseFixture database, Action<Phase> phase)
    {
        phase(Phase.ClubRead);
        using (var anonymous = await client.GetAsync(FcPath)) Expect(anonymous, HttpStatusCode.Unauthorized, "Anonymous club access was accepted.");
        var fc = await ReadClubAsync(client, FcPath, cookie);
        var academy = await ReadClubAsync(client, AcademyPath, cookie);
        Check(fc.Version == 0 && fc.Club.Name == WebPlatformPreviewClubFixture.FcName &&
            fc.Club.OrganizationId == "lefteria_fc" && academy.Version == 0 &&
            academy.Club.Name == WebPlatformPreviewClubFixture.AcademyName && academy.Club.OrganizationId == "lefteria_fc_academy",
            "The initial fictional club profiles differed.");
        var fields = new AdminClubEditable("Fictional saved browser FC", "Δοκιμαστικός σύλλογος", 2024,
            "Fictional browser description", "Fictional motto", new string('λ', 100000), null, null,
            "Λεμεσός", "Κύπρος", "Fictional address", "saved@example.invalid", null, "https://example.invalid",
            new Dictionary<string, string> { ["instagram"] = "https://example.invalid/club" });
        var command = new AdminClubUpdateCommand(0, fields);
        var origin = options.Origin.GetLeftPart(UriPartial.Authority);
        phase(Phase.ClubBoundary);
        foreach (var deniedOrigin in new string?[] { null, "https://attacker.invalid" })
        {
            using var request = ClubMutation(cookie, command, deniedOrigin);
            using var response = await client.SendAsync(request);
            Expect(response, HttpStatusCode.Forbidden, "The club proxy manufactured a trusted Origin.");
        }
        using (var request = ClubMutation(cookie, command, origin, marker: false))
        using (var response = await client.SendAsync(request)) Expect(response, HttpStatusCode.Forbidden, "The club custom-header gate was bypassed.");
        phase(Phase.AuthBodyLimit);
        using (var request = Login(account.Username, origin, marker: true))
        {
            request.Content = new StringContent(new string('x', 17 * 1024), Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request);
            Expect(response, HttpStatusCode.RequestEntityTooLarge, "The auth body limit increased with the club limit.");
        }
        phase(Phase.ClubBodyLimit);
        using (var request = ClubMutation(cookie, command, origin))
        {
            // Kestrel may reset a connection while a client is still sending
            // an oversized chunked body. Use the standard early-response
            // handshake to assert the explicit HTTP limit without that race.
            // The accepted full-size JsonContent save below is unknown-length.
            request.Content = new ByteArrayContent(new byte[1024 * 1024 + 1]);
            request.Headers.ExpectContinue = true;
            using var response = await client.SendAsync(request);
            Expect(response, HttpStatusCode.RequestEntityTooLarge, "The club body exceeded its preview limit.");
        }
        phase(Phase.ClubSave);
        using (var request = ClubMutation(cookie, command, origin))
        using (var response = await client.SendAsync(request))
        {
            Expect(response, HttpStatusCode.OK, "A full-size Greek club edit failed through the preview.");
            var saved = await response.Content.ReadFromJsonAsync<AdminClubProfileDto>(Json);
            Check(saved is { Version: 1 } && saved.Club.Id == fc.Club.Id && saved.Club.Name == fields.Name && saved.Club.History?.Length == 100000,
                "The confirmed club save projection differed.");
        }
        phase(Phase.PublicRead);
        using (var publicSite = await client.GetAsync("/api/public/site?organization_id=lefteria_fc"))
        {
            Expect(publicSite, HttpStatusCode.OK, "The edited public club was unavailable.");
            var site = await publicSite.Content.ReadFromJsonAsync<PublicSiteResult>(Json);
            Check(site?.Club?.Name == fields.Name && site.Club.History?.Length == 100000, "The public club did not immediately reflect the save.");
        }
        phase(Phase.ClubIsolation);
        var unchanged = await ReadClubAsync(client, AcademyPath, cookie);
        Check(unchanged.Version == 0 && unchanged.Club.Name == academy.Club.Name && unchanged.Club.Id == academy.Club.Id,
            "The FC edit changed Academy club data.");
        phase(Phase.ClubConflict);
        using (var stale = ClubMutation(cookie, command, origin))
        using (var response = await client.SendAsync(stale)) Expect(response, HttpStatusCode.Conflict, "A stale club version overwrote a saved edit.");
        phase(Phase.ClubRoutes);
        using (var wrongMethod = new HttpRequestMessage(HttpMethod.Patch, FcPath))
        using (var response = await client.SendAsync(wrongMethod)) Expect(response, HttpStatusCode.NotFound, "An unapproved club method was exposed.");
        using (var unknownRoute = await client.GetAsync("/api/admin/club/unknown")) Expect(unknownRoute, HttpStatusCode.NotFound, "An unapproved admin club route was exposed.");
        phase(Phase.ClubRevocation);
        await database.RevokeOrganizationAsync(account, "lefteria_fc_academy");
        using (var denied = new HttpRequestMessage(HttpMethod.Get, AcademyPath))
        {
            denied.Headers.Add("Cookie", cookie);
            using var response = await client.SendAsync(denied);
            Expect(response, HttpStatusCode.Forbidden, "A revoked club organization remained accessible.");
        }
        Check((await ReadClubAsync(client, FcPath, cookie)).Version == 1, "Revoking Academy changed the assigned FC club.");
    }

    private static async Task<AdminClubProfileDto> ReadClubAsync(HttpClient client, string path, string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path); request.Headers.Add("Cookie", cookie);
        using var response = await client.SendAsync(request);
        Expect(response, HttpStatusCode.OK, "A cookie-authenticated club read failed.");
        return await response.Content.ReadFromJsonAsync<AdminClubProfileDto>(Json) ?? throw new PreviewSmokeException("The club response was missing.");
    }

    private static HttpRequestMessage ClubMutation(string cookie, AdminClubUpdateCommand command, string? origin, bool marker = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, FcPath) { Content = JsonContent.Create(command, options: Json) };
        request.Headers.Add("Cookie", cookie);
        if (origin is not null) request.Headers.Add("Origin", origin);
        if (marker) request.Headers.Add("X-Lefteria-Admin", "1");
        return request;
    }

    private static string TransportKind(Exception? error) => error switch
    {
        IOException { InnerException: System.Net.Sockets.SocketException socket } => "socket:" + socket.SocketErrorCode,
        IOException => "io", InvalidOperationException => "invalid_operation", OperationCanceledException => "cancelled", _ => "other"
    };

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
