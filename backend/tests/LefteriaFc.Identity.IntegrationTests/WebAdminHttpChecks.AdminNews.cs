using System.Net.Http.Json;
using System.Text.Json;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminHttpChecks
{
    private const string NewsPath = "/api/admin/website/news?organization_id=lefteria_fc";
    private const string AcademyNewsPath = "/api/admin/website/news?organization_id=lefteria_fc_academy";

    private async Task WebsiteNewsAdminHttpAsync()
    {
        using var anonymous = Client();
        using (var denied = await anonymous.GetAsync(NewsPath))
            await ExpectJsonAsync(denied, 401, "not_authenticated");

        var account = await database.SeedAsync(organizations: ["lefteria_fc", "lefteria_fc_academy"]);
        using var client = Client();
        var login = await LoginAsync(client, account);
        using (var unavailable = await GetAsync(client, NewsPath, login.Cookie))
            await ExpectJsonAsync(unavailable, 503, "news_unavailable");

        var fixture = await database.ImportNewsFixtureAsync();
        try
        {
            using (var firstTeam = await GetAsync(client, NewsPath, login.Cookie))
            {
                var data = await ExpectJsonAsync(firstTeam, 200);
                Require(firstTeam.Headers.CacheControl?.NoStore == true,
                    "Protected news response must not be cached.");
                Require(data.EnumerateObject().Count() == 4 &&
                    data.GetProperty("total").GetInt32() == 2 &&
                    data.GetProperty("items").GetArrayLength() == 2 &&
                    data.GetProperty("items").EnumerateArray().All(item =>
                        item.GetProperty("organization_id").GetString() == "lefteria_fc") &&
                    !data.GetRawText().Contains("private-news-fixture", StringComparison.Ordinal),
                    "Admin news leaked source evidence or crossed organizations.");
            }

            using (var academy = await GetAsync(client, AcademyNewsPath, login.Cookie))
            {
                var data = await ExpectJsonAsync(academy, 200);
                Require(data.GetProperty("total").GetInt32() == 2 &&
                    data.GetProperty("items").EnumerateArray().All(item =>
                        item.GetProperty("organization_id").GetString() == "lefteria_fc_academy"),
                    "Academy news selection leaked FC-owned records.");
            }

            using (var page = await GetAsync(client, NewsPath + "&limit=1&offset=1", login.Cookie))
            {
                var data = await ExpectJsonAsync(page, 200);
                Require(data.GetProperty("total").GetInt32() == 2 &&
                    data.GetProperty("items").GetArrayLength() == 1 &&
                    data.GetProperty("offset").GetInt32() == 1 &&
                    data.GetProperty("limit").GetInt32() == 1,
                    "Admin news pagination was not bounded and repeatable.");
            }

            // Edit only an already-published article. The original imported
            // evidence stays immutable; both public endpoints must immediately
            // reflect the exact owner-specific safe projection after saving.
            const string editPath = "/api/admin/website/news/news-shared?organization_id=lefteria_fc";
            using (var original = await GetAsync(client, editPath, login.Cookie))
            {
                var body = await ExpectJsonAsync(original, 200);
                Require(body.GetProperty("version").GetInt64() == 0, "Imported news must begin with version zero.");
                var published = body.GetProperty("article");
                Require(published.GetProperty("id").GetString() == "news-shared" &&
                    published.GetProperty("organization_id").GetString() == "lefteria_fc" &&
                    published.GetProperty("created_at").GetDateTimeOffset().Year == 2026,
                    "News editor changed source identity or publication date.");

                var editedArticle = new
                {
                    title = "Verified published FC article",
                    content = "Updated safe published content",
                    excerpt = "Scoped edited excerpt",
                    image_url = (string?)null,
                    category = "Νέα",
                    audience = "both",
                    is_featured = false
                };
                using (var update = Mutation(editPath, JsonContent.Create(new
                {
                    expected_version = 0,
                    article = editedArticle
                })))
                {
                    update.Method = HttpMethod.Put;
                    update.Headers.Add("Cookie", login.Cookie);
                    using var saved = await client.SendAsync(update);
                    var result = await ExpectJsonAsync(saved, 200);
                    Require(result.GetProperty("version").GetInt64() == 1 &&
                        result.GetProperty("article").GetProperty("title").GetString() == editedArticle.title &&
                        result.GetProperty("article").GetProperty("created_at").GetRawText() ==
                        published.GetProperty("created_at").GetRawText(),
                        "News edit did not commit the expected safe projection.");
                }
                using (var stale = Mutation(editPath, JsonContent.Create(new { expected_version = 0, article = editedArticle })))
                {
                    stale.Method = HttpMethod.Put;
                    stale.Headers.Add("Cookie", login.Cookie);
                    using var denied = await client.SendAsync(stale);
                    await ExpectJsonAsync(denied, 409, "news_version_conflict");
                }
                using (var duplicate = Mutation(editPath, new StringContent(
                    "{\"expected_version\":1,\"expected_version\":1,\"article\":{}}",
                    System.Text.Encoding.UTF8, "application/json")))
                {
                    duplicate.Method = HttpMethod.Put;
                    duplicate.Headers.Add("Cookie", login.Cookie);
                    using var denied = await client.SendAsync(duplicate);
                    await ExpectJsonAsync(denied, 422, "invalid_news_article");
                }

                using (var publicNews = await client.GetAsync("/api/public/news?organization_id=lefteria_fc"))
                {
                    var listing = await ExpectJsonAsync(publicNews, 200);
                    Require(listing.GetProperty("items").EnumerateArray().Any(item =>
                        item.GetProperty("id").GetString() == "news-shared" &&
                        item.GetProperty("title").GetString() == editedArticle.title),
                        "Public FC news feed failed to apply the edited safe projection.");
                }
                using (var academyPublic = await client.GetAsync("/api/public/news?organization_id=lefteria_fc_academy"))
                {
                    var listing = await ExpectJsonAsync(academyPublic, 200);
                    Require(listing.GetProperty("items").EnumerateArray().Any(item =>
                        item.GetProperty("id").GetString() == "news-shared" &&
                        item.GetProperty("title").GetString() == "Fixture club announcement"),
                        "Editing FC article changed the independent Academy visibility copy.");
                }
                using (var publicSite = await client.GetAsync("/api/public/site?organization_id=lefteria_fc"))
                {
                    // The admin-private AssertSafeJson helper intentionally
                    // rejects any "phone" key, but public club profiles may
                    // legitimately expose an approved public phone field.
                    // Keep the public-site status and private-source evidence
                    // checks instead of weakening the admin-private helper.
                    await ExpectStatusAsync(publicSite, 200);
                    var json = await publicSite.Content.ReadAsStringAsync();
                    Require(!json.Contains("private-news-fixture", StringComparison.Ordinal) &&
                        !json.Contains("bank_iban", StringComparison.Ordinal),
                        "Public website exposed imported private source evidence.");
                    using var document = JsonDocument.Parse(json);
                    var site = document.RootElement;
                    Require(site.GetProperty("news").EnumerateArray().Any(item =>
                        item.GetProperty("id").GetString() == "news-shared" &&
                        item.GetProperty("title").GetString() == editedArticle.title),
                        "Public homepage failed to apply the versioned article override.");
                }

                // Repeated edits must update version 1 -> 2 without inserting a
                // new imported article and without accepting a stale version.
                var revisedArticle = new
                {
                    title = "Second approved FC revision",
                    content = editedArticle.content,
                    excerpt = editedArticle.excerpt,
                    image_url = editedArticle.image_url,
                    category = editedArticle.category,
                    audience = editedArticle.audience,
                    is_featured = true
                };
                using (var next = Mutation(editPath, JsonContent.Create(new
                {
                    expected_version = 1,
                    article = revisedArticle
                })))
                {
                    next.Method = HttpMethod.Put;
                    next.Headers.Add("Cookie", login.Cookie);
                    using var saved = await client.SendAsync(next);
                    var result = await ExpectJsonAsync(saved, 200);
                    Require(result.GetProperty("version").GetInt64() == 2 &&
                        result.GetProperty("article").GetProperty("title").GetString() == revisedArticle.title,
                        "News editor failed to advance version 1 -> 2.");
                }
            }

            foreach (var path in new[] {
                "/api/admin/website/news",
                "/api/admin/website/news?organization_id=all",
                "/api/admin/website/news?organization_id=unknown"
            })
                using (var invalid = await GetAsync(client, path, login.Cookie))
                    await ExpectJsonAsync(invalid, 400, "invalid_organization");

            foreach (var path in new[] { NewsPath + "&offset=-1", NewsPath + "&limit=51" })
                using (var invalid = await GetAsync(client, path, login.Cookie))
                    await ExpectJsonAsync(invalid, 400, "invalid_filters");

            foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete })
            {
                using var request = Mutation(NewsPath, JsonContent.Create(new { }));
                request.Method = method;
                request.Headers.Add("Cookie", login.Cookie);
                using var denied = await client.SendAsync(request);
                await ExpectStatusAsync(denied, 405);
            }

            var coach = await database.SeedAsync(role: "coach", organizations: ["lefteria_fc"]);
            using (var coachClient = Client())
            {
                var coachLogin = await LoginAsync(coachClient, coach);
                using var denied = await GetAsync(coachClient, NewsPath, coachLogin.Cookie);
                await ExpectJsonAsync(denied, 403, "not_authorized");
            }
            var academyOnly = await database.SeedAsync(role: "editor", organizations: ["lefteria_fc_academy"]);
            using (var academyClient = Client())
            {
                var academyLogin = await LoginAsync(academyClient, academyOnly);
                using (var denied = await GetAsync(academyClient, NewsPath, academyLogin.Cookie))
                    await ExpectJsonAsync(denied, 403, "not_authorized");
                using (var allowed = await GetAsync(academyClient, AcademyNewsPath, academyLogin.Cookie))
                    await ExpectJsonAsync(allowed, 200);
            }

            await database.SetStateAsync(account, role: "coach");
            using (var denied = await GetAsync(client, NewsPath, login.Cookie))
                await ExpectJsonAsync(denied, 403, "not_authorized");
            await database.SetStateAsync(account, role: "manager");
            await database.RevokeOrganizationAsync(account, "lefteria_fc");
            using (var denied = await GetAsync(client, NewsPath, login.Cookie))
                await ExpectJsonAsync(denied, 403, "not_authorized");
            using (var academy = await GetAsync(client, AcademyNewsPath, login.Cookie))
                await ExpectJsonAsync(academy, 200);
            await database.ExpireSessionAsync(account, login.Token);
            using (var denied = await GetAsync(client, AcademyNewsPath, login.Cookie))
                await ExpectJsonAsync(denied, 401, "not_authenticated");
        }
        finally { await database.CleanupNewsFixtureAsync(fixture); }
    }
}
