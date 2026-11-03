using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Infrastructure.PublicContent;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using LefteriaFc.Infrastructure.Persistence;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminHttpChecks
{
    private const string ClubPath = "/api/admin/club?organization_id=lefteria_fc";
    private static readonly JsonSerializerOptions ClubJson = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private static AdminClubEditable ClubFields(string name = "Fictional edited club", string? history = null) => new(name,
        "Δοκιμαστικός σύλλογος", 2024, "Recorded fictional description", "Fixture motto", history, null, null,
        "Λεμεσός", "Κύπρος", "Fixture address", "public@example.test", "+35722000000", "https://example.test",
        new Dictionary<string, string> { ["instagram"] = "https://example.test/club" });
    private static async Task<AdminClubProfileDto> ClubBody(HttpResponseMessage response, int expected = 200)
    {
        Require((int)response.StatusCode == expected, "Club editor status differs from its contract.");
        Require(response.Headers.CacheControl?.NoStore == true, "Club editor responses must not be cached.");
        var text = await response.Content.ReadAsStringAsync();
        Require(!text.Contains("private-club-fixture", StringComparison.Ordinal) && !text.Contains("password_hash", StringComparison.Ordinal), "Club editor leaked private imported evidence.");
        return JsonSerializer.Deserialize<AdminClubProfileDto>(text, ClubJson) ?? throw new InvalidOperationException("Club editor response is missing.");
    }
    private static async Task<HttpResponseMessage> SaveClub(HttpClient client, string cookie, long version, AdminClubEditable? fields = null, string path = ClubPath)
    {
        using var request = Mutation(path, JsonContent.Create(new AdminClubUpdateCommand(version, fields ?? ClubFields()), options: ClubJson));
        request.Method = HttpMethod.Put; request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request);
    }
    private async Task ClubEditorAsync()
    {
        var account = await database.SeedAsync(organizations: ["lefteria_fc", "lefteria_fc_academy"]);
        using var client = Client();
        using (var anonymous = await client.GetAsync(ClubPath)) await ExpectJsonAsync(anonymous, 401, "not_authenticated");
        var login = await LoginAsync(client, account);
        using (var unavailable = await GetAsync(client, ClubPath, login.Cookie)) await ExpectJsonAsync(unavailable, 503, "public_content_unavailable");
        var plan = await database.ImportClubFixtureAsync();
        try
        {
            await database.ImportClubSectionsFixtureAsync(plan);
            await database.AssertClubMigrationRecoveryAsync(false);
            using (var initial = await GetAsync(client, ClubPath, login.Cookie)) Require((await ClubBody(initial)).Version == 0, "Imported club version must start at zero.");
            using (var missing = await GetAsync(client, "/api/admin/club", login.Cookie)) await ExpectJsonAsync(missing, 400, "invalid_organization");
            using (var unknown = await GetAsync(client, "/api/admin/club?organization_id=all", login.Cookie)) await ExpectJsonAsync(unknown, 400, "invalid_organization");
            using (var foreign = Mutation(ClubPath, JsonContent.Create(new AdminClubUpdateCommand(0, ClubFields()), options: ClubJson)))
            {
                foreign.Method = HttpMethod.Put; foreign.Headers.Add("Cookie", login.Cookie);
                foreign.Headers.Remove("Origin"); foreign.Headers.Add("Origin", "https://foreign.invalid");
                using var denied = await client.SendAsync(foreign); await ExpectJsonAsync(denied, 403, "request_origin_denied");
            }
            using (var saved = await SaveClub(client, login.Cookie, 0, ClubFields(history: new string('λ', 100000))))
            {
                var body = await ClubBody(saved);
                Require(body.Version == 1 && body.Club.History?.Length == 100000 && body.Club.Id == "club-profile" && body.Club.OrganizationId == "lefteria_fc", "Safe full-length club update failed.");
            }
            using (var publicSections = await client.GetAsync("/api/public/sections?organization_id=lefteria_fc"))
            {
                var text = await publicSections.Content.ReadAsStringAsync(); using var document = JsonDocument.Parse(text);
                var branding = document.RootElement.GetProperty("branding");
                Require(publicSections.StatusCode == HttpStatusCode.OK && branding.GetProperty("name").GetString() == "Fictional edited club" &&
                    branding.GetProperty("history").GetString()?.Length == 100000 && branding.GetProperty("city").GetString() == "Λεμεσός" &&
                    branding.GetProperty("legacy_socials")[0].GetProperty("url").GetString() == "https://example.test/club" &&
                    branding.GetProperty("stadium").GetString() == "Fixture stadium" &&
                    branding.GetProperty("first_team_socials")[0].GetProperty("url").GetString() == "https://example.test/original-fc" &&
                    !text.Contains("private-club-fixture", StringComparison.Ordinal) && !text.Contains("version", StringComparison.Ordinal),
                    "Public section branding must overlay edited common fields without replacing stadium/scope-specific source fields or exposing metadata.");
            }
            using (var academySections = await client.GetAsync("/api/public/sections?organization_id=lefteria_fc_academy"))
            {
                using var document = JsonDocument.Parse(await academySections.Content.ReadAsStringAsync());
                Require(academySections.StatusCode == HttpStatusCode.OK && document.RootElement.GetProperty("branding").GetProperty("name").GetString() == "Fictional Academy club", "FC edit changed Academy section branding.");
            }
            using (var publicSite = await client.GetAsync("/api/public/site?organization_id=lefteria_fc"))
            {
                var text = await publicSite.Content.ReadAsStringAsync(); using var document = JsonDocument.Parse(text);
                Require(publicSite.StatusCode == HttpStatusCode.OK && document.RootElement.GetProperty("club").GetProperty("name").GetString() == "Fictional edited club" &&
                    !text.Contains("private-club-fixture", StringComparison.Ordinal) && !text.Contains("version", StringComparison.Ordinal), "Public effective club projection is wrong.");
            }
            using (var academy = await GetAsync(client, "/api/admin/club?organization_id=lefteria_fc_academy", login.Cookie))
                Require((await ClubBody(academy)).Version == 0, "FC update changed Academy profile.");
            using (var stale = await SaveClub(client, login.Cookie, 0)) await ExpectJsonAsync(stale, 409, "club_version_conflict");
            var concurrent = await Task.WhenAll(SaveClub(client, login.Cookie, 1, ClubFields("Concurrent A")), SaveClub(client, login.Cookie, 1, ClubFields("Concurrent B")));
            try { Require(concurrent.Count(row => row.StatusCode == HttpStatusCode.OK) == 1 && concurrent.Count(row => row.StatusCode == HttpStatusCode.Conflict) == 1, "Concurrent club edits must have exactly one winner."); }
            finally { foreach (var response in concurrent) response.Dispose(); }
            using (var current = await GetAsync(client, ClubPath, login.Cookie)) Require((await ClubBody(current)).Version == 2, "Concurrent edit incremented version more than once.");

            var valid = JsonSerializer.Serialize(new AdminClubUpdateCommand(2, ClubFields()), ClubJson);
            foreach (var invalid in new[]
            {
                valid.Replace("\"expected_version\":2", "\"expected_version\":\"2\"", StringComparison.Ordinal),
                valid.Replace("\"expected_version\":2", "\"expected_version\":2,\"expected_version\":2", StringComparison.Ordinal),
                valid.Replace("\"expected_version\":2,", "", StringComparison.Ordinal),
                valid.Replace("\"club\":{", "\"club\":{\"id\":\"other\",", StringComparison.Ordinal),
                valid.Replace("\"club\":{", "\"club\":{\"twilio_auth_token\":\"forbidden\",", StringComparison.Ordinal),
                valid.Replace("https://example.test/club", "javascript:alert(1)", StringComparison.Ordinal),
                JsonSerializer.Serialize(new AdminClubUpdateCommand(2, ClubFields(" ")), ClubJson),
                JsonSerializer.Serialize(new AdminClubUpdateCommand(2, ClubFields(history: new string('x', 100001))), ClubJson)
            })
            {
                using var request = Mutation(ClubPath, new StringContent(invalid, Encoding.UTF8, "application/json")); request.Method = HttpMethod.Put; request.Headers.Add("Cookie", login.Cookie);
                using var denied = await client.SendAsync(request); await ExpectJsonAsync(denied, 422, "invalid_club_profile");
            }
            using (var request = Mutation(ClubPath, new UnknownLengthJsonContent(new byte[1024 * 1024 + 1])))
            { request.Method = HttpMethod.Put; request.Headers.Add("Cookie", login.Cookie); using var denied = await client.SendAsync(request); await ExpectJsonAsync(denied, 413, "request_too_large"); }
            await database.WithClubAuditFailureAsync(account, async () =>
            { using var failed = await SaveClub(client, login.Cookie, 2); await ExpectJsonAsync(failed, 503); });
            using (var unchanged = await GetAsync(client, ClubPath, login.Cookie)) Require((await ClubBody(unchanged)).Version == 2, "Failed audit committed the club edit.");
            await database.AssertClubEvidenceAsync(plan, account, 2);
            await database.AssertClubMigrationRecoveryAsync(true);
            await database.AssertClubRuntimeBoundaryAsync();

            foreach (var role in new[] { "editor", "marketing", "coach", "matchday_editor" })
            {
                var deniedAccount = await database.SeedAsync(role: role, organizations: ["lefteria_fc"]);
                using var deniedClient = Client(); var deniedLogin = await LoginAsync(deniedClient, deniedAccount);
                using var read = await GetAsync(deniedClient, ClubPath, deniedLogin.Cookie); await ExpectJsonAsync(read, 403, "admin_permission_denied");
                using var write = await SaveClub(deniedClient, deniedLogin.Cookie, 2); await ExpectJsonAsync(write, 403, "admin_permission_denied");
            }
            var superAdmin = await database.SeedAsync(role: "super_admin", organizations: ["lefteria_fc_academy"]);
            using (var superClient = Client())
            {
                var superLogin = await LoginAsync(superClient, superAdmin);
                using var saved = await SaveClub(superClient, superLogin.Cookie, 0, ClubFields("Fictional Academy edit"), "/api/admin/club?organization_id=lefteria_fc_academy");
                Require((await ClubBody(saved)).Version == 1, "Assigned super-admin cannot edit Academy settings.");
                using var outsideGrant = await SaveClub(superClient, superLogin.Cookie, 2);
                await ExpectJsonAsync(outsideGrant, 403, "organization_denied");
            }
            var fcOnly = await database.SeedAsync(organizations: ["lefteria_fc"]);
            using var scopedClient = Client(); var scopedLogin = await LoginAsync(scopedClient, fcOnly);
            using (var denied = await SaveClub(scopedClient, scopedLogin.Cookie, 0, path: "/api/admin/club?organization_id=lefteria_fc_academy")) await ExpectJsonAsync(denied, 403, "organization_denied");
            await database.RevokeOrganizationAsync(fcOnly, "lefteria_fc");
            using (var denied = await SaveClub(scopedClient, scopedLogin.Cookie, 2)) await ExpectJsonAsync(denied, 403, "organization_denied");
            var replacementAccount = await database.SeedAsync(organizations: ["lefteria_fc"]);
            using (var replacementClient = Client())
            {
                var replacementLogin = await LoginAsync(replacementClient, replacementAccount);
                using var denied = await database.ReplaceGrantDuringClubWriteAsync(replacementAccount,
                    () => SaveClub(replacementClient, replacementLogin.Cookie, 2));
                await ExpectJsonAsync(denied, 403, "organization_denied");
                await database.RevokeOrganizationAsync(replacementAccount, "lefteria_fc");
                using var unchanged = await GetAsync(client, ClubPath, login.Cookie);
                Require((await ClubBody(unchanged)).Version == 2, "A replaced but never locked grant authorized an edit.");
            }
            using (var denied = await database.ChangeRoleDuringClubWriteAsync(account, () => SaveClub(client, login.Cookie, 2)))
                await ExpectJsonAsync(denied, 403, "admin_permission_denied");
            await database.SetStateAsync(account, passwordHash: WebAdminDatabaseFixture.SpacedPasswordHash);
            using (var denied = await SaveClub(client, login.Cookie, 2)) await ExpectJsonAsync(denied, 401);
        }
        finally { await database.CleanupClubFixtureAsync(plan); }
    }
}

internal sealed partial class WebAdminDatabaseFixture
{
    public async Task<ValidatedPublicContentImport> ImportClubFixtureAsync()
    {
        await using var connection = await OwnerAsync();
        await using (var check = new NpgsqlCommand("SELECT NOT EXISTS(SELECT 1 FROM public_content.import_state) AND NOT EXISTS(SELECT 1 FROM public_content.documents) AND NOT EXISTS(SELECT 1 FROM public_content.edits)", connection))
            if (await check.ExecuteScalarAsync() is not true) throw new InvalidOperationException("Club fixture requires an empty public snapshot.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema_version = 1, source_repository = "MikeCy-com/Lefteria-FC", source_commit = new string('b', 40),
            complete_collections = new[] { "club_profile", "news", "teams", "academy_groups", "fixtures" },
            club_profile = new[] { new { id = "club-profile", organization_id = "lefteria_fc", name = "Fictional FC club", bank_iban = "private-club-fixture", stadium = "Fixture stadium", first_team_instagram = "https://example.test/original-fc" },
                new { id = "club-profile:lefteria_fc_academy", organization_id = "lefteria_fc_academy", name = "Fictional Academy club", bank_iban = "private-club-fixture", stadium = "Fixture Academy stadium", first_team_instagram = "https://example.test/original-academy" } },
            news = Array.Empty<object>(), teams = Array.Empty<object>(), academy_groups = Array.Empty<object>(), fixtures = Array.Empty<object>()
        });
        var plan = PublicContentImportValidator.Validate(bytes);
        await PostgresPublicContentImport.ApplyAsync(plan, fixture.AdminConnectionString, plan.Digest, FixtureConfiguration.ExpectedDatabase, "development");
        return plan;
    }
    public async Task ImportClubSectionsFixtureAsync(ValidatedPublicContentImport basis)
    {
        await using var owner = await OwnerAsync();
        await using (var check = new NpgsqlCommand("SELECT NOT EXISTS(SELECT 1 FROM public_sections.snapshots)", owner))
            if (await check.ExecuteScalarAsync() is not true) throw new InvalidOperationException("Club sections fixture requires no existing snapshot.");
        var root = new JsonObject
        {
            ["schema_version"] = 1, ["snapshot_version"] = 1, ["source_repository"] = ValidatedPublicContentImport.Repository,
            ["source_commit"] = basis.SourceCommit, ["base_public_sha256"] = basis.Digest, ["exported_at"] = "2026-10-31T00:00:00Z",
            ["complete_collections"] = JsonSerializer.SerializeToNode(PublicSectionsImportValidator.Collections)
        };
        foreach (var collection in PublicSectionsImportValidator.Collections) root[collection] = new JsonArray();
        using var original = JsonDocument.Parse(basis.ManifestBytes);
        foreach (var collection in new[] { "teams", "academy_groups", "fixtures", "club_profile" })
            root[collection] = JsonNode.Parse(original.RootElement.GetProperty(collection).GetRawText());
        var plan = PublicSectionsImportValidator.Validate(Encoding.UTF8.GetBytes(root.ToJsonString()));
        await PostgresPublicSectionsImport.ApplyAsync(plan, fixture.AdminConnectionString, plan.Digest, FixtureConfiguration.ExpectedDatabase, "development");
    }
    public async Task CleanupClubFixtureAsync(ValidatedPublicContentImport plan)
    {
        await using var connection = await OwnerAsync(); await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand("""
            DO $guard$ BEGIN IF (SELECT digest FROM public_content.import_state) IS DISTINCT FROM current_setting('cms.fixture_digest')
              OR (SELECT count(*) FROM public_content.documents)<>2 OR (SELECT count(*) FROM migration.public_content_source_documents)<>2
              OR EXISTS(SELECT 1 FROM public_content.documents WHERE kind<>'club' OR id NOT IN ('club-profile','club-profile:lefteria_fc_academy'))
              OR EXISTS(SELECT 1 FROM public_content.edits WHERE kind<>'club' OR id NOT IN ('club-profile','club-profile:lefteria_fc_academy'))
              OR EXISTS(SELECT 1 FROM public_sections.snapshots WHERE base_public_digest<>current_setting('cms.fixture_digest') OR version<>1)
              THEN RAISE EXCEPTION 'Refusing unexpected club fixture cleanup'; END IF; END $guard$;
            DELETE FROM public_sections.current WHERE snapshot_version IN (SELECT version FROM public_sections.snapshots WHERE base_public_digest=current_setting('cms.fixture_digest'));
            DELETE FROM migration.public_sections_issues WHERE snapshot_version IN (SELECT version FROM public_sections.snapshots WHERE base_public_digest=current_setting('cms.fixture_digest'));
            DELETE FROM public_sections.documents WHERE snapshot_version IN (SELECT version FROM public_sections.snapshots WHERE base_public_digest=current_setting('cms.fixture_digest'));
            DELETE FROM migration.public_sections_source WHERE snapshot_version IN (SELECT version FROM public_sections.snapshots WHERE base_public_digest=current_setting('cms.fixture_digest'));
            DELETE FROM migration.public_sections_evidence WHERE digest IN (SELECT digest FROM public_sections.snapshots WHERE base_public_digest=current_setting('cms.fixture_digest'));
            DELETE FROM audit.security_events WHERE action='public_sections.import' AND subject_digest IN (SELECT digest FROM public_sections.snapshots WHERE base_public_digest=current_setting('cms.fixture_digest'));
            DELETE FROM public_sections.snapshots WHERE base_public_digest=current_setting('cms.fixture_digest');
            DELETE FROM public_content.edits WHERE kind='club' AND id IN ('club-profile','club-profile:lefteria_fc_academy');
            DELETE FROM public_content.documents WHERE kind='club' AND id IN ('club-profile','club-profile:lefteria_fc_academy');
            DELETE FROM migration.public_content_source_documents WHERE import_digest=current_setting('cms.fixture_digest');
            DELETE FROM migration.public_content_import_evidence WHERE digest=current_setting('cms.fixture_digest');
            DELETE FROM public_content.import_state WHERE digest=current_setting('cms.fixture_digest');
            """, connection, transaction);
        await using (var setting = new NpgsqlCommand("SELECT set_config('cms.fixture_digest',@digest,true)", connection, transaction))
        { setting.Parameters.AddWithValue("digest", plan.Digest); await setting.ExecuteNonQueryAsync(); }
        await command.ExecuteNonQueryAsync(); await transaction.CommitAsync();
    }
    public async Task AssertClubEvidenceAsync(ValidatedPublicContentImport plan, Account account, long version)
    {
        var result = await PostgresPublicContentImport.ApplyAsync(plan, fixture.AdminConnectionString, plan.Digest, FixtureConfiguration.ExpectedDatabase, "development");
        if (!result.Noop) throw new InvalidOperationException("Club edit changed initial import replay.");
        await using var connection = await OwnerAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM audit.security_events WHERE actor_realm='admin' AND actor_identity_id=@actor AND action='admin.club.update'", connection);
        command.Parameters.AddWithValue("actor", account.Id);
        if ((long)(await command.ExecuteScalarAsync())! != version) throw new InvalidOperationException("Club edit and success audit did not commit atomically.");
    }
    public async Task WithClubAuditFailureAsync(Account account, Func<Task> operation)
    {
        RequireOwned(account); await using var connection = await OwnerAsync();
        // Fixed fixture-only object names, exact synthetic actor, always removed.
        await using (var create = new NpgsqlCommand($"""
            CREATE FUNCTION public.club_fixture_audit_failure() RETURNS trigger LANGUAGE plpgsql AS $fixture$
            BEGIN IF NEW.actor_identity_id='{account.Id:D}'::uuid AND NEW.action='admin.club.update'
              THEN RAISE EXCEPTION 'fictional_club_audit_failure'; END IF; RETURN NEW; END $fixture$;
            CREATE TRIGGER club_fixture_audit_failure BEFORE INSERT ON audit.security_events
              FOR EACH ROW EXECUTE FUNCTION public.club_fixture_audit_failure();
            """, connection)) await create.ExecuteNonQueryAsync();
        try { await operation(); }
        finally { await using var drop = new NpgsqlCommand("DROP TRIGGER club_fixture_audit_failure ON audit.security_events; DROP FUNCTION public.club_fixture_audit_failure()", connection); await drop.ExecuteNonQueryAsync(); }
    }
    public async Task<HttpResponseMessage> ChangeRoleDuringClubWriteAsync(Account account, Func<Task<HttpResponseMessage>> send)
    {
        RequireOwned(account); await using var connection = await OwnerAsync(); await using var transaction = await connection.BeginTransactionAsync();
        await using (var change = new NpgsqlCommand("UPDATE identity.admin_accounts SET role='editor' WHERE id=@id", connection, transaction))
        { change.Parameters.AddWithValue("id", account.Id); await change.ExecuteNonQueryAsync(); }
        var pending = send();
        await using var observer = await OwnerAsync(); var observed = false;
        for (var attempt = 0; attempt < 30 && !pending.IsCompleted; attempt++)
        {
            await using var probe = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE application_name='lefteria-web-admin-auth' AND wait_event_type='Lock' AND query LIKE '%lock_admin_club_authority%')", observer);
            if (await probe.ExecuteScalarAsync() is true) { observed = true; break; }
            await Task.Delay(25);
        }
        await transaction.CommitAsync();
        if (!observed) { (await pending).Dispose(); throw new InvalidOperationException("The club write did not wait for its current authority lock."); }
        return await pending;
    }
    public async Task<HttpResponseMessage> ReplaceGrantDuringClubWriteAsync(Account account, Func<Task<HttpResponseMessage>> send)
    {
        RequireOwned(account); await using var connection = await OwnerAsync(); await using var transaction = await connection.BeginTransactionAsync();
        await using (var remove = new NpgsqlCommand("DELETE FROM identity.admin_organization_grants WHERE admin_account_id=@id AND organization_id='lefteria_fc'", connection, transaction))
        { remove.Parameters.AddWithValue("id", account.Id); if (await remove.ExecuteNonQueryAsync() != 1) throw new InvalidOperationException("Missing owned fixture grant."); }
        var pending = send(); await using var observer = await OwnerAsync(); var observed = false;
        for (var attempt = 0; attempt < 40 && !pending.IsCompleted; attempt++)
        {
            await using var probe = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE application_name='lefteria-web-admin-auth' AND wait_event_type='Lock' AND query LIKE '%lock_admin_club_authority%')", observer);
            if (await probe.ExecuteScalarAsync() is true) { observed = true; break; }
            await Task.Delay(25);
        }
        await using (var replace = new NpgsqlCommand("INSERT INTO identity.admin_organization_grants(admin_account_id,organization_id) VALUES(@id,'lefteria_fc')", connection, transaction))
        { replace.Parameters.AddWithValue("id", account.Id); await replace.ExecuteNonQueryAsync(); }
        await transaction.CommitAsync();
        if (!observed) { (await pending).Dispose(); throw new InvalidOperationException("Club authority did not wait for the deleted grant tuple."); }
        // The replacement exists, but it was not the tuple selected/locked by
        // the waiting PERFORM. A fresh EXISTS must not admit it retroactively.
        await using (var exists = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM identity.admin_organization_grants WHERE admin_account_id=@id AND organization_id='lefteria_fc')", observer))
        { exists.Parameters.AddWithValue("id", account.Id); if (await exists.ExecuteScalarAsync() is not true) throw new InvalidOperationException("Replacement grant did not commit."); }
        return await pending;
    }
    public async Task AssertClubRuntimeBoundaryAsync()
    {
        using var scope = factory.Services.CreateScope();
        await using var auth = await scope.ServiceProvider.GetRequiredService<LefteriaFc.Infrastructure.WebAdmin.WebAdminConnections>().OpenAsync(CancellationToken.None);
        foreach (var sql in new[] { "UPDATE identity.admin_accounts SET role='super_admin' WHERE false", "UPDATE identity.admin_organization_grants SET organization_id='lefteria_fc' WHERE false", "UPDATE public_content.documents SET data_json='{}' WHERE false", "SELECT manifest_bytes FROM migration.public_content_import_evidence LIMIT 1" })
        {
            try { await using var denied = new NpgsqlCommand(sql, auth); await denied.ExecuteNonQueryAsync(); }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.InsufficientPrivilege) { continue; }
            throw new InvalidOperationException("Admin club role exceeded its bounded write/read privilege.");
        }
        await using var mobile = new NpgsqlConnection(fixture.AppConnectionString); await mobile.OpenAsync();
        foreach (var sql in new[] { "SELECT version FROM public_content.edits LIMIT 1", "SELECT identity.lock_admin_club_authority(repeat('0',64),'lefteria_fc')", "UPDATE public_content.edits SET version=version+1 WHERE false" })
        {
            try { await using var denied = new NpgsqlCommand(sql, mobile); await denied.ExecuteNonQueryAsync(); }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.InsufficientPrivilege) { continue; }
            throw new InvalidOperationException("Public/mobile runtime reached club editor metadata or writes.");
        }
        await using var owner = await OwnerAsync(); await using var transaction = await owner.BeginTransactionAsync();
        await using (var create = new NpgsqlCommand("CREATE FUNCTION public.club_fixture_unexpected_definer() RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS 'BEGIN RETURN; END'; REVOKE ALL ON FUNCTION public.club_fixture_unexpected_definer() FROM PUBLIC", owner, transaction)) await create.ExecuteNonQueryAsync();
        // A routine outside the exact signature remains uncallable, even though
        // its owner could otherwise cross the credential/runtime boundary.
        // The uncommitted catalog object is not visible cross-connection, so
        // probe through SET LOCAL ROLE within this rollback-only transaction.
        var authName = new NpgsqlConnectionStringBuilder(auth.ConnectionString).Username!;
        using var identifiers = new NpgsqlCommandBuilder();
        await using (var role = new NpgsqlCommand("SET LOCAL ROLE " + identifiers.QuoteIdentifier(authName), owner, transaction)) await role.ExecuteNonQueryAsync();
        try { await using var denied = new NpgsqlCommand("SELECT public.club_fixture_unexpected_definer()", owner, transaction); await denied.ExecuteNonQueryAsync(); }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.InsufficientPrivilege) { await transaction.RollbackAsync(); return; }
        throw new InvalidOperationException("An unexpected definer routine was executable by the CMS role.");
    }
    public async Task AssertClubMigrationRecoveryAsync(bool populated)
    {
        await using var verified = await OwnerAsync();
        await using var context = new LefteriaDbContext(new DbContextOptionsBuilder<LefteriaDbContext>().UseNpgsql(fixture.AdminConnectionString).Options);
        await using var transaction = await context.Database.BeginTransactionAsync();
        var assembly = context.GetService<IMigrationsAssembly>();
        var definition = assembly.Migrations.Single(row => row.Key.EndsWith("_PublicClubProfileEditor", StringComparison.Ordinal));
        var migration = assembly.CreateMigration(definition.Value, context.Database.ProviderName!);
        async Task Operations(bool down)
        {
            foreach (var sql in context.GetService<IMigrationsSqlGenerator>().Generate(down ? migration.DownOperations : migration.UpOperations, context.Model, MigrationsSqlGenerationOptions.NoTransactions))
            {
                await using var command = context.Database.GetDbConnection().CreateCommand();
                command.CommandText = sql.CommandText; command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
                await command.ExecuteNonQueryAsync();
            }
        }
        try { await Operations(true); }
        catch (PostgresException error) when (populated && error.SqlState == "P0001" && error.MessageText == "club_profile_edits_require_forward_recovery")
        { await transaction.RollbackAsync(); await AssertClubConstraintsAsync(); return; }
        if (populated) throw new InvalidOperationException("Populated club migration allowed destructive rollback.");
        await Operations(false); await transaction.RollbackAsync();
    }
    private async Task AssertClubConstraintsAsync()
    {
        foreach (var sql in new[] { "UPDATE public_content.edits SET data_json='{}'", "UPDATE public_content.edits SET data_json='{\"id\":null,\"organization_id\":null}'", "UPDATE public_content.edits SET version=0" })
        {
            await using var connection = await OwnerAsync(); await using var transaction = await connection.BeginTransactionAsync();
            try { await using var invalid = new NpgsqlCommand(sql, connection, transaction); await invalid.ExecuteNonQueryAsync(); }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.CheckViolation) { await transaction.RollbackAsync(); continue; }
            throw new InvalidOperationException("Club edit identity/version check accepted invalid storage.");
        }
        await using var missingConnection = await OwnerAsync(); await using var missingTransaction = await missingConnection.BeginTransactionAsync();
        try
        {
            await using var missing = new NpgsqlCommand("INSERT INTO public_content.edits(kind,organization_id,id,data_json,version) VALUES('club','lefteria_fc','missing-club','{\"id\":\"missing-club\",\"organization_id\":\"lefteria_fc\"}',1)", missingConnection, missingTransaction);
            await missing.ExecuteNonQueryAsync();
        }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.ForeignKeyViolation) { await missingTransaction.RollbackAsync(); return; }
        throw new InvalidOperationException("Club override without an imported base was accepted.");
    }
}
