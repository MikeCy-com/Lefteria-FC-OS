using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LefteriaFc.Infrastructure.Persistence;
using LefteriaFc.Infrastructure.WebAdmin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed class WebAdminHttpChecks(WebAdminApplicationFactory factory, WebAdminDatabaseFixture database)
{
    private const string LoginPath = "/api/admin/auth/login";
    private const string MePath = "/api/admin/auth/me";
    private const string LogoutPath = "/api/admin/auth/logout";
    private int clientSequence;

    public static async Task RunAsync(string fixturePath, FixtureConfiguration fixture)
    {
        await fixture.VerifyServerAsync();
        var metadata = JsonSerializer.Deserialize<AuthFixtureDocument>(await File.ReadAllTextAsync(fixturePath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("The disposable admin-auth fixture metadata is missing.");
        if (string.IsNullOrWhiteSpace(metadata.AdminAuthUsername) ||
            metadata.AdminAuthUsername == fixture.AdminUsername || metadata.AdminAuthUsername == fixture.AppUsername)
            throw new InvalidOperationException("A third dedicated disposable admin-auth role is required.");
        FixtureConfiguration.ValidateConnection(metadata.AdminAuthConnectionString, metadata.AdminAuthUsername);
        var authSettings = new NpgsqlConnectionStringBuilder(metadata.AdminAuthConnectionString);
        var ownerSettings = new NpgsqlConnectionStringBuilder(fixture.AdminConnectionString);
        var appSettings = new NpgsqlConnectionStringBuilder(fixture.AppConnectionString);
        if (authSettings.Password is not { Length: >= 32 } || authSettings.Password == ownerSettings.Password || authSettings.Password == appSettings.Password)
            throw new InvalidOperationException("The disposable admin-auth credential must be independent.");
        await VerifyAuthServerAsync(metadata.AdminAuthConnectionString!, metadata.AdminAuthUsername);

        var factory = new WebAdminApplicationFactory(fixture, metadata.AdminAuthConnectionString!, metadata.AdminAuthUsername);
        var database = new WebAdminDatabaseFixture(fixture, factory);
        var checks = new WebAdminHttpChecks(factory, database);
        var failures = 0;
        try
        {
            using (var scope = factory.Services.CreateScope())
            {
                FixtureConfiguration.ValidateConnection(scope.ServiceProvider.GetRequiredService<LefteriaDbContext>()
                    .Database.GetConnectionString(), fixture.AppUsername);
                await using var connection = await scope.ServiceProvider.GetRequiredService<WebAdminConnections>().OpenAsync(CancellationToken.None);
                FixtureConfiguration.ValidateConnection(connection.ConnectionString, metadata.AdminAuthUsername);
            }
            foreach (var (name, check) in checks.Cases)
            {
                try { await check(); Console.WriteLine("PASS " + name); }
                catch (Exception)
                {
                    failures++;
                    // Do not print exception bodies: a driver/model-binding error
                    // can contain a SQL parameter, request body or cookie.
                    Console.Error.WriteLine("FAIL " + name);
                }
            }
            Require(factory.Sms.Calls == 0, "The web-admin fixture invoked its SMS sender.");
        }
        finally
        {
            await factory.DisposeAsync();
            await database.CleanupAsync();
        }
        if (failures != 0) throw new InvalidOperationException("Web-admin HTTP/PostgreSQL fixture checks failed.");
    }

    private IReadOnlyList<(string Name, Func<Task> Check)> Cases =>
    [
        ("Web-admin opaque secure cookie and digest-backed 24-hour/30/60/90-day sessions", CookieAndLifetimesAsync),
        ("Web-admin exact credentials, uniform failures and unavailable two-factor fail closed", CredentialFailuresAsync),
        ("Web-admin mutation origins, JSON types, methods and bounded streamed bodies", RequestBoundaryAsync),
        ("Web-admin role permissions and organization union follow current grants", FreshAuthorityAsync),
        ("Web-admin copied cookies lose authority after logout and account/session changes", CookieInvalidationAsync),
        ("Web-admin cookie and mobile bearer realms remain independent", RealmIsolationAsync),
        ("Web-admin durable client and username throttle dimensions are independent", DurableThrottlesAsync)
    ];

    private HttpClient Client()
    {
        var number = ++clientSequence;
        Require(number <= 250, "The web-admin fixture client range is exhausted.");
        database.TrackClient(number);
        var client = factory.CreateClient(new()
        {
            BaseAddress = new Uri(WebAdminApplicationFactory.Origin), AllowAutoRedirect = false, HandleCookies = false
        });
        client.Timeout = TimeSpan.FromSeconds(20);
        client.DefaultRequestHeaders.Add("X-WebAdmin-Test-Client", number.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return client;
    }

    private async Task CookieAndLifetimesAsync()
    {
        // Omission and explicit null both mean 24 hours in the admin realm.
        foreach (var (provided, days) in new (bool, int?)[] { (false, null), (true, null), (true, 30), (true, 60), (true, 90) })
        {
            var account = await database.SeedAsync();
            using var client = Client();
            var before = DateTimeOffset.UtcNow;
            var login = await LoginAsync(client, account, days, provided);
            var after = DateTimeOffset.UtcNow;
            var expiry = login.Body.GetProperty("expires_at").GetDateTimeOffset();
            Require(expiry >= before.AddDays(days ?? 1).AddSeconds(-2) && expiry <= after.AddDays(days ?? 1).AddSeconds(2),
                "The web-admin response expiry differs from the selected lifetime.");
            Require(await database.SessionCountAsync(account) == 1, "A single successful login must persist one session.");
            await database.AssertSessionAsync(account, login.Token, days ?? 1);
            using var me = await GetAsync(client, MePath, login.Cookie);
            var profile = await ExpectJsonAsync(me, 200);
            Require(profile.GetProperty("user").GetProperty("id").GetGuid() == account.Id, "The cookie selected another account.");
            AssertSafeJson(profile);
        }
    }

    private async Task CredentialFailuresAsync()
    {
        var active = await database.SeedAsync();
        var inactive = await database.SeedAsync(active: false);
        var invited = await database.SeedAsync(active: false, invite: true);
        var absent = "WebAdminAbsent-" + Guid.NewGuid().ToString("N");
        string? uniform = null;
        foreach (var (username, password) in new[]
        {
            (active.Username, "incorrect-fictional-password"), (absent, WebAdminDatabaseFixture.Password),
            (inactive.Username, WebAdminDatabaseFixture.Password), (invited.Username, WebAdminDatabaseFixture.Password),
            (active.Username.ToLowerInvariant(), WebAdminDatabaseFixture.Password),
            (" " + active.Username + " ", WebAdminDatabaseFixture.Password)
        })
        {
            using var client = Client();
            using var response = await AttemptAsync(client, username, password);
            var body = await ExpectJsonAsync(response, 401, "credentials_invalid");
            uniform ??= body.GetRawText();
            Require(body.GetRawText() == uniform, "Credential failures must have the same response for every account state.");
            Require(!response.Headers.Contains("Set-Cookie"), "A failed login issued a cookie.");
        }
        Require(await database.SessionCountAsync(active) == 0 && await database.SessionCountAsync(inactive) == 0 &&
            await database.SessionCountAsync(invited) == 0, "Invalid or unavailable credentials created a session.");

        var spaced = await database.SeedAsync(spacedPassword: true);
        using (var client = Client())
        {
            using var trimmed = await AttemptAsync(client, spaced.Username, WebAdminDatabaseFixture.SpacedPassword.Trim());
            await ExpectJsonAsync(trimmed, 401, "credentials_invalid");
            Require(await database.SessionCountAsync(spaced) == 0, "Trimming a password changed its meaning.");
            await LoginAsync(client, spaced, password: WebAdminDatabaseFixture.SpacedPassword);
        }
        var twoFactor = await database.SeedAsync(twoFactor: true);
        using (var client = Client())
        {
            using var response = await AttemptAsync(client, twoFactor.Username, WebAdminDatabaseFixture.Password);
            await ExpectJsonAsync(response, 503, "two_factor_unavailable");
            Require(!response.Headers.Contains("Set-Cookie") && await database.SessionCountAsync(twoFactor) == 0,
                "An unavailable two-factor flow issued authority.");
        }
        Require(factory.Sms.Calls == 0, "Two-factor fallback attempted an SMS call.");
    }

    private async Task RequestBoundaryAsync()
    {
        var account = await database.SeedAsync();
        using var client = Client();
        var body = JsonSerializer.Serialize(new { username = account.Username, password = WebAdminDatabaseFixture.Password });
        foreach (var mode in new[] { "missing-origin", "wrong-origin", "http-origin", "path-origin", "multiple-origins", "missing-custom", "multiple-custom", "cross-site" })
        {
            using var request = Mutation(LoginPath, new StringContent(body, Encoding.UTF8, "application/json"));
            switch (mode)
            {
                case "missing-origin": request.Headers.Remove("Origin"); break;
                case "wrong-origin": request.Headers.Remove("Origin"); request.Headers.Add("Origin", "https://attacker.invalid"); break;
                case "http-origin": request.Headers.Remove("Origin"); request.Headers.Add("Origin", "http://admin-fixture.lefteria.invalid"); break;
                case "path-origin": request.Headers.Remove("Origin"); request.Headers.Add("Origin", WebAdminApplicationFactory.Origin + "/extra"); break;
                case "multiple-origins": request.Headers.TryAddWithoutValidation("Origin", "https://attacker.invalid"); break;
                case "missing-custom": request.Headers.Remove("X-Lefteria-Admin"); break;
                case "multiple-custom": request.Headers.TryAddWithoutValidation("X-Lefteria-Admin", "1"); break;
                case "cross-site": request.Headers.Add("Sec-Fetch-Site", "cross-site"); break;
            }
            using var response = await client.SendAsync(request);
            await ExpectJsonAsync(response, 403, "request_origin_denied");
        }
        using (var request = Mutation(LoginPath, new StringContent(body, Encoding.UTF8, "text/plain")))
        using (var response = await client.SendAsync(request)) await ExpectJsonAsync(response, 415, "json_required");
        foreach (var content in new HttpContent[]
        {
            new StringContent(new string('x', 16 * 1024 + 1), Encoding.UTF8, "application/json"),
            new UnknownLengthJsonContent(Encoding.UTF8.GetBytes(new string('x', 16 * 1024 + 1)))
        })
        {
            using var request = Mutation(LoginPath, content);
            using var response = await client.SendAsync(request);
            await ExpectJsonAsync(response, 413, "request_too_large");
        }
        foreach (var payload in new[]
        {
            "{", JsonSerializer.Serialize(new { username = account.Username, password = WebAdminDatabaseFixture.Password, remember_days = "30" }),
            JsonSerializer.Serialize(new { username = account.Username, password = new string('x', 4097) }),
            JsonSerializer.Serialize(new { username = new string('x', 81), password = WebAdminDatabaseFixture.Password })
        })
        {
            using var request = Mutation(LoginPath, new StringContent(payload, Encoding.UTF8, "application/json"));
            using var response = await client.SendAsync(request);
            await ExpectStatusAsync(response, 400);
        }
        foreach (var days in new[] { 0, 24, 31, 91 })
        {
            using var response = await AttemptAsync(client, account.Username, WebAdminDatabaseFixture.Password, days);
            await ExpectJsonAsync(response, 422, "invalid_remember_days");
        }
        using (var response = await client.GetAsync(LoginPath)) await ExpectStatusAsync(response, 405);
        using (var request = Mutation(LoginPath, JsonContent.Create(new { })))
        {
            request.Method = HttpMethod.Put;
            using var response = await client.SendAsync(request);
            await ExpectStatusAsync(response, 405);
        }
        Require(await database.SessionCountAsync(account) == 0, "A denied request shape created a session.");
    }

    private async Task FreshAuthorityAsync()
    {
        var account = await database.SeedAsync(organizations: ["lefteria_fc", "lefteria_fc_academy"]);
        await database.SetStateAsync(account, activeOrganization: "lefteria_fc");
        using var client = Client();
        var login = await LoginAsync(client, account);
        AssertAuthority(login.Body, "manager", ["lefteria_fc", "lefteria_fc_academy"], "staff:write");
        await database.SetStateAsync(account, role: "editor", activeOrganization: "lefteria_fc");
        using (var response = await GetAsync(client, MePath, login.Cookie))
        {
            var body = await ExpectJsonAsync(response, 200);
            AssertAuthority(body, "editor", ["lefteria_fc", "lefteria_fc_academy"], "news:*");
            Require(!Permissions(body).Contains("staff:write", StringComparer.Ordinal), "A copied session retained the prior role permission.");
        }
        await database.RevokeOrganizationAsync(account, "lefteria_fc");
        using (var response = await GetAsync(client, MePath, login.Cookie))
            AssertAuthority(await ExpectJsonAsync(response, 200), "editor", ["lefteria_fc_academy"], "news:*");
        await database.RevokeOrganizationAsync(account, "lefteria_fc_academy");
        using (var response = await GetAsync(client, MePath, login.Cookie))
            AssertAuthority(await ExpectJsonAsync(response, 200), "editor", [], "news:*");
    }

    private async Task CookieInvalidationAsync()
    {
        var account = await database.SeedAsync();
        using (var client = Client())
        using (var copied = Client())
        {
            var login = await LoginAsync(client, account);
            using (var before = await GetAsync(copied, MePath, login.Cookie)) await ExpectJsonAsync(before, 200);
            using (var deniedRequest = Mutation(LogoutPath, JsonContent.Create(new { })))
            {
                deniedRequest.Headers.Remove("Origin");
                deniedRequest.Headers.Add("Cookie", login.Cookie);
                using var deniedLogout = await client.SendAsync(deniedRequest);
                await ExpectJsonAsync(deniedLogout, 403, "request_origin_denied");
            }
            using (var stillCurrent = await GetAsync(copied, MePath, login.Cookie)) await ExpectJsonAsync(stillCurrent, 200);
            using var request = Mutation(LogoutPath, JsonContent.Create(new { }));
            request.Headers.Add("Cookie", login.Cookie);
            using var logout = await client.SendAsync(request);
            await ExpectStatusAsync(logout, 204);
            Require((await logout.Content.ReadAsByteArrayAsync()).Length == 0, "Logout 204 must not contain a body.");
            Require(logout.Headers.TryGetValues("Set-Cookie", out var cleared) && cleared.Any(value => value.StartsWith(WebAdminApplicationFactory.CookieName + "=;", StringComparison.Ordinal)),
                "Logout did not clear the host cookie.");
            using var after = await GetAsync(copied, MePath, login.Cookie);
            await ExpectJsonAsync(after, 401, "not_authenticated");
        }
        foreach (var change in new[] { "password", "disabled", "invite", "two-factor", "expired" })
        {
            var changed = await database.SeedAsync();
            using var client = Client();
            var login = await LoginAsync(client, changed);
            switch (change)
            {
                case "password": await database.SetStateAsync(changed, passwordHash: WebAdminDatabaseFixture.SpacedPasswordHash); break;
                case "disabled": await database.SetStateAsync(changed, active: false); break;
                case "invite": await database.SetStateAsync(changed, active: false, invite: true); break;
                case "two-factor": await database.SetStateAsync(changed, twoFactor: true); break;
                case "expired": await database.ExpireSessionAsync(changed, login.Token); break;
            }
            using var response = await GetAsync(client, MePath, login.Cookie);
            await ExpectJsonAsync(response, 401, "not_authenticated");
        }
    }

    private async Task RealmIsolationAsync()
    {
        var account = await database.SeedAsync();
        using var client = Client();
        var login = await LoginAsync(client, account);
        using (var response = await GetAsync(client, "/api/mobile/auth/me", login.Cookie)) await ExpectStatusAsync(response, 401);
        using var bearer = Client();
        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "mobile-bearer-cannot-authorize-admin");
        using (var response = await bearer.GetAsync(MePath)) await ExpectJsonAsync(response, 401, "not_authenticated");
        // A stray mobile bearer cannot replace the explicitly selected cookie realm.
        using (var response = await GetAsync(bearer, MePath, login.Cookie)) await ExpectJsonAsync(response, 200);
    }

    private async Task DurableThrottlesAsync()
    {
        using (var sharedClient = Client())
        {
            for (var index = 0; index <= 10; index++)
            {
                var account = await database.SeedAsync();
                using var response = await AttemptAsync(sharedClient, account.Username, "incorrect-fictional-password");
                await ExpectJsonAsync(response, index < 10 ? 401 : 429, index < 10 ? "credentials_invalid" : "login_rate_limited");
                Require(await database.SessionCountAsync(account) == 0, "A client-limited attempt created a session.");
            }
        }
        var sharedUsername = await database.SeedAsync();
        for (var index = 0; index <= 5; index++)
        {
            using var client = Client();
            using var response = await AttemptAsync(client, sharedUsername.Username,
                index < 5 ? "incorrect-fictional-password" : WebAdminDatabaseFixture.Password);
            await ExpectJsonAsync(response, index < 5 ? 401 : 429, index < 5 ? "credentials_invalid" : "login_rate_limited");
        }
        Require(await database.SessionCountAsync(sharedUsername) == 0, "A username-limited correct password bypassed shared throttling.");
    }

    private async Task<Login> LoginAsync(HttpClient client, WebAdminDatabaseFixture.Account account, int? days = null,
        bool provided = true, string password = WebAdminDatabaseFixture.Password)
    {
        using var response = await AttemptAsync(client, account.Username, password, days, provided);
        var body = await ExpectJsonAsync(response, 200);
        Require(response.Headers.TryGetValues("Set-Cookie", out var values), "A successful admin login did not issue a cookie.");
        var cookies = values!.ToArray();
        Require(cookies.Length == 1, "Login issued an unexpected extra cookie.");
        var parts = cookies[0].Split(';', StringSplitOptions.TrimEntries);
        var prefix = WebAdminApplicationFactory.CookieName + "=";
        Require(parts[0].StartsWith(prefix, StringComparison.Ordinal), "Login did not issue the host-prefixed admin cookie.");
        var token = parts[0][prefix.Length..];
        Require(Regex.IsMatch(token, "^[A-Za-z0-9_-]{43}$", RegexOptions.CultureInvariant), "The admin cookie must contain only an opaque 256-bit token.");
        Require(parts.Contains("secure", StringComparer.OrdinalIgnoreCase) && parts.Contains("httponly", StringComparer.OrdinalIgnoreCase) &&
            parts.Contains("samesite=lax", StringComparer.OrdinalIgnoreCase) && parts.Contains("path=/", StringComparer.OrdinalIgnoreCase) &&
            parts.Any(part => part.StartsWith("expires=", StringComparison.OrdinalIgnoreCase)) &&
            parts.Any(part => part.StartsWith("max-age=", StringComparison.OrdinalIgnoreCase)) &&
            !parts.Any(part => part.StartsWith("domain=", StringComparison.OrdinalIgnoreCase)), "The admin cookie attributes are incomplete.");
        Require(!body.GetRawText().Contains(token, StringComparison.Ordinal), "A cookie token appeared in a JSON response.");
        return new(body, token, parts[0]);
    }

    private async Task<HttpResponseMessage> AttemptAsync(HttpClient client, string username, string password, int? days = null, bool provided = true)
    {
        database.TrackUsername(username);
        var payload = new Dictionary<string, object?> { ["username"] = username, ["password"] = password };
        if (provided) payload["remember_days"] = days;
        using var request = Mutation(LoginPath, JsonContent.Create(payload));
        return await client.SendAsync(request);
    }

    private static HttpRequestMessage Mutation(string path, HttpContent content)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        request.Headers.Add("Origin", WebAdminApplicationFactory.Origin);
        request.Headers.Add("X-Lefteria-Admin", "1");
        return request;
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> ExpectJsonAsync(HttpResponseMessage response, int status, string? code = null)
    {
        await ExpectStatusAsync(response, status);
        using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var body = document.RootElement.Clone();
        if (code is not null) Require(body.TryGetProperty("code", out var error) && error.GetString() == code, "The admin response fault code differs.");
        AssertSafeJson(body);
        if (status >= 400) Require(!response.Headers.Contains("Set-Cookie"), "A refused request issued a session cookie.");
        return body;
    }

    private static async Task ExpectStatusAsync(HttpResponseMessage response, int status)
    {
        Require((int)response.StatusCode == status, "The web-admin HTTP status differs from the expected boundary.");
        if (response.RequestMessage?.RequestUri?.AbsolutePath.StartsWith("/api/admin", StringComparison.Ordinal) == true)
            Require(response.Headers.CacheControl?.NoStore == true, "Admin responses must not be stored in caches.");
        var content = await response.Content.ReadAsStringAsync();
        Require(!content.Contains(WebAdminDatabaseFixture.Password, StringComparison.Ordinal) &&
            !content.Contains(WebAdminDatabaseFixture.PasswordHash, StringComparison.Ordinal) &&
            !content.Contains(WebAdminDatabaseFixture.SpacedPassword, StringComparison.Ordinal) &&
            !content.Contains(WebAdminDatabaseFixture.SpacedPasswordHash, StringComparison.Ordinal) &&
            !content.Contains("incorrect-fictional-password", StringComparison.Ordinal), "An HTTP response echoed a supplied credential.");
    }

    private static void AssertSafeJson(JsonElement body)
    {
        if (body.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in body.EnumerateObject())
            {
                Require(property.Name is not ("token" or "access_token" or "refresh_token" or "password" or "password_hash" or
                    "phone" or "normalized_phone" or "credential_digest" or "token_digest" or "otp"), "The admin JSON projection includes authentication/private evidence.");
                AssertSafeJson(property.Value);
            }
        }
        else if (body.ValueKind == JsonValueKind.Array)
            foreach (var item in body.EnumerateArray()) AssertSafeJson(item);
        else if (body.ValueKind == JsonValueKind.String)
        {
            var value = body.GetString()!;
            Require(!value.Contains(WebAdminDatabaseFixture.Password, StringComparison.Ordinal) &&
                !value.Contains(WebAdminDatabaseFixture.PasswordHash, StringComparison.Ordinal) &&
                !value.Contains(WebAdminDatabaseFixture.SpacedPassword, StringComparison.Ordinal) &&
                !value.Contains(WebAdminDatabaseFixture.SpacedPasswordHash, StringComparison.Ordinal) &&
                !value.Contains("incorrect-fictional-password", StringComparison.Ordinal), "An admin response echoed a supplied credential.");
        }
    }

    private static string[] Permissions(JsonElement body) => body.GetProperty("user").GetProperty("permissions").EnumerateArray().Select(value => value.GetString()!).ToArray();

    private static void AssertAuthority(JsonElement body, string role, string[] organizations, string expectedPermission)
    {
        var user = body.GetProperty("user");
        Require(user.GetProperty("role").GetString() == role && Permissions(body).Contains(expectedPermission, StringComparer.Ordinal), "The current admin role permissions differ.");
        var actual = user.GetProperty("organizations").EnumerateArray().Select(value => value.GetProperty("id").GetString()!).Order(StringComparer.Ordinal);
        Require(actual.SequenceEqual(organizations.Order(StringComparer.Ordinal), StringComparer.Ordinal), "The admin profile does not reflect the union of current organization grants.");
    }

    private static async Task VerifyAuthServerAsync(string connectionString, string username)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT current_database()=@database AND host(inet_server_addr())='127.0.0.1' AND inet_server_port()=@port
              AND current_user=@username AND r.rolcanlogin AND NOT r.rolinherit
              AND NOT (r.rolsuper OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication OR r.rolbypassrls)
              AND NOT EXISTS (SELECT 1 FROM pg_auth_members WHERE member=r.oid OR roleid=r.oid)
              AND NOT EXISTS (SELECT 1 FROM pg_database WHERE datdba=r.oid)
              AND NOT EXISTS (SELECT 1 FROM pg_namespace WHERE nspowner=r.oid)
            FROM pg_roles r WHERE rolname=current_user
            """, connection);
        command.Parameters.AddWithValue("database", FixtureConfiguration.ExpectedDatabase);
        command.Parameters.AddWithValue("port", FixtureConfiguration.ExpectedPort); command.Parameters.AddWithValue("username", username);
        Require(await command.ExecuteScalarAsync() is true, "The admin-auth connection did not reach the exact unprivileged disposable role.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class AuthFixtureDocument
    {
        public AuthFixtureDocument() { }
        public string? AdminAuthUsername { get; init; }
        public string? AdminAuthConnectionString { get; init; }
    }

    private sealed class Login(JsonElement body, string token, string cookie)
    {
        public JsonElement Body { get; } = body;
        public string Token { get; } = token;
        public string Cookie { get; } = cookie;
    }

    private sealed class UnknownLengthJsonContent : HttpContent
    {
        private readonly byte[] bytes;
        public UnknownLengthJsonContent(byte[] bytes)
        {
            this.bytes = bytes;
            Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    }
}
