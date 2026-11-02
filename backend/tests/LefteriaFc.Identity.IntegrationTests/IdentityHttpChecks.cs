using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Domain.Organizations;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks(IdentityApplicationFactory factory, TestDatabase database)
{
    private int clientSequence;

    public IReadOnlyList<(string Name, Func<Task> Check)> Cases =>
    [
        ("Public website import allowlists exclude private settings and source records", PublicContentPolicyAsync),
        ("Public website reads use separate initialized projections and explicit organization scope", PublicContentEndToEndAsync),
        ("Versioned public sections preserve youth privacy, immutable evidence and scoped reads", PublicSectionsEndToEndAsync),
        ("Scoped calendar import, role visibility and atomic own-player RSVP use current canonical authority", CalendarEndToEndAsync),
        ("Owner account import, canonical role revocation and legacy fan OTP preserve source authority", AccountImportEndToEndAsync),
        ("Bounded legacy token validation preserves source claim families and rejects unapproved JWTs", LegacyMobileTokenValidationAsync),
        ("Legacy bridge startup enforces independent keys and disabled defaults", LegacyBridgeConfigurationAsync),
        ("Calendar import preserves private sources, scope references, publication and date semantics", CalendarImportPlannerAsync),
        ("Twilio adapter encodes provider request and role sender fallback without network", TwilioAdapterChecks.ConfiguredRequestAsync),
        ("Unconfigured Twilio adapter refuses delivery without HTTP", TwilioAdapterChecks.UnconfiguredDoesNotCallHttpAsync),
        ("Restricted application role has no schema or audit mutation privilege", database.AssertRestrictedApplicationRoleAsync),
        ("Real HTTP health, authentication boundary and no-store headers", HttpBoundaryAsync),
        ("Unknown verified phone consumes its proof before 404", UnknownPhoneAsync),
        ("Role picker has no token; neutral fan and exact-expiry role switch", RolePickerAndSwitchAsync),
        ("Revoked formal role exposes no links and can recover only active fan", RevocationAsync),
        ("Fan registration changes remain staged until OTP verification", FanStagingAsync),
        ("Own profile preference validates input, binds current identity and isolates roles", ProfilePreferenceAsync),
        ("Profile details allowlist preserves ownership and appends a bound audit", ProfileDetailsAllowlistAsync),
        ("Profile details merge names, ignore nulls and replace boolean preferences", ProfileDetailsMergeAsync),
        ("Profile details reject invalid input atomically with bounded bodies", ProfileDetailsValidationAsync),
        ("Profile details support all active roles and deny mismatched or revoked sessions", ProfileDetailsRoleBindingAsync),
        ("Avatar upload publishes sanitized JPEG with safe public GET/HEAD and bounded replacement", AvatarDeliveryAsync),
        ("Avatar codec validates types/dimensions/truncation and strips metadata/orientation", AvatarValidationAsync),
        ("Avatar current identity binding isolates roles and rejects revocation during decode", AvatarAuthorizationAsync),
        ("Avatar storage failure rolls back image, profile timestamp and audit atomically", AvatarRollbackAsync),
        ("Roster manifest validation is strict, bounded and private; CLI dry-run has no DB dependency", RosterValidationAsync),
        ("Staff manifest validates global assignments and preserves raw contact presence without DB access", StaffValidationAsync),
        ("Account planning preserves role subjects, quarantine, canonical links and explicit grants", AccountPlannerAsync),
        ("Account planning indexes full-source ambiguity and preserves strict source boundaries", AccountPlannerAdditionalAsync),
        ("Staff readiness distinguishes missing, verified empty and dangling group coach snapshots", StaffEmptySnapshotAsync),
        ("Roster initial import readiness, atomic failure, idempotency and concurrency are enforced", RosterImportAsync),
        ("Authenticated player picker projects only current active first-team players", RosterPickerAsync),
        ("Roster composite organization FKs and runtime archive/write denials hold", RosterIntegrityAsync),
        ("Staff snapshot binds roster provenance, rolls back failures and replays concurrently", StaffImportAsync),
        ("Staff relationships preserve cross-org assignments and private contacts without identity grants", StaffIntegrityAsync),
        ("Formal role signup and malformed registration are rejected", RegistrationValidationAsync),
        ("Remember-days HTTP presence and strict JSON types", RememberDaysAsync),
        ("Expired OTP and sixth verification cannot create a session", ExpiryAndAttemptsAsync),
        ("Concurrent OTP consumption creates exactly one login", ConcurrentConsumptionAsync),
        ("Invalid selected role spends a call without consuming valid proof", InvalidRoleAsync),
        ("Management organization selection, revocation and durable sole context", OrganizationScopesAsync),
        ("PostgreSQL rejects invalid grants, identity links and OTP states", DatabaseConstraintsAsync),
        ("Shared request/verify throttles and destination limit survive concurrency", ThrottlesAsync),
        ("Older failed SMS delivery cannot delete a newer challenge", DeliveryFailureRaceAsync),
        ("JWT signature, issuer, audience, type, role and expiry are enforced", JwtValidationAsync)
    ];

    private HttpClient Client()
    {
        var number = ++clientSequence;
        if (number > 250) throw new InvalidOperationException("The test client address range is exhausted.");
        database.TrackClient(number);
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.Timeout = TimeSpan.FromSeconds(20);
        client.DefaultRequestHeaders.Add("X-Identity-Test-Client", number.ToString());
        return client;
    }

    private async Task HttpBoundaryAsync()
    {
        using var client = Client();
        using var ready = await client.GetAsync("/api/health/ready");
        await ExpectAsync(ready, 200);
        using var me = await client.GetAsync("/api/mobile/auth/me");
        await ExpectAsync(me, 401);
        Check.True(me.Headers.CacheControl?.NoStore == true, "Mobile auth responses must be no-store.");
    }

    private async Task UnknownPhoneAsync()
    {
        using var client = Client();
        var phone = database.NewPhone();
        var request = await RequestAsync(client, phone);
        Check.True(request.GetProperty("role_detected").ValueKind == JsonValueKind.Null,
            "Unknown phones must not acquire a detected role.");
        var stored = await database.ChallengeAsync(phone) ?? throw new InvalidOperationException("OTP was not persisted.");
        Check.Equal(32, stored.CodeDigest.Length, "Only a full keyed digest may be stored.");
        Check.True(!stored.CodeDigest.SequenceEqual(System.Text.Encoding.UTF8.GetBytes(factory.Sms.Latest(phone))),
            "A raw OTP must never be stored.");
        var result = await VerifyAsync(client, phone, factory.Sms.Latest(phone), 404);
        Check.Equal("account_not_found", result.GetProperty("code").GetString(), "Unknown account fault differs.");
        Check.True((await database.ChallengeAsync(phone))?.IsConsumed == true, "Unknown valid proof was not consumed.");
        await VerifyAsync(client, phone, factory.Sms.Latest(phone), 400);
    }

    private async Task RolePickerAndSwitchAsync()
    {
        using var client = Client();
        var phone = database.NewPhone();
        await database.SeedIdentityAsync(phone, MobileRole.Parent, "Formal guardian private display");
        await RequestAsync(client, phone);
        var code = factory.Sms.Latest(phone);
        var picker = await VerifyAsync(client, phone, code, 200);
        Check.True(picker.GetProperty("multi_role").GetBoolean(), "A formal identity plus fan must require a role choice.");
        Check.True(!picker.TryGetProperty("token", out _), "The role picker must not issue a token.");
        Check.Equal(2, picker.GetProperty("identities").GetArrayLength(), "The role picker lost a role.");
        var staged = await database.ChallengeAsync(phone) ?? throw new InvalidOperationException("Role-picker challenge disappeared.");
        Check.Equal(1, staged.VerificationCalls, "The role-picker call must count once.");
        Check.True(!staged.IsConsumed, "Role picker consumed the OTP.");
        var fan = await database.IdentityAsync(phone, MobileRole.Fan) ?? throw new InvalidOperationException("Verified fan fallback was not created.");
        Check.Equal(MobileAuthService.NeutralFanDisplayName, fan.DisplayName, "Formal private name leaked into the neutral public fan identity.");
        var login = await VerifyAsync(client, phone, code, 200, "parent", true, null);
        var token = login.GetProperty("token").GetString()!;
        var claims = Claims(token);
        CheckDuration(claims, TimeSpan.FromDays(1));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var switched = await PostAsync(client, "/api/mobile/auth/switch-role", new { role = "fan" }, 200);
        var switchedClaims = Claims(switched.GetProperty("token").GetString()!);
        Check.Equal(claims.GetProperty("exp").GetInt64(), switchedClaims.GetProperty("exp").GetInt64(), "Role switch extended login expiry.");
        Check.Equal(claims.GetProperty("sid").GetString(), switchedClaims.GetProperty("sid").GetString(), "Role switch changed device session identity.");
        client.DefaultRequestHeaders.Authorization = new("Bearer", switched.GetProperty("token").GetString());
        var noop = await PostAsync(client, "/api/mobile/auth/switch-role", new { role = "fan" }, 200);
        Check.True(noop.GetProperty("noop").GetBoolean() && noop.GetProperty("token").ValueKind == JsonValueKind.Null,
            "Same-role response must be an explicit token:null no-op.");
        await VerifyAsync(client, phone, code, 400, "parent");
    }

    private async Task RevocationAsync()
    {
        using var client = Client();
        var phone = database.NewPhone();
        var parentId = await database.SeedIdentityAsync(phone, MobileRole.Parent);
        await database.SeedIdentityAsync(phone, MobileRole.Coach);
        await database.SeedIdentityAsync(phone, MobileRole.Fan, "Existing independent fan");
        var login = await LoginAsync(client, phone, "parent");
        await database.SetIdentityActiveAsync(parentId, false);
        var me = await GetAsync(client, "/api/mobile/auth/me", 200);
        Check.True(!me.GetProperty("current_role_available").GetBoolean(), "Revoked identity still reports operational availability.");
        Check.Equal(0, me.GetProperty("linked_player_ids").GetArrayLength(), "Revoked parent exposes operational links.");
        await PostAsync(client, "/api/mobile/auth/switch-role", new { role = "coach" }, 403);
        var fan = await PostAsync(client, "/api/mobile/auth/switch-role", new { role = "fan" }, 200);
        Check.Equal("fan", fan.GetProperty("role").GetString(), "Revoked formal role could not recover its fan identity.");
        Check.Equal(Claims(login.GetProperty("token").GetString()!).GetProperty("exp").GetInt64(),
            Claims(fan.GetProperty("token").GetString()!).GetProperty("exp").GetInt64(), "Recovery renewed the device expiry.");
    }

    private async Task FanStagingAsync()
    {
        using var client = Client();
        var phone = database.NewPhone();
        await RegisterAsync(client, phone, "Fixture", "Supporter", "1995-04-02", 200);
        Check.True(await database.IdentityAsync(phone, MobileRole.Fan) is null, "Unverified registration created a fan identity.");
        var challenge = await database.ChallengeAsync(phone) ?? throw new InvalidOperationException("Pending signup was not stored.");
        Check.Equal("Fixture", challenge.PendingFirstName, "Pending signup data was lost.");
        var code = factory.Sms.Latest(phone);
        await VerifyAsync(client, phone, Wrong(code), 400, "fan");
        Check.True(await database.IdentityAsync(phone, MobileRole.Fan) is null, "Incorrect proof applied registration fields.");
        await VerifyAsync(client, phone, code, 200, "fan", true, null);
        var fan = await database.IdentityAsync(phone, MobileRole.Fan) ?? throw new InvalidOperationException("Verified registration did not create a fan.");
        Check.Equal("Fixture Supporter", fan.DisplayName, "Verified fan name was not saved.");
        Check.Equal(new DateOnly(1995, 4, 2), fan.Birthday!.Value, "Verified birthday differs.");
        await RegisterAsync(client, phone, "Changed", "Supporter", "1996-05-03", 200);
        Check.Equal("Fixture Supporter", (await database.IdentityAsync(phone, MobileRole.Fan))!.DisplayName,
            "A signup request modified an existing fan before OTP verification.");
        await VerifyAsync(client, phone, factory.Sms.Latest(phone), 200, "fan");
        Check.Equal("Changed Supporter", (await database.IdentityAsync(phone, MobileRole.Fan))!.DisplayName,
            "Verified staged profile update was not committed.");
    }

    private async Task RegistrationValidationAsync()
    {
        using var client = Client();
        var formal = database.NewPhone();
        await database.SeedIdentityAsync(formal, MobileRole.Coach);
        await RegisterAsync(client, formal, "Fixture", "Coach", "1990-01-01", 400);
        Check.Equal(0, factory.Sms.Count(formal), "Formal-role signup sent an unnecessary SMS.");
        var phone = database.NewPhone();
        await RegisterAsync(client, phone, "", "Supporter", "1990-01-01", 400);
        await RegisterAsync(client, phone, "Fixture", "Supporter", "02/04/1995", 400);
        await RegisterAsync(client, phone, "Fixture", "Supporter", "2999-01-01", 400);
        Check.True(await database.ChallengeAsync(phone) is null, "Invalid registration persisted a challenge.");
        await PostAsync(client, "/api/mobile/auth/request-otp", new { phone = "not-a-phone" }, 400);
    }

    private async Task RememberDaysAsync()
    {
        using var client = Client();
        var phone = database.NewPhone();
        await database.SeedIdentityAsync(phone, MobileRole.Fan);
        await RequestAsync(client, phone);
        var code = factory.Sms.Latest(phone);
        foreach (var value in new[] { "\"30\"", "true", "30.0", "91" })
        {
            using var document = JsonDocument.Parse(value);
            await VerifyAsync(client, phone, code, 422, "fan", true, document.RootElement.Clone());
        }
        Check.Equal(0, (await database.ChallengeAsync(phone))!.VerificationCalls, "JSON validation spent an OTP comparison.");
        var omitted = await VerifyAsync(client, phone, code, 200, "fan");
        CheckDuration(Claims(omitted.GetProperty("token").GetString()!), TimeSpan.FromDays(90));

        using var second = Client();
        var explicitPhone = database.NewPhone();
        await database.SeedIdentityAsync(explicitPhone, MobileRole.Fan);
        await RequestAsync(second, explicitPhone);
        var explicitNull = await VerifyAsync(second, explicitPhone, factory.Sms.Latest(explicitPhone), 200, "fan", true, null);
        CheckDuration(Claims(explicitNull.GetProperty("token").GetString()!), TimeSpan.FromDays(1));
    }

    private async Task ExpiryAndAttemptsAsync()
    {
        using var client = Client();
        var expired = database.NewPhone();
        await database.SeedIdentityAsync(expired, MobileRole.Fan);
        await RequestAsync(client, expired);
        await database.ExpireChallengeAsync(expired);
        var failure = await VerifyAsync(client, expired, factory.Sms.Latest(expired), 400, "fan");
        Check.Equal("challenge_expired", failure.GetProperty("code").GetString(), "Expired OTP did not fail explicitly.");
        Check.True((await database.ChallengeAsync(expired))!.IsConsumed, "Expired proof was not terminal.");
        var attempted = database.NewPhone();
        await database.SeedIdentityAsync(attempted, MobileRole.Fan);
        await RequestAsync(client, attempted);
        var code = factory.Sms.Latest(attempted);
        for (var i = 0; i < 5; i++) await VerifyAsync(client, attempted, Wrong(code), 400, "fan");
        await VerifyAsync(client, attempted, code, 429, "fan");
        var stored = (await database.ChallengeAsync(attempted))!;
        Check.Equal(5, stored.VerificationCalls, "OTP comparisons exceeded five.");
        Check.True(stored.IsConsumed, "Exhausted proof remained usable.");
    }

    private async Task ConcurrentConsumptionAsync()
    {
        using var client = Client();
        var phone = database.NewPhone();
        var identity = await database.SeedIdentityAsync(phone, MobileRole.Fan);
        await RequestAsync(client, phone);
        var body = new { phone = phone.WireValue, code = factory.Sms.Latest(phone), role = "fan" };
        var responses = await Task.WhenAll(client.PostAsJsonAsync("/api/mobile/auth/verify-otp", body),
            client.PostAsJsonAsync("/api/mobile/auth/verify-otp", body));
        try
        {
            Check.Equal(1, responses.Count(response => (int)response.StatusCode == 200), "Concurrent proof issued multiple sessions.");
            Check.Equal(1, responses.Count(response => (int)response.StatusCode == 400), "Replayed concurrent proof did not fail.");
        }
        finally { foreach (var response in responses) response.Dispose(); }
        Check.Equal(1, await database.CountLoginAuditsAsync(identity), "Concurrent proof committed multiple login audits.");
    }

    private async Task InvalidRoleAsync()
    {
        using var client = Client();
        var phone = database.NewPhone();
        await database.SeedIdentityAsync(phone, MobileRole.Parent);
        await RequestAsync(client, phone);
        var code = factory.Sms.Latest(phone);
        await VerifyAsync(client, phone, code, 400, "management");
        var state = (await database.ChallengeAsync(phone))!;
        Check.True(!state.IsConsumed && state.VerificationCalls == 1, "Invalid role consumed or failed to count valid proof.");
        var fan = await VerifyAsync(client, phone, code, 200, "fan");
        Check.Equal("fan", fan.GetProperty("role").GetString(), "Retained proof could not select its available fan role.");
    }

    private async Task OrganizationScopesAsync()
    {
        using var client = Client();
        var phone = database.NewPhone();
        var admin = await database.SeedAdminAsync(OrganizationIds.Club, OrganizationIds.Academy);
        await database.SeedIdentityAsync(phone, MobileRole.Management, adminId: admin);
        await LoginAsync(client, phone, "management");
        var context = await GetAsync(client, "/api/mobile/management/organization", 200);
        Check.True(context.GetProperty("requires_organization_selection").GetBoolean(), "Two organizations silently selected a default.");
        Check.True(context.GetProperty("organization_id").ValueKind == JsonValueKind.Null, "Unselected scope exposed an active organization.");
        Check.Equal(2, context.GetProperty("available_organizations").GetArrayLength(), "Organization grants are incomplete.");
        await PostAsync(client, "/api/mobile/management/organization", new { organization_id = OrganizationIds.Club }, 200);
        Check.Equal(OrganizationIds.Club, await database.ActiveOrganizationAsync(admin), "Selected context was not persisted.");
        await PostAsync(client, "/api/mobile/management/organization", new { organization_id = "foreign-club" }, 400);
        await database.RemoveGrantAsync(admin, OrganizationIds.Academy);
        await PostAsync(client, "/api/mobile/management/organization", new { organization_id = OrganizationIds.Academy }, 403);
        Check.Equal(OrganizationIds.Club, await database.ActiveOrganizationAsync(admin), "Unauthorized selection changed persisted context.");
        await database.SetAdminActiveAsync(admin, false);
        var me = await GetAsync(client, "/api/mobile/auth/me", 200);
        Check.True(!me.GetProperty("current_role_available").GetBoolean(), "Disabled admin retained management role availability.");
        await GetAsync(client, "/api/mobile/management/organization", 403);
        await PostAsync(client, "/api/mobile/auth/switch-role", new { role = "fan" }, 200);

        using var soleClient = Client();
        var solePhone = database.NewPhone();
        var soleAdmin = await database.SeedAdminAsync(OrganizationIds.Academy);
        await database.SeedIdentityAsync(solePhone, MobileRole.Management, adminId: soleAdmin);
        await LoginAsync(soleClient, solePhone, "management");
        var sole = await GetAsync(soleClient, "/api/mobile/management/organization", 200);
        Check.Equal(OrganizationIds.Academy, sole.GetProperty("organization_id").GetString(), "Sole assignment was not selected.");
        Check.Equal(OrganizationIds.Academy, await database.ActiveOrganizationAsync(soleAdmin), "Sole assignment selection was not durable.");
        using var fanClient = Client();
        var fanPhone = database.NewPhone();
        await database.SeedIdentityAsync(fanPhone, MobileRole.Fan);
        await LoginAsync(fanClient, fanPhone, "fan");
        await GetAsync(fanClient, "/api/mobile/management/organization", 403);
    }

    private async Task DatabaseConstraintsAsync()
    {
        var phone = database.NewPhone();
        var id = await database.SeedIdentityAsync(phone, MobileRole.Fan);
        var created = DateTimeOffset.UtcNow;
        var insert = "INSERT INTO identity.mobile_identities (id,normalized_phone,role,display_name,is_active,platform,created_at,management_admin_id) VALUES (@id,@phone,@role,'Constraint fixture',true,'Web/PWA',@created,@admin)";
        await database.AssertRejectedSqlAsync(insert, PostgresErrorCodes.UniqueViolation,
            new("id", Guid.NewGuid()), new("phone", phone.Digits), new("role", "fan"), new("created", created), new("admin", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = DBNull.Value });
        await database.AssertRejectedSqlAsync(insert, PostgresErrorCodes.ForeignKeyViolation,
            new("id", Guid.NewGuid()), new("phone", database.NewPhone().Digits), new("role", "management"), new("created", created), new("admin", Guid.NewGuid()));
        var admin = await database.SeedAdminAsync(OrganizationIds.Club);
        await database.AssertRejectedSqlAsync("UPDATE identity.admin_accounts SET active_organization_id=@organization WHERE id=@id",
            PostgresErrorCodes.ForeignKeyViolation, new("organization", OrganizationIds.Academy), new("id", admin));
        var challengeSql = "INSERT INTO identity.otp_challenges (id,normalized_phone,code_digest,created_at,expires_at,verification_calls,is_consumed) VALUES (@id,@phone,@digest,@created,@expires,@calls,false)";
        await database.AssertRejectedSqlAsync(challengeSql, PostgresErrorCodes.CheckViolation,
            new("id", Guid.NewGuid()), new("phone", database.NewPhone().Digits), new("digest", new byte[32]), new("created", created), new("expires", created.AddMinutes(6)), new("calls", (object)0));
        await database.AssertRejectedSqlAsync(challengeSql, PostgresErrorCodes.CheckViolation,
            new("id", Guid.NewGuid()), new("phone", database.NewPhone().Digits), new("digest", new byte[31]), new("created", created), new("expires", created.AddMinutes(5)), new("calls", (object)0));
        await database.AssertRejectedSqlAsync(challengeSql, PostgresErrorCodes.CheckViolation,
            new("id", Guid.NewGuid()), new("phone", database.NewPhone().Digits), new("digest", new byte[32]), new("created", created), new("expires", created.AddMinutes(5)), new("calls", 6));
        Check.True(id != Guid.Empty, "Identity constraint fixture was not created.");
    }

    private async Task ThrottlesAsync()
    {
        using var client = Client();
        var burstPhones = Enumerable.Range(0, 6).Select(_ => database.NewPhone()).ToArray();
        var ipResponses = await Task.WhenAll(burstPhones.Select(phone => client.PostAsJsonAsync(
            "/api/mobile/auth/request-otp", new { phone = phone.WireValue })));
        try
        {
            Check.Equal(5, ipResponses.Count(response => (int)response.StatusCode == 200), "Shared IP request capacity differs.");
            Check.Equal(1, ipResponses.Count(response => (int)response.StatusCode == 429), "Concurrent IP request throttle exceeded capacity.");
            var index = Array.FindIndex(ipResponses, response => (int)response.StatusCode == 429);
            Check.True(ipResponses[index].Headers.RetryAfter is not null, "Request throttle omitted Retry-After.");
            Check.Equal(0, factory.Sms.Count(burstPhones[index]), "Throttled request still delivered SMS.");
        }
        finally { foreach (var response in ipResponses) response.Dispose(); }

        var destination = database.NewPhone();
        var concurrentClients = Enumerable.Range(0, 6).Select(_ => Client()).ToArray();
        try
        {
            var responses = await Task.WhenAll(concurrentClients.Select(item => item.PostAsJsonAsync(
                "/api/mobile/auth/request-otp", new { phone = destination.WireValue })));
            try
            {
                Check.Equal(5, responses.Count(response => (int)response.StatusCode == 200), "Destination throttle did not allow its initial capacity.");
                Check.Equal(1, responses.Count(response => (int)response.StatusCode == 429), "Concurrent destination throttle exceeded capacity.");
                Check.Equal(5, factory.Sms.Count(destination), "Concurrent destination throttle delivered too many messages.");
            }
            finally { foreach (var response in responses) response.Dispose(); }
        }
        finally { foreach (var item in concurrentClients) item.Dispose(); }

        using var verifier = Client();
        var unknown = database.NewPhone();
        var verificationResponses = await Task.WhenAll(Enumerable.Range(0, 11).Select(_ => verifier.PostAsJsonAsync(
            "/api/mobile/auth/verify-otp", new { phone = unknown.WireValue, code = "123456", role = "fan" })));
        try
        {
            Check.Equal(10, verificationResponses.Count(response => (int)response.StatusCode == 400), "Shared IP verify capacity differs.");
            Check.Equal(1, verificationResponses.Count(response => (int)response.StatusCode == 429), "Concurrent IP verify throttle exceeded capacity.");
        }
        finally { foreach (var response in verificationResponses) response.Dispose(); }
    }

    private async Task DeliveryFailureRaceAsync()
    {
        using var olderClient = Client();
        using var newerClient = Client();
        var phone = database.NewPhone();
        await database.SeedIdentityAsync(phone, MobileRole.Fan);
        var gate = factory.Sms.HoldNext(phone);
        var olderRequest = olderClient.PostAsJsonAsync("/api/mobile/auth/request-otp", new { phone = phone.WireValue });
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var older = (await database.ChallengeAsync(phone))!;
        await RequestAsync(newerClient, phone);
        var newerCode = factory.Sms.Latest(phone);
        var newer = (await database.ChallengeAsync(phone))!;
        Check.True(older.Id != newer.Id, "Resend did not replace the challenge ID.");
        gate.Result.TrySetResult(false);
        using var olderResponse = await olderRequest;
        await ExpectAsync(olderResponse, 503);
        Check.Equal(newer.Id, (await database.ChallengeAsync(phone))!.Id, "Older delivery failure deleted the newer challenge.");
        await VerifyAsync(newerClient, phone, newerCode, 200, "fan");
    }

    private async Task JwtValidationAsync()
    {
        using var client = Client();
        var phone = database.NewPhone();
        var id = await database.SeedIdentityAsync(phone, MobileRole.Fan);
        var login = await LoginAsync(client, phone, "fan");
        var token = login.GetProperty("token").GetString()!;
        var parts = token.Split('.');
        parts[2] = (parts[2][0] == 'A' ? "B" : "A") + parts[2][1..];
        client.DefaultRequestHeaders.Authorization = new("Bearer", string.Join('.', parts));
        await GetAsync(client, "/api/mobile/auth/me", 401);
        foreach (var (issuer, audience, type, role, expired, status) in new[]
        {
            ("foreign-issuer", IdentityApplicationFactory.Audience, "mobile_access", "fan", false, 401),
            (IdentityApplicationFactory.Issuer, "foreign-audience", "mobile_access", "fan", false, 401),
            (IdentityApplicationFactory.Issuer, IdentityApplicationFactory.Audience, "access", "fan", false, 403),
            (IdentityApplicationFactory.Issuer, IdentityApplicationFactory.Audience, "mobile_access", "super_admin", false, 401),
            (IdentityApplicationFactory.Issuer, IdentityApplicationFactory.Audience, "mobile_access", "fan", true, 401)
        })
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", SignedToken(id, phone, issuer, audience, type, role, expired));
            await GetAsync(client, "/api/mobile/auth/me", status);
        }
    }

    private string SignedToken(Guid id, PhoneNumber phone, string issuer, string audience, string type, string role, bool expired)
    {
        var now = DateTimeOffset.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer, Audience = audience, IssuedAt = now.AddMinutes(-2).UtcDateTime,
            NotBefore = now.AddMinutes(-2).UtcDateTime,
            Expires = (expired ? now.AddMinutes(-1) : now.AddMinutes(5)).UtcDateTime,
            Subject = new ClaimsIdentity([new Claim("sub", id.ToString()), new Claim("sid", Guid.NewGuid().ToString()),
                new Claim("role", role), new Claim("phone", phone.WireValue), new Claim("type", type)]),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Convert.FromBase64String(factory.SigningKey)), SecurityAlgorithms.HmacSha256)
        });
    }

    private async Task<JsonElement> LoginAsync(HttpClient client, PhoneNumber phone, string role)
    {
        await RequestAsync(client, phone);
        var login = await VerifyAsync(client, phone, factory.Sms.Latest(phone), 200, role);
        client.DefaultRequestHeaders.Authorization = new("Bearer", login.GetProperty("token").GetString());
        return login;
    }

    private async Task<JsonElement> RequestAsync(HttpClient client, PhoneNumber phone)
    {
        var result = await PostAsync(client, "/api/mobile/auth/request-otp", new { phone = phone.WireValue }, 200);
        Check.Equal(300, result.GetProperty("expires_in").GetInt32(), "OTP duration differs.");
        Check.True(!result.TryGetProperty("otp_debug", out _), "A live API response exposed an OTP debug field.");
        return result;
    }

    private static Task<JsonElement> RegisterAsync(HttpClient client, PhoneNumber phone, string first, string last, string birthday, int status) =>
        PostAsync(client, "/api/mobile/auth/register-fan", new { phone = phone.WireValue, first_name = first, last_name = last, birthday }, status);

    private static Task<JsonElement> VerifyAsync(HttpClient client, PhoneNumber phone, string code, int status,
        string? role = null, bool rememberProvided = false, object? remember = null)
    {
        var body = new Dictionary<string, object?> { ["phone"] = phone.WireValue, ["code"] = code };
        if (role is not null) body["role"] = role;
        if (rememberProvided) body["remember_days"] = remember;
        return PostAsync(client, "/api/mobile/auth/verify-otp", body, status);
    }

    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object body, int status)
    {
        using var response = await client.PostAsJsonAsync(path, body);
        return await ExpectAsync(response, status);
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, string path, int status)
    {
        using var response = await client.GetAsync(path);
        return await ExpectAsync(response, status);
    }

    private static async Task<JsonElement> ExpectAsync(HttpResponseMessage response, int status)
    {
        var content = await response.Content.ReadAsByteArrayAsync();
        using var document = JsonDocument.Parse(content);
        var body = document.RootElement.Clone();
        var error = body.TryGetProperty("code", out var code) ? code.GetString() : null;
        Check.Equal(status, (int)response.StatusCode, $"Expected HTTP {status}; observed {(int)response.StatusCode} ({error ?? "no application fault code"}).");
        return body;
    }

    private static JsonElement Claims(string token)
    {
        var segment = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        segment = segment.PadRight((segment.Length + 3) / 4 * 4, '=');
        using var document = JsonDocument.Parse(Convert.FromBase64String(segment));
        return document.RootElement.Clone();
    }

    private static void CheckDuration(JsonElement claims, TimeSpan duration)
    {
        var actual = claims.GetProperty("exp").GetInt64() - claims.GetProperty("iat").GetInt64();
        Check.True(Math.Abs(actual - duration.TotalSeconds) <= 2, "Device session duration differs from remember-days policy.");
    }

    private static string Wrong(string code) => code == "000000" ? "111111" : "000000";
}
