using System.Net;
using System.Text;
using System.Text.Json;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Application.Roster;
using LefteriaFc.Infrastructure.PublicContent;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    private static Task PublicContentPolicyAsync()
    {
        var bytes = PublicContentFixture();
        var plan = PublicContentImportValidator.Validate(bytes);
        if (plan.Source.Count != 8 || plan.Documents.Count != 9 || !plan.ManifestBytes.AsSpan().SequenceEqual(bytes)) throw new InvalidOperationException("Public import must retain every original byte and row.");
        var safe = string.Join("\n", plan.Documents.Select(row => row.DataJson));
        foreach (var secret in new[] { "sentinel-provider-credential", "sentinel-bank", "sentinel-medical", "sentinel-parent" })
            if (safe.Contains(secret, StringComparison.Ordinal)) throw new InvalidOperationException("Public projection exposed a private source field.");
        if (!plan.Source.Any(row => row.Json.Contains("sentinel-provider-credential", StringComparison.Ordinal))) throw new InvalidOperationException("Private source evidence was discarded.");
        foreach (var invalid in new[]
        {
            Encoding.UTF8.GetString(bytes).Replace("\"schema_version\":1", "\"schema_version\":1,\"schema_version\":1", StringComparison.Ordinal),
            Encoding.UTF8.GetString(bytes).Replace("\"source_commit\":\"" + new string('a',40) + "\"", "\"source_commit\":\"invalid\"", StringComparison.Ordinal),
            Encoding.UTF8.GetString(bytes).Replace("\"organization_id\":\"lefteria_fc\"", "\"organization_id\":\"other-club\"", StringComparison.Ordinal)
        })
        {
            try { PublicContentImportValidator.Validate(Encoding.UTF8.GetBytes(invalid)); }
            catch (RosterImportValidationException) { continue; }
            throw new InvalidOperationException("Invalid public manifest was accepted.");
        }
        var malicious = Encoding.UTF8.GetString(bytes).Replace("https://example.test/news.jpg", "javascript:alert(1)", StringComparison.Ordinal);
        var omitted = PublicContentImportValidator.Validate(Encoding.UTF8.GetBytes(malicious));
        if (omitted.Documents.Any(row => row.DataJson.Contains("javascript:", StringComparison.Ordinal))) throw new InvalidOperationException("Unsafe media URL reached a public projection.");
        return Task.CompletedTask;
    }

    private async Task PublicContentEndToEndAsync()
    {
        using var client = Client();
        var before = await client.GetAsync("/api/public/site");
        if (before.StatusCode != HttpStatusCode.ServiceUnavailable) throw new InvalidOperationException("Unimported public content must be unavailable.");
        if ((await client.GetAsync("/api/public/news")).StatusCode != HttpStatusCode.ServiceUnavailable ||
            (await client.GetAsync("/api/public/news/academy-only")).StatusCode != HttpStatusCode.ServiceUnavailable)
            throw new InvalidOperationException("Unimported global public news must be unavailable.");
        var plan = PublicContentImportValidator.Validate(PublicContentFixture());
        try
        {
            var applied = await database.ApplyPublicContentAsync(plan);
            var replay = await database.ApplyPublicContentAsync(plan);
            if (!applied.Applied || !replay.Noop) throw new InvalidOperationException("Initial public import/replay failed.");
            client.DefaultRequestHeaders.Add("Cookie", "access_token=invalid; active_organization_id=lefteria_fc_academy");
            client.DefaultRequestHeaders.Authorization = new("Bearer", "invalid-public-probe");
            var response = await client.GetAsync("/api/public/site?organization_id=lefteria_fc");
            var text = await response.Content.ReadAsStringAsync();
            if (response.StatusCode != HttpStatusCode.OK || text.Contains("sentinel-", StringComparison.Ordinal)) throw new InvalidOperationException("Public site exposed source data or inherited invalid credentials.");
            using var site = JsonDocument.Parse(text);
            if (site.RootElement.GetProperty("time_zone").GetString() != PublicFixtureClock.TimeZone ||
                !site.RootElement.GetProperty("generated_at").TryGetDateTimeOffset(out _) ||
                site.RootElement.GetProperty("fixture_clock").GetArrayLength() != 1 ||
                site.RootElement.GetProperty("fixture_clock")[0].GetProperty("id").GetString() != "match" ||
                site.RootElement.GetProperty("fixture_clock")[0].GetProperty("kickoff_at").GetDateTimeOffset() != DateTimeOffset.Parse("2026-11-07T13:00:00Z"))
                throw new InvalidOperationException("The public clock must expose the server snapshot and exact scoped Cyprus kickoff.");
            if (site.RootElement.GetProperty("teams").GetArrayLength() != 1 || site.RootElement.GetProperty("academy_groups").GetArrayLength() != 0 ||
                site.RootElement.GetProperty("news").GetArrayLength() != 2) throw new InvalidOperationException("FC public content scope is wrong.");
            // The imported public document alone does not authorize an Academy
            // group. The anonymous directory stays empty until an authorized
            // manager has persisted the group's canonical admin profile.
            var academy = await client.GetAsync("/api/public/site?organization_id=lefteria_fc_academy");
            using var academySite = JsonDocument.Parse(await academy.Content.ReadAsStringAsync());
            if (academy.StatusCode != HttpStatusCode.OK || academySite.RootElement.GetProperty("teams").GetArrayLength() != 0 ||
                academySite.RootElement.GetProperty("academy_groups").GetArrayLength() != 0 ||
                academySite.RootElement.GetProperty("news").GetArrayLength() != 2)
                throw new InvalidOperationException("An imported-only Academy group was published without an admin profile.");
            var globalResponse = await client.GetAsync("/api/public/news");
            var globalText = await globalResponse.Content.ReadAsStringAsync();
            using var globalNews = JsonDocument.Parse(globalText);
            var globalIds = globalNews.RootElement.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetString()).ToArray();
            if (globalResponse.StatusCode != HttpStatusCode.OK || globalText.Contains("sentinel-", StringComparison.Ordinal) ||
                globalNews.RootElement.GetProperty("total").GetInt32() != 3 || !globalIds.SequenceEqual(new[] { "academy-only", "first", "shared" }))
                throw new InvalidOperationException("Default public news must include both organizations, deduplicate shared articles and sort by creation time without credentials.");
            using (var anonymousClient = Client())
            {
                var anonymousResponse = await anonymousClient.GetAsync("/api/public/news");
                if (anonymousResponse.StatusCode != HttpStatusCode.OK || await anonymousResponse.Content.ReadAsStringAsync() != globalText)
                    throw new InvalidOperationException("CMS cookies and bearer headers must not change the anonymous global public feed.");
            }
            foreach (var scope in new[] { "lefteria_fc", "lefteria_fc_academy" })
            {
                var scopedResponse = await client.GetAsync("/api/public/news?organization_id=" + scope);
                using var scopedNews = JsonDocument.Parse(await scopedResponse.Content.ReadAsStringAsync());
                var scopedIds = scopedNews.RootElement.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetString()).ToArray();
                var expected = scope == "lefteria_fc" ? new[] { "first", "shared" } : new[] { "academy-only", "shared" };
                if (scopedResponse.StatusCode != HttpStatusCode.OK || scopedNews.RootElement.GetProperty("total").GetInt32() != 2 || !scopedIds.SequenceEqual(expected))
                    throw new InvalidOperationException("Explicit public news scope must retain its exact visibility filter.");
            }
            foreach (var articlePath in new[] { "/api/public/news/academy-only", "/api/public/news/shared", "/api/public/news/academy-only?organization_id=lefteria_fc_academy" })
            {
                var articleResponse = await client.GetAsync(articlePath);
                var articleText = await articleResponse.Content.ReadAsStringAsync();
                if (articleResponse.StatusCode != HttpStatusCode.OK || articleText.Contains("sentinel-", StringComparison.Ordinal))
                    throw new InvalidOperationException("Global public article lookup must include Academy and shared articles safely.");
            }
            var pagedResponse = await client.GetAsync("/api/public/news?offset=1&limit=1");
            using var pagedNews = JsonDocument.Parse(await pagedResponse.Content.ReadAsStringAsync());
            if (pagedResponse.StatusCode != HttpStatusCode.OK || pagedNews.RootElement.GetProperty("total").GetInt32() != 3 ||
                pagedNews.RootElement.GetProperty("items").GetArrayLength() != 1 || pagedNews.RootElement.GetProperty("items")[0].GetProperty("id").GetString() != "first")
                throw new InvalidOperationException("Global public pagination must operate after shared-article deduplication.");
            var categoryResponse = await client.GetAsync("/api/public/news?category=" + Uri.EscapeDataString("Ακαδημία"));
            using var categoryNews = JsonDocument.Parse(await categoryResponse.Content.ReadAsStringAsync());
            if (categoryResponse.StatusCode != HttpStatusCode.OK || categoryNews.RootElement.GetProperty("total").GetInt32() != 1 ||
                categoryNews.RootElement.GetProperty("items")[0].GetProperty("id").GetString() != "academy-only")
                throw new InvalidOperationException("Global public news category filtering lost Academy content.");
            if ((await client.GetAsync("/api/public/site?organization_id=other")).StatusCode != HttpStatusCode.BadRequest ||
                (await client.GetAsync("/api/public/news/first?organization_id=lefteria_fc_academy")).StatusCode != HttpStatusCode.NotFound ||
                (await client.GetAsync("/api/public/news/academy-only?organization_id=lefteria_fc")).StatusCode != HttpStatusCode.NotFound ||
                (await client.GetAsync("/api/public/news?organization_id=all")).StatusCode != HttpStatusCode.BadRequest ||
                (await client.GetAsync("/api/public/news?limit=101")).StatusCode != HttpStatusCode.BadRequest)
                throw new InvalidOperationException("Public selectors must be bounded and exact.");
            // Protect the query contract before moving pagination into SQL.
            // Use the existing disposable fixture, never a hosted database.
            await AssertNewsQueryParityAsync(client);
            await database.AssertNewsSlugRegistryAsync();
            await database.AssertConcurrentNewsSlugClaimAsync();
            await database.AssertPublicContentRuntimeBoundaryAsync();
            await database.AssertPublicProjectionConstraintAsync();
            await database.AssertWebPlatformForwardRecoveryAsync();
            await database.AssertPublicContentReplayDetectsChangeAsync(plan);
        }
        finally { await database.CleanupPublicContentAsync(plan); }
    }

    private async Task AssertNewsQueryParityAsync(HttpClient client)
    {
        async Task AssertPage(string query, int total, int offset, int limit, params string[] expectedIds)
        {
            using var response = await client.GetAsync("/api/public/news" + query);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = json.RootElement;
            var ids = root.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetString()).ToArray();
            if (response.StatusCode != HttpStatusCode.OK ||
                root.GetProperty("total").GetInt32() != total ||
                root.GetProperty("offset").GetInt32() != offset ||
                root.GetProperty("limit").GetInt32() != limit ||
                !ids.SequenceEqual(expectedIds))
                throw new InvalidOperationException("Public news query contract changed: " + query);
        }

        // Stable totals even beyond the end of the filtered result.
        await AssertPage("?offset=3&limit=1", 3, 3, 1);
        await AssertPage("?offset=20000&limit=100", 3, 20000, 100);
        await AssertPage("?category=missing-category&offset=0&limit=1", 0, 0, 1);
        await AssertPage("?organization_id=lefteria_fc&offset=1&limit=1", 2, 1, 1, "shared");

        // Exact database lookup must not fold case, return another owner,
        // treat SQL metacharacters as selectors or reject missing long IDs
        // with a database error. All retain the public 404/no-store contract.
        foreach (var path in new[]
        {
            "/api/public/news/SHARED",
            "/api/public/news/not-present",
            "/api/public/news/" + Uri.EscapeDataString("shared' OR '1'='1"),
            "/api/public/news/" + new string('x', 129),
            "/api/public/news/first?organization_id=lefteria_fc_academy"
        })
        {
            using var missing = await client.GetAsync(path);
            if (missing.StatusCode != HttpStatusCode.NotFound ||
                missing.Headers.CacheControl?.NoStore != true)
                throw new InvalidOperationException("Exact public detail lookup changed its missing-article contract.");
            using var json = JsonDocument.Parse(await missing.Content.ReadAsStringAsync());
            if (json.RootElement.GetProperty("code").GetString() != "not_found")
                throw new InvalidOperationException("Public detail error contract changed.");
        }

        await database.AddPublicNewsParityOverlaysAsync();
        try
        {
            // The globally selected shared record must come from the same
            // organization as the unfiltered feed, even when only the other
            // organization's edit matches the requested category.
            await AssertPage("?offset=0&limit=1", 3, 0, 1, "academy-only");
            await AssertPage("?category=Parity-FC&limit=1", 1, 0, 1, "shared");
            await AssertPage("?category=Parity-FC&offset=1&limit=1", 1, 1, 1);
            await AssertPage("?category=Parity-Academy&limit=1", 0, 0, 1);
            await AssertPage("?category=parity-fc&limit=1", 0, 0, 1);
            await AssertPage("?organization_id=lefteria_fc_academy&category=Parity-Academy&limit=1",
                1, 0, 1, "shared");
            await AssertPage("?organization_id=lefteria_fc&category=Parity-Academy&limit=1", 0, 0, 1);

            foreach (var (query, expectedOwner, expectedTitle, expectedCategory) in new[]
            {
                ("", "lefteria_fc", "Parity FC edition", "Parity-FC"),
                ("?organization_id=lefteria_fc", "lefteria_fc", "Parity FC edition", "Parity-FC"),
                ("?organization_id=lefteria_fc_academy", "lefteria_fc_academy", "Parity Academy edition", "Parity-Academy")
            })
            {
                using var response = await client.GetAsync("/api/public/news/shared" + query);
                var text = await response.Content.ReadAsStringAsync();
                using var json = JsonDocument.Parse(text);
                var article = json.RootElement;
                if (response.StatusCode != HttpStatusCode.OK ||
                    article.GetProperty("organization_id").GetString() != expectedOwner ||
                    article.GetProperty("title").GetString() != expectedTitle ||
                    article.GetProperty("category").GetString() != expectedCategory ||
                    article.TryGetProperty("version", out _) ||
                    text.Contains("sentinel-", StringComparison.Ordinal))
                    throw new InvalidOperationException("Public article overlay visibility or precedence changed.");
            }
        }
        finally { await database.RemovePublicNewsParityOverlaysAsync(); }
    }

    private static byte[] PublicContentFixture() => JsonSerializer.SerializeToUtf8Bytes(new
    {
        schema_version=1, source_repository="MikeCy-com/Lefteria-FC", source_commit=new string('a',40),
        complete_collections=new[]{"club_profile","news","teams","academy_groups","fixtures"},
        club_profile=new[]{new {id="club-profile",organization_id="lefteria_fc",name="Fixture club",twilio_auth_token="sentinel-provider-credential",bank_iban="sentinel-bank"}},
        teams=new[]{new{id="team",name="Fixture first team"}},
        academy_groups=new[]{new{id="group",name="Fixture academy",age_range="U12",training_schedule="Monday",description="Fixture group"}},
        news=new[]{
            new{id="first",title="Fixture first-team article",content="Public **Markdown**",excerpt="Fixture excerpt",category="Νέα",audience="first_team",image_url="https://example.test/news.jpg",created_at="2026-10-31T12:00:00Z"},
            new{id="shared",title="Fixture shared article",content="Public text",excerpt="Fixture excerpt",category="Νέα",audience="both",image_url="https://example.test/news.jpg",created_at="2026-10-30T12:00:00Z"},
            new{id="academy-only",title="Fixture academy article",content="Public academy text",excerpt="Fixture academy excerpt",category="Ακαδημία",audience="academy",image_url="https://example.test/news.jpg",created_at="2026-11-01T12:00:00Z"}},
        fixtures=new object[]{
            new{id="match",home_team="Fixture club",away_team="Fixture opponent",match_date="2026-11-07",match_time="15:00",venue="Fixture venue",competition="Fixture competition",status="Scheduled",parent_phone="sentinel-parent",injuries="sentinel-medical"},
            new{id="academy-match",home_team="Fixture academy",away_team="Fixture academy opponent",match_date="2026-11-08",match_time="10:00",venue="Fixture venue",competition="Fixture competition",status="Scheduled",academy_group_id="group",parent_phone="sentinel-parent",injuries="sentinel-medical"}}
    });
}

internal sealed partial class TestDatabase
{
    // Fixture-only owner writes seed safe public overlays. HTTP reads below
    // are still performed through the real application and restricted DB role.
    public async Task AddPublicNewsParityOverlaysAsync()
    {
        await using var context = CreateContext();
        var rows = await context.Set<PublicContentDocumentRecord>().AsNoTracking()
            .Where(row => row.Kind == "news" && row.Id == "shared").ToArrayAsync();
        if (rows.Length != 2 || rows.Select(row => row.OrganizationId).Distinct().Count() != 2 ||
            rows.Any(row => row.OrganizationId is not ("lefteria_fc" or "lefteria_fc_academy")) ||
            await context.Set<AdminClubEditRecord>().AnyAsync(row => row.Kind == "news" && row.Id == "shared"))
            throw new InvalidOperationException("Unexpected news parity fixture; refusing overlay seed.");

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        foreach (var row in rows)
        {
            var original = JsonSerializer.Deserialize<PublicArticle>(row.DataJson, options)
                ?? throw new InvalidOperationException("Invalid fixture article.");
            var isAcademy = row.OrganizationId == "lefteria_fc_academy";
            var edited = original with
            {
                Title = isAcademy ? "Parity Academy edition" : "Parity FC edition",
                Category = isAcademy ? "Parity-Academy" : "Parity-FC"
            };
            context.Set<AdminClubEditRecord>().Add(new AdminClubEditRecord
            {
                Kind = row.Kind, OrganizationId = row.OrganizationId, Id = row.Id,
                DataJson = JsonSerializer.Serialize(edited, options), Version = 1
            });
        }
        await context.SaveChangesAsync();
    }

    public async Task RemovePublicNewsParityOverlaysAsync()
    {
        await using var context = CreateContext();
        var removed = await context.Set<AdminClubEditRecord>()
            .Where(row => row.Kind == "news" && row.Id == "shared" && row.Version == 1 &&
                (row.OrganizationId == "lefteria_fc" || row.OrganizationId == "lefteria_fc_academy"))
            .ExecuteDeleteAsync();
        if (removed != 2) throw new InvalidOperationException("Unexpected parity overlay cleanup count.");
    }

    // Owner-only disposable database probes. The transaction is rolled back
    // before normal import fixture cleanup; no public routing is activated.
    public async Task AssertNewsSlugRegistryAsync()
    {
        await using var context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await context.Database.ExecuteSqlRawAsync("""
            DO $slug_test$
            BEGIN
              IF public_content.claim_news_slug('lefteria_fc','shared','fixture-news',0)
                IS DISTINCT FROM 'fixture-news'
              THEN RAISE EXCEPTION 'initial reservation failed'; END IF;
              IF public_content.claim_news_slug('lefteria_fc','shared','renamed-news',1)
                IS DISTINCT FROM 'renamed-news'
              THEN RAISE EXCEPTION 'rename failed'; END IF;
              IF (SELECT count(*) FROM public_content.news_slug_aliases
                WHERE organization_id='lefteria_fc' AND article_id='shared')<>2
                OR NOT EXISTS(SELECT 1 FROM public_content.news_slug_current
                  WHERE organization_id='lefteria_fc' AND article_id='shared' AND slug='renamed-news' AND revision=2)
              THEN RAISE EXCEPTION 'alias history lost'; END IF;
              BEGIN
                PERFORM public_content.claim_news_slug('lefteria_fc_academy','shared','fixture-news',0);
                RAISE EXCEPTION 'cross-owner alias reuse accepted';
              EXCEPTION WHEN unique_violation THEN NULL; END;
              BEGIN
                PERFORM public_content.claim_news_slug('lefteria_fc','first','shared',0);
                RAISE EXCEPTION 'legacy ID collision accepted';
              EXCEPTION WHEN unique_violation THEN NULL; END;
              BEGIN
                PERFORM public_content.claim_news_slug('lefteria_fc','shared','stale-news',1);
                RAISE EXCEPTION 'stale rename accepted';
              EXCEPTION WHEN serialization_failure THEN NULL; END;
              BEGIN
                PERFORM public_content.claim_news_slug('lefteria_fc','academy-only','foreign-news',0);
                RAISE EXCEPTION 'foreign article accepted';
              EXCEPTION WHEN invalid_parameter_value THEN NULL; END;
              BEGIN
                PERFORM public_content.claim_news_slug('lefteria_fc','first','../bad',0);
                RAISE EXCEPTION 'invalid slug accepted';
              EXCEPTION WHEN invalid_parameter_value THEN NULL; END;
              BEGIN
                PERFORM public_content.claim_news_slug('lefteria_fc','first','search',0);
                RAISE EXCEPTION 'reserved route accepted';
              EXCEPTION WHEN invalid_parameter_value THEN NULL; END;
              IF public_content.claim_news_slug('lefteria_fc','shared','fixture-news',2)
                IS DISTINCT FROM 'fixture-news'
              THEN RAISE EXCEPTION 'own historical alias could not be restored'; END IF;
              -- Even the now-historical "renamed-news" reservation must
              -- reject a future published article with that raw identifier.
              BEGIN
                INSERT INTO public_content.documents(kind,organization_id,id,data_json,display_order)
                  SELECT kind,organization_id,'renamed-news',
                    jsonb_set(data_json,ARRAY['id'],to_jsonb('renamed-news'::text)),display_order
                  FROM public_content.documents
                  WHERE kind='news' AND organization_id='lefteria_fc_academy' AND id='academy-only';
                RAISE EXCEPTION 'future legacy ID collision accepted';
              EXCEPTION WHEN unique_violation THEN
                IF SQLERRM<>'news_slug_legacy_collision' THEN RAISE; END IF;
              END;
              BEGIN
                UPDATE public_content.documents
                  SET id='fixture-news',data_json=jsonb_set(data_json,ARRAY['id'],to_jsonb('fixture-news'::text))
                  WHERE kind='news' AND organization_id='lefteria_fc_academy' AND id='academy-only';
                RAISE EXCEPTION 'existing raw ID change shadowed an alias';
              EXCEPTION WHEN unique_violation THEN
                IF SQLERRM<>'news_slug_legacy_collision' THEN RAISE; END IF;
              END;
              -- A -> B -> A cannot resurrect a request from revision 1.
              BEGIN
                PERFORM public_content.claim_news_slug('lefteria_fc','shared','aba-stale',1);
                RAISE EXCEPTION 'stale ABA rename accepted';
              EXCEPTION WHEN serialization_failure THEN NULL; END;
              IF public_content.claim_news_slug('lefteria_fc','shared','fixture-news',3)
                IS DISTINCT FROM 'fixture-news'
                OR NOT EXISTS(SELECT 1 FROM public_content.news_slug_current
                  WHERE organization_id='lefteria_fc' AND article_id='shared' AND slug='fixture-news' AND revision=3)
              THEN RAISE EXCEPTION 'same-slug no-op changed revision'; END IF;
              BEGIN
                PERFORM public_content.claim_news_slug('lefteria_fc','shared','fixture-news',NULL);
                RAISE EXCEPTION 'missing revision accepted';
              EXCEPTION WHEN invalid_parameter_value THEN NULL; END;
              BEGIN
                PERFORM public_content.claim_news_slug('lefteria_fc','shared','fixture-news',-1);
                RAISE EXCEPTION 'negative revision accepted';
              EXCEPTION WHEN invalid_parameter_value THEN NULL; END;
              IF (SELECT count(*) FROM public_content.news_slug_aliases)<>2
              THEN RAISE EXCEPTION 'failed claims changed persistent reservations'; END IF;
            END $slug_test$;
            """);
        try { await ExecuteMigrationOperationsAsync(context, "PublicNewsSlugRegistry", true); }
        catch (PostgresException error) when (error.SqlState == "P0001" &&
            error.MessageText == "news_slug_history_requires_forward_recovery")
        { await transaction.RollbackAsync(); return; }
        throw new InvalidOperationException("Reserved URL history did not block destructive rollback.");
    }

    public async Task AssertWebPlatformForwardRecoveryAsync()
    {
        await using var context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        try { await ExecuteMigrationOperationsAsync(context, "PublicWebsiteAndWebAdmin", true); }
        catch (PostgresException error) when (error.SqlState == "P0001" && error.MessageText == "web_platform_data_requires_forward_recovery")
        { await transaction.RollbackAsync(); return; }
        throw new InvalidOperationException("Populated public content did not refuse destructive migration rollback.");
    }

    public async Task AssertPublicProjectionConstraintAsync()
    {
        foreach (var invalid in new[] { "{}", "{\"id\":null,\"organization_id\":null}" })
        {
            await using var context = CreateContext();
            await using var transaction = await context.Database.BeginTransactionAsync();
            try
            {
                await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE public_content.documents SET data_json={invalid}::jsonb WHERE kind='team'");
            }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.CheckViolation)
            { await transaction.RollbackAsync(); continue; }
            throw new InvalidOperationException("Public projection constraint accepted missing or null identity fields.");
        }
    }

    public Task<PublicContentImportResult> ApplyPublicContentAsync(ValidatedPublicContentImport plan) =>
        PostgresPublicContentImport.ApplyAsync(plan, fixture.AdminConnectionString, plan.Digest, FixtureConfiguration.ExpectedDatabase, "development");

    public async Task AssertPublicContentRuntimeBoundaryAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.AppConnectionString);
        await connection.OpenAsync();
        foreach (var sql in new[] {
            "SELECT manifest_bytes FROM migration.public_content_import_evidence LIMIT 1",
            "SELECT source_json FROM migration.public_content_source_documents LIMIT 1",
            "UPDATE public_content.documents SET data_json='{}' WHERE false",
            "DELETE FROM public_content.import_state WHERE false",
            "SELECT slug FROM public_content.news_slug_aliases LIMIT 1",
            "SELECT slug FROM public_content.news_slug_current LIMIT 1",
            "SELECT public_content.claim_news_slug('lefteria_fc','first','fixture-news',0)" })
        {
            try { await using var command=new NpgsqlCommand(sql,connection); await command.ExecuteNonQueryAsync(); }
            catch(PostgresException exception) when(exception.SqlState==PostgresErrorCodes.InsufficientPrivilege){continue;}
            throw new InvalidOperationException("Runtime exceeded public projection/owner-evidence boundary.");
        }
    }

    public async Task AssertPublicContentReplayDetectsChangeAsync(ValidatedPublicContentImport plan)
    {
        await using var context=CreateContext();
        var row=await context.Set<PublicContentDocumentRecord>().SingleAsync(item=>item.Kind=="team");
        var original=row.DataJson;
        row.DisplayOrder=99;
        await context.SaveChangesAsync();
        try
        {
            try { await ApplyPublicContentAsync(plan); }
            catch(RosterImportApplyException error) when(error.Code=="target_reconciliation_failed") { return; }
            throw new InvalidOperationException("Changed public projection replay must refuse.");
        }
        finally { row.DataJson=original; row.DisplayOrder=0; await context.SaveChangesAsync(); }
    }

    public async Task CleanupPublicContentAsync(ValidatedPublicContentImport plan)
    {
        await using var context=CreateContext();
        await using var transaction=await context.Database.BeginTransactionAsync();
        await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(5495884701258)");
        var state=await context.Set<PublicContentImportStateRecord>().AsNoTracking().SingleOrDefaultAsync();
        if(state?.Digest!=plan.Digest)return;
        if(await context.Set<PublicContentDocumentRecord>().CountAsync()!=plan.Documents.Count || await context.Set<PublicContentSourceDocumentRecord>().CountAsync()!=plan.Source.Count)
            throw new InvalidOperationException("Refusing cleanup of an unexpected public snapshot.");
        var ids=plan.Documents.Select(row=>row.Id).Distinct().ToArray();
        // Test-only cleanup of versioned safe overlays before imported rows.
        // The digest and exact imported row counts above prevent accidentally
        // cleaning any database other than the disposable fixture.
        await context.Set<AdminClubEditRecord>().Where(row=>ids.Contains(row.Id)).ExecuteDeleteAsync();
        await context.Set<PublicContentDocumentRecord>().Where(row=>ids.Contains(row.Id)).ExecuteDeleteAsync();
        await context.Set<PublicContentSourceDocumentRecord>().Where(row=>row.ImportDigest==plan.Digest).ExecuteDeleteAsync();
        await context.Set<PublicContentImportEvidenceRecord>().Where(row=>row.Digest==plan.Digest).ExecuteDeleteAsync();
        await context.Set<PublicContentImportStateRecord>().Where(row=>row.Digest==plan.Digest).ExecuteDeleteAsync();
        await transaction.CommitAsync();
    }
}
