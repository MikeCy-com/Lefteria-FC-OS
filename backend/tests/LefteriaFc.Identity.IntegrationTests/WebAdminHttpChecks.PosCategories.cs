using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminHttpChecks
{
    private const string CategoriesPath = "/api/admin/pos/categories?organization_id=lefteria_fc";
    private const string AcademyCategoriesPath = "/api/admin/pos/categories?organization_id=lefteria_fc_academy";

    private static async Task<HttpResponseMessage> SavePosCategory(
        HttpClient client, string cookie, HttpMethod method, string path, object payload)
    {
        using var request = Mutation(path, JsonContent.Create(payload));
        request.Method = method;
        request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request);
    }

    private async Task AdminPosCategoryWritesAsync()
    {
        using var anonymous = Client();
        using (var request = Mutation(CategoriesPath, JsonContent.Create(new { name = "Denied", active = true })))
        using (var denied = await anonymous.SendAsync(request))
            await ExpectJsonAsync(denied, 401, "not_authenticated");

        var fc = await database.SeedAsync(organizations: ["lefteria_fc", "lefteria_fc_academy"]);
        using var client = Client();
        var login = await LoginAsync(client, fc);

        var fixtureId = await database.SeedPosCatalogAsync();
        string? createdId = null;
        try
        {
            using (var create = await SavePosCategory(client, login.Cookie, HttpMethod.Post,
                CategoriesPath, new { name = "New FC category", active = true }))
            {
                var data = await ExpectJsonAsync(create, 200);
                createdId = data.GetProperty("id").GetString();
                Require(createdId is not null && createdId.StartsWith("cat_", StringComparison.Ordinal) &&
                    data.EnumerateObject().Count() == 4 &&
                    data.GetProperty("name").GetString() == "New FC category" &&
                    data.GetProperty("active").GetBoolean() &&
                    data.GetProperty("version").GetInt64() == 0,
                    "POS category create did not commit an exact bounded record.");
            }
            await database.AssertCategoryAsync("lefteria_fc", createdId!, "New FC category", true, 0);
            Require(await database.CategoryAuditCountAsync(fc) == 1,
                "Creating a POS category did not atomically record one audit.");

            var path = "/api/admin/pos/categories/" + createdId + "?organization_id=lefteria_fc";
            using (var stale = await SavePosCategory(client, login.Cookie, HttpMethod.Put,
                path, new { name = "Wrong version", active = true, expected_version = 5 }))
                await ExpectJsonAsync(stale, 409, "pos_category_version_conflict");
            using (var updated = await SavePosCategory(client, login.Cookie, HttpMethod.Put,
                path, new { name = "Updated FC category", active = false, expected_version = 0 }))
            {
                var data = await ExpectJsonAsync(updated, 200);
                Require(data.GetProperty("id").GetString() == createdId &&
                    data.GetProperty("version").GetInt64() == 1 &&
                    !data.GetProperty("active").GetBoolean(),
                    "POS category versioned update response is incorrect.");
            }
            await database.AssertCategoryAsync("lefteria_fc", createdId!, "Updated FC category", false, 1);
            Require(await database.CategoryAuditCountAsync(fc) == 2,
                "Only one category update should append an audit.");

            using (var repeated = await SavePosCategory(client, login.Cookie, HttpMethod.Put,
                path, new { name = "Replay forbidden", active = true, expected_version = 0 }))
                await ExpectJsonAsync(repeated, 409, "pos_category_version_conflict");

            using (var catalog = await GetAsync(client, PosCatalogPath, login.Cookie))
            {
                var data = await ExpectJsonAsync(catalog, 200);
                Require(data.GetProperty("categories").GetArrayLength() == 2 &&
                    data.GetProperty("categories").EnumerateArray().Any(c =>
                        c.GetProperty("id").GetString() == createdId &&
                        c.GetProperty("name").GetString() == "Updated FC category" &&
                        c.GetProperty("version").GetInt64() == 1),
                    "Private catalog read did not show the committed category.");
            }
            using (var academyCatalog = await GetAsync(client, AcademyPosCatalogPath, login.Cookie))
            {
                var data = await ExpectJsonAsync(academyCatalog, 200);
                Require(data.GetProperty("categories").GetArrayLength() == 1 &&
                    !data.GetRawText().Contains("Updated FC category", StringComparison.Ordinal),
                    "FC write crossed the Academy data boundary.");
            }

            await database.WithPosAuditFailureAsync(fc, async () =>
            {
                using var denied = await SavePosCategory(client, login.Cookie, HttpMethod.Put,
                    path, new { name = "Audit must roll back", active = true, expected_version = 1 });
                await ExpectJsonAsync(denied, 503);
            });
            await database.AssertCategoryAsync("lefteria_fc", createdId!, "Updated FC category", false, 1);
            Require(await database.CategoryAuditCountAsync(fc) == 2,
                "Failed audit inserted a POS event or left an uncommitted edit.");

            using (var wrong = Mutation(CategoriesPath, new StringContent(
                "{\"name\":\"A\",\"name\":\"B\",\"active\":true}", Encoding.UTF8, "application/json")))
            {
                wrong.Headers.Add("Cookie", login.Cookie);
                using var denied = await client.SendAsync(wrong);
                await ExpectJsonAsync(denied, 400, "invalid_json");
            }
            foreach (var payload in new object[] {
                new { name = "Unknown property", active = true, organization_id = "lefteria_fc_academy" },
                new { name = "Unknown property", active = true, secret = "must reject" }
            })
                using (var denied = await SavePosCategory(client, login.Cookie, HttpMethod.Post, CategoriesPath, payload))
                    await ExpectJsonAsync(denied, 400, "invalid_json");
            foreach (var payload in new object[] {
                new { name = "No active flag" },
                new { name = "  padded ", active = true },
                new { name = "", active = true }
            })
                using (var denied = await SavePosCategory(client, login.Cookie, HttpMethod.Post, CategoriesPath, payload))
                    await ExpectJsonAsync(denied, 422, "invalid_category");
            using (var wrongOrigin = Mutation(CategoriesPath, JsonContent.Create(new { name = "Denied", active = true })))
            {
                wrongOrigin.Headers.Remove("Origin");
                wrongOrigin.Headers.Add("Origin", "https://untrusted.invalid");
                wrongOrigin.Headers.Add("Cookie", login.Cookie);
                using var denied = await client.SendAsync(wrongOrigin);
                await ExpectJsonAsync(denied, 403, "request_origin_denied");
            }
            using (var badVersion = Mutation(path, new StringContent(
                "{\"name\":\"Denied\",\"active\":true,\"expected_version\":\"1\"}",
                Encoding.UTF8, "application/json")))
            {
                badVersion.Method = HttpMethod.Put; badVersion.Headers.Add("Cookie", login.Cookie);
                using var denied = await client.SendAsync(badVersion);
                await ExpectJsonAsync(denied, 400, "invalid_json");
            }
            Require(await database.CategoryAuditCountAsync(fc) == 2,
                "Invalid or untrusted POST/PUT created an audit event.");

            var editor = await database.SeedAsync(role: "editor", organizations: ["lefteria_fc"]);
            using (var editorClient = Client())
            {
                var editorLogin = await LoginAsync(editorClient, editor);
                using var denied = await SavePosCategory(editorClient, editorLogin.Cookie,
                    HttpMethod.Put, path, new { name = "Editor forbidden", active = true, expected_version = 1 });
                await ExpectJsonAsync(denied, 403, "not_authorized");
            }
            var academyOnly = await database.SeedAsync(organizations: ["lefteria_fc_academy"]);
            using (var academyClient = Client())
            {
                var academyLogin = await LoginAsync(academyClient, academyOnly);
                using (var denied = await SavePosCategory(academyClient, academyLogin.Cookie, HttpMethod.Put,
                    path, new { name = "Cross owner", active = true, expected_version = 1 }))
                    await ExpectJsonAsync(denied, 403, "not_authorized");
                using (var academySaved = await SavePosCategory(academyClient, academyLogin.Cookie, HttpMethod.Put,
                    "/api/admin/pos/categories/" + fixtureId + "?organization_id=lefteria_fc_academy",
                    new { name = "Academy owner update", active = true, expected_version = 0 }))
                    await ExpectJsonAsync(academySaved, 200);
            }
            await database.AssertCategoryAsync("lefteria_fc_academy", fixtureId, "Academy owner update", true, 1);
            await database.AssertCategoryAsync("lefteria_fc", fixtureId, "FC catalog category", true, 0);

            await database.RevokeOrganizationAsync(fc, "lefteria_fc");
            using (var denied = await SavePosCategory(client, login.Cookie, HttpMethod.Put,
                path, new { name = "Revoked", active = true, expected_version = 1 }))
                await ExpectJsonAsync(denied, 403, "not_authorized");
            await database.ExpireSessionAsync(fc, login.Token);
            using (var denied = await SavePosCategory(client, login.Cookie, HttpMethod.Put,
                "/api/admin/pos/categories/" + fixtureId + "?organization_id=lefteria_fc_academy",
                new { name = "Expired", active = true, expected_version = 1 }))
                await ExpectJsonAsync(denied, 401, "not_authenticated");
            await database.AssertCategoryAsync("lefteria_fc", createdId!, "Updated FC category", false, 1);
        }
        finally
        {
            if (createdId is not null) await database.CleanupCreatedCategoryAsync("lefteria_fc", createdId);
            await database.CleanupPosCatalogAsync(fixtureId);
        }
    }
}
