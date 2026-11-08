using System.Net.Http.Json;
using System.Text.Json;
using LefteriaFc.Infrastructure.WebAdmin;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminHttpChecks
{
    private const string PosCatalogPath = "/api/admin/pos/catalog?organization_id=lefteria_fc";
    private const string AcademyPosCatalogPath = "/api/admin/pos/catalog?organization_id=lefteria_fc_academy";

    private async Task AdminPosCatalogHttpAsync()
    {
        using var anonymous = Client();
        using (var denied = await anonymous.GetAsync(PosCatalogPath))
            await ExpectJsonAsync(denied, 401, "not_authenticated");

        using (var bearer = new HttpRequestMessage(HttpMethod.Get, PosCatalogPath))
        {
            bearer.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "not-admin");
            using var denied = await anonymous.SendAsync(bearer);
            await ExpectJsonAsync(denied, 401, "not_authenticated");
        }

        var account = await database.SeedAsync(organizations: ["lefteria_fc", "lefteria_fc_academy"]);
        using var client = Client();
        var login = await LoginAsync(client, account);
        using (var empty = await GetAsync(client, PosCatalogPath, login.Cookie))
        {
            var data = await ExpectJsonAsync(empty, 200);
            Require(data.GetProperty("categories").GetArrayLength() == 0 &&
                data.GetProperty("products").GetArrayLength() == 0,
                "Empty catalog must not use synthetic POS data or imply checkout.");
        }

        // This remains forbidden despite allowing one secure function.
        using (var scope = factory.Services.CreateScope())
        await using (var connection = await scope.ServiceProvider.GetRequiredService<WebAdminConnections>().OpenAsync(CancellationToken.None))
        {
            Require(!await CanSelectPosAsync(connection), "Admin auth unexpectedly gained direct private POS SELECT.");
            var forbidden = false;
            try
            {
                await using var command = new NpgsqlCommand(
                    "SELECT pos.read_admin_catalog(repeat('0',64),'lefteria_fc')", connection);
                await command.ExecuteScalarAsync();
            }
            catch (PostgresException error) when (error.SqlState == "P0001") { forbidden = true; }
            Require(forbidden, "POS reader accepted a synthetic unbound session digest.");
        }

        var fixtureId = await database.SeedPosCatalogAsync();
        try
        {
            using (var fc = await GetAsync(client, PosCatalogPath, login.Cookie))
            {
                var data = await ExpectJsonAsync(fc, 200);
                Require(data.EnumerateObject().Count() == 3 &&
                    data.GetProperty("organization_id").GetString() == "lefteria_fc" &&
                    data.GetProperty("categories").GetArrayLength() == 1 &&
                    data.GetProperty("products").GetArrayLength() == 1 &&
                    data.GetProperty("categories")[0].GetProperty("id").GetString() == fixtureId &&
                    data.GetProperty("categories")[0].GetProperty("version").GetInt64() == 0 &&
                    !data.GetProperty("products")[0].TryGetProperty("organization_id", out _),
                    "Scoped POS response leaked unexpected data.");
            }
            using (var fc = await GetAsync(client, PosCatalogPath, login.Cookie))
            {
                var data = await ExpectJsonAsync(fc, 200);
                var product = data.GetProperty("products")[0];
                Require(product.GetProperty("name").GetString() == "FC fixture item" &&
                    product.GetProperty("variants").GetArrayLength() == 1 &&
                    product.GetProperty("variants")[0].GetProperty("price_cents").GetInt32() == 150 &&
                    product.GetProperty("variants")[0].GetProperty("cost_cents").GetInt32() == 40 &&
                    !data.GetRawText().Contains("Academy fixture item", StringComparison.Ordinal),
                    "FC private POS catalog did not reflect persisted price/cost or leaked Academy.");
            }
            using (var academy = await GetAsync(client, AcademyPosCatalogPath, login.Cookie))
            {
                var data = await ExpectJsonAsync(academy, 200);
                Require(data.GetProperty("organization_id").GetString() == "lefteria_fc_academy" &&
                    data.GetProperty("products")[0].GetProperty("name").GetString() == "Academy fixture item" &&
                    !data.GetRawText().Contains("FC fixture item", StringComparison.Ordinal),
                    "Academy POS catalog crossed owner boundaries.");
            }

            foreach (var path in new[] {
                "/api/admin/pos/catalog",
                "/api/admin/pos/catalog?organization_id=all",
                "/api/admin/pos/catalog?organization_id=unknown"
            })
                using (var denied = await GetAsync(client, path, login.Cookie))
                    await ExpectJsonAsync(denied, 400, "invalid_organization");

            foreach (var verb in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete })
            {
                using var request = Mutation(PosCatalogPath, JsonContent.Create(new { }));
                request.Method = verb;
                request.Headers.Add("Cookie", login.Cookie);
                using var denied = await client.SendAsync(request);
                await ExpectStatusAsync(denied, 405);
            }

            var coach = await database.SeedAsync(role: "coach", organizations: ["lefteria_fc"]);
            using (var coachClient = Client())
            {
                var coachLogin = await LoginAsync(coachClient, coach);
                using var denied = await GetAsync(coachClient, PosCatalogPath, coachLogin.Cookie);
                await ExpectJsonAsync(denied, 403, "not_authorized");
            }
            var academyOnly = await database.SeedAsync(organizations: ["lefteria_fc_academy"]);
            using (var academyClient = Client())
            {
                var academyLogin = await LoginAsync(academyClient, academyOnly);
                using (var denied = await GetAsync(academyClient, PosCatalogPath, academyLogin.Cookie))
                    await ExpectJsonAsync(denied, 403, "not_authorized");
                using (var allowed = await GetAsync(academyClient, AcademyPosCatalogPath, academyLogin.Cookie))
                    await ExpectJsonAsync(allowed, 200);
            }

            await database.SetStateAsync(account, role: "editor");
            using (var denied = await GetAsync(client, PosCatalogPath, login.Cookie))
                await ExpectJsonAsync(denied, 403, "not_authorized");
            await database.SetStateAsync(account, role: "manager");
            await database.RevokeOrganizationAsync(account, "lefteria_fc");
            using (var denied = await GetAsync(client, PosCatalogPath, login.Cookie))
                await ExpectJsonAsync(denied, 403, "not_authorized");
            using (var academy = await GetAsync(client, AcademyPosCatalogPath, login.Cookie))
                await ExpectJsonAsync(academy, 200);
            await database.ExpireSessionAsync(account, login.Token);
            using (var denied = await GetAsync(client, AcademyPosCatalogPath, login.Cookie))
                await ExpectJsonAsync(denied, 401, "not_authenticated");
        }
        finally { await database.CleanupPosCatalogAsync(fixtureId); }
    }

    private static async Task<bool> CanSelectPosAsync(NpgsqlConnection connection)
    {
        try
        {
            await using var command = new NpgsqlCommand("SELECT count(*) FROM pos.products", connection);
            await command.ExecuteScalarAsync();
            return true;
        }
        catch (PostgresException error) when (error.SqlState == "42501") { return false; }
    }
}
