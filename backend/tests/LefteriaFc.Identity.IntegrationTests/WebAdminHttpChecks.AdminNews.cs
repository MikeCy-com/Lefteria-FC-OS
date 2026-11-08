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
