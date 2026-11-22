using System.Net;
using System.Net.Http.Json;

namespace LefteriaFc.Identity.IntegrationTests;

/// <summary>
/// Real HTTP pipeline proof, on the existing disposable identity/PostgreSQL
/// fixture only. No provider, hosting or customer network operations.
/// </summary>
internal static class AcademyMvpHttpChecks
{
    public static async Task RunAsync(FixtureConfiguration fixture)
    {
        using var factory = new IdentityApplicationFactory(fixture,academyMvpMode:true);
        using var client=factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Identity-Test-Client","22");

        foreach(var path in new[]{
            "/api/admin/pos/sites?organization_id=lefteria_fc_academy",
            "/api/admin/finance/payabl/payment-requests?organization_id=lefteria_fc_academy",
            "/api/mobile/hub/fan-subscription/plans",
            "/api/mobile/hub/fan-learning/next",
            "/api/admin/finance/payabl/training-fees/00000000-0000-0000-0000-000000000001/sms",
            "/api/payabl/webhooks/00000000-0000-0000-0000-000000000001/unused"
        })
        {
            using var response=await client.GetAsync(path);
            await ExpectDisabledAsync(response,path);
        }

        // A POST cannot slip past the guard even if anonymous or incomplete.
        foreach(var path in new[]{
            "/api/admin/pos/cashier/registers/test-register/shifts",
            "/api/mobile/hub/fan-subscription/checkout",
            "/api/admin/finance/payabl/payment-requests?organization_id=lefteria_fc_academy"
        })
        {
            using var response=await client.PostAsJsonAsync(path,new{});
            await ExpectDisabledAsync(response,path);
        }

        // Identity and core Academy finance are intentionally not disabled.
        using(var live=await client.GetAsync("/api/health/live"))
            Check.Equal(HttpStatusCode.OK,live.StatusCode,
                "Pilot feature guard broke health liveness.");
        foreach(var path in new[]{
            "/api/mobile/family",
            "/api/mobile/finance/training-fees",
            "/api/admin/finance/payabl/training-fees?organization_id=lefteria_fc_academy",
            "/api/admin/football/audiences?organization_id=lefteria_fc_academy"
        })
        {
            using var response=await client.GetAsync(path);
            Check.Equal(false,response.StatusCode==HttpStatusCode.ServiceUnavailable,
                "Pilot feature guard disabled an Academy core read: "+path);
            Check.Equal(false,response.IsSuccessStatusCode,
                "Anonymous client received private Academy data: "+path);
        }
    }

    private static async Task ExpectDisabledAsync(HttpResponseMessage response,string path)
    {
        Check.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode,
            "Optional action was not blocked by the MVP feature guard: "+path+
                " (HTTP "+(int)response.StatusCode+")");
        Check.Equal(true,response.Headers.CacheControl?.NoStore==true,
            "Disabled optional endpoint was cacheable: "+path);
        using var json=System.Text.Json.JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        Check.Equal("mvp_feature_disabled",json.RootElement.GetProperty("code").GetString(),
            "Expected exact MVP-disabled response, not an upstream outage or auth fallback: "+path);
    }
}
