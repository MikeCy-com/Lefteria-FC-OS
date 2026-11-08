using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminHttpChecks
{
    private const string TrialsAdminPath = "/api/admin/trials/settings?organization_id=lefteria_fc";

    private async Task FirstTeamTrialAdminHttpAsync()
    {
        using var anonymous = Client();
        using (var denied = await anonymous.GetAsync(TrialsAdminPath))
            await ExpectJsonAsync(denied, 401, "not_authenticated");
        using (var bearer = new HttpRequestMessage(HttpMethod.Get, TrialsAdminPath))
        {
            bearer.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-an-admin-cookie");
            using var denied = await anonymous.SendAsync(bearer);
            await ExpectJsonAsync(denied, 401, "not_authenticated");
        }

        var fc = await database.SeedAsync(organizations: ["lefteria_fc"]);
        using var admin = Client();
        var login = await LoginAsync(admin, fc);
        using (var response = await GetAsync(admin, TrialsAdminPath, login.Cookie))
        {
            var status = await ExpectJsonAsync(response, 200);
            Require(status.EnumerateObject().Count() == 5 &&
                status.GetProperty("open").GetBoolean() == false &&
                status.GetProperty("headline").GetString()?.Length > 0 &&
                status.GetProperty("subtitle").GetString()?.Length > 0 &&
                status.GetProperty("button_text").GetString()?.Length > 0 &&
                status.GetProperty("closed_message").GetString()?.Length > 0,
                "The admin response exposed an unexpected field or failed to provide verified closed status.");
        }

        foreach (var path in new[] {
            "/api/admin/trials/settings",
            "/api/admin/trials/settings?organization_id=lefteria_fc_academy",
            "/api/admin/trials/settings?organization_id=all"
        })
            using (var response = await GetAsync(admin, path, login.Cookie))
                await ExpectJsonAsync(response, 400, "invalid_organization");

        var academy = await database.SeedAsync(organizations: ["lefteria_fc_academy"]);
        using (var academyClient = Client())
        {
            var academyLogin = await LoginAsync(academyClient, academy);
            using var denied = await GetAsync(academyClient, TrialsAdminPath, academyLogin.Cookie);
            await ExpectJsonAsync(denied, 403, "not_authorized");
        }

        foreach (var verb in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete })
        {
            using var request = Mutation(TrialsAdminPath, JsonContent.Create(new { }));
            request.Method = verb;
            request.Headers.Add("Cookie", login.Cookie);
            using var response = await admin.SendAsync(request);
            await ExpectStatusAsync(response, 405);
        }

        await database.SetStateAsync(fc, role: "coach");
        using (var denied = await GetAsync(admin, TrialsAdminPath, login.Cookie))
            await ExpectJsonAsync(denied, 403, "not_authorized");

        await database.SetStateAsync(fc, role: "manager");
        await database.RevokeOrganizationAsync(fc, "lefteria_fc");
        using (var denied = await GetAsync(admin, TrialsAdminPath, login.Cookie))
            await ExpectJsonAsync(denied, 403, "not_authorized");

        await database.ExpireSessionAsync(fc, login.Token);
        using (var denied = await GetAsync(admin, TrialsAdminPath, login.Cookie))
            await ExpectJsonAsync(denied, 401, "not_authenticated");
    }
}
