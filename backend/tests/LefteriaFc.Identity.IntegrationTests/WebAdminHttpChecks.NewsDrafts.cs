using System.Net.Http.Json;
using System.Text.Json;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminHttpChecks
{
    private const string NewsDraftsPath = "/api/admin/website/news-drafts?organization_id=lefteria_fc";
    private const string AcademyNewsDraftsPath = "/api/admin/website/news-drafts?organization_id=lefteria_fc_academy";

    private async Task WebsiteNewsDraftWorkspaceHttpAsync()
    {
        using var anonymous = Client();
        using (var denied = await anonymous.GetAsync(NewsDraftsPath))
            await ExpectJsonAsync(denied, 401, "not_authenticated");

        var account = await database.SeedAsync(organizations: ["lefteria_fc", "lefteria_fc_academy"]);
        using var client = Client();
        var login = await LoginAsync(client, account);
        var fixture = await database.ImportNewsFixtureAsync();
        string? createdId = null;
        try
        {
            using (var empty = await GetAsync(client, NewsDraftsPath, login.Cookie))
            {
                var body = await ExpectJsonAsync(empty, 200);
                Require(body.GetProperty("organization_id").GetString() == "lefteria_fc" &&
                    body.GetProperty("drafts").GetArrayLength() == 0,
                    "Private draft listing did not start empty for Club.");
            }

            var article = new
            {
                title = "Unpublished fictional draft",
                content = "Work in progress - not public",
                excerpt = "",
                image_url = (string?)null,
                category = "Νέα",
                audience = "both",
                is_featured = false
            };

            using (var request = Mutation(NewsDraftsPath, JsonContent.Create(new { article })))
            {
                request.Headers.Add("Cookie", login.Cookie);
                using var response = await client.SendAsync(request);
                var body = await ExpectJsonAsync(response, 200);
                createdId = body.GetProperty("id").GetString();
                Require(createdId is {Length:38} && createdId.StartsWith("draft_", StringComparison.Ordinal) &&
                    body.GetProperty("organization_id").GetString() == "lefteria_fc" &&
                    body.GetProperty("status").GetString() == "draft" &&
                    body.GetProperty("version").GetInt64() == 1 &&
                    body.GetProperty("article").GetProperty("title").GetString() == article.title,
                    "New CMS draft must be private, version one and owner-bound.");
            }

            using (var listed = await GetAsync(client, NewsDraftsPath, login.Cookie))
            {
                var body = await ExpectJsonAsync(listed, 200);
                Require(body.GetProperty("drafts").GetArrayLength() == 1 &&
                    body.GetProperty("drafts")[0].GetProperty("id").GetString() == createdId,
                    "Saved draft could not be read through the scoped private API.");
            }
            using (var other = await GetAsync(client, AcademyNewsDraftsPath, login.Cookie))
            {
                var body = await ExpectJsonAsync(other, 200);
                Require(body.GetProperty("drafts").GetArrayLength() == 0,
                    "Club-only private draft leaked into Academy listing.");
            }

            using (var stillPublic = await client.GetAsync("/api/public/news?organization_id=lefteria_fc"))
            {
                var body = await ExpectJsonAsync(stillPublic, 200);
                Require(body.GetProperty("total").GetInt32() == 2 &&
                    !body.GetRawText().Contains(article.title, StringComparison.Ordinal),
                    "Unpublished draft appeared on the public website.");
            }

            var updatePath = "/api/admin/website/news-drafts/" + createdId + "?organization_id=lefteria_fc";
            var revision = new
            {
                title = "Revised fictional private draft",
                content = "Still unpublished after revision",
                excerpt = "Draft synopsis",
                image_url = (string?)null,
                category = "Ανακοινώσεις",
                audience = "both",
                is_featured = true
            };
            using (var request = Mutation(updatePath,
                JsonContent.Create(new { expected_version = 1, article = revision })))
            {
                request.Method = HttpMethod.Put;
                request.Headers.Add("Cookie", login.Cookie);
                using var response = await client.SendAsync(request);
                var body = await ExpectJsonAsync(response, 200);
                Require(body.GetProperty("version").GetInt64() == 2 &&
                    body.GetProperty("article").GetProperty("title").GetString() == revision.title,
                    "Updating existing draft failed optimistic version increment.");
            }
            using (var request = Mutation(updatePath,
                JsonContent.Create(new { expected_version = 1, article = revision })))
            {
                request.Method = HttpMethod.Put;
                request.Headers.Add("Cookie", login.Cookie);
                using var response = await client.SendAsync(request);
                await ExpectJsonAsync(response, 409, "news_draft_conflict");
            }
            using (var request = Mutation(updatePath, new StringContent(
                "{\"expected_version\":2,\"expected_version\":2,\"article\":{}}",
                System.Text.Encoding.UTF8, "application/json")))
            {
                request.Method = HttpMethod.Put;
                request.Headers.Add("Cookie", login.Cookie);
                using var response = await client.SendAsync(request);
                await ExpectJsonAsync(response, 422, "invalid_news_draft");
            }

            var academyOnly = await database.SeedAsync(role: "editor",
                organizations: ["lefteria_fc_academy"]);
            using (var otherClient = Client())
            {
                var otherLogin = await LoginAsync(otherClient, academyOnly);
                using (var denied = await GetAsync(otherClient, NewsDraftsPath, otherLogin.Cookie))
                    await ExpectJsonAsync(denied, 403, "organization_denied");
                using (var allowed = await GetAsync(otherClient, AcademyNewsDraftsPath, otherLogin.Cookie))
                    await ExpectJsonAsync(allowed, 200);
            }
            await database.SetStateAsync(account, role: "coach");
            using (var denied = await GetAsync(client, NewsDraftsPath, login.Cookie))
                await ExpectJsonAsync(denied, 403, "news_permission_denied");
            await database.SetStateAsync(account, role: "manager");
            await database.RevokeOrganizationAsync(account, "lefteria_fc");
            using (var denied = await GetAsync(client, NewsDraftsPath, login.Cookie))
                await ExpectJsonAsync(denied, 403, "organization_denied");
        }
        finally
        {
            if (createdId is not null)
                await database.DeletePrivateNewsDraftAsync("lefteria_fc", createdId);
            await database.CleanupNewsFixtureAsync(fixture);
        }
    }
}
