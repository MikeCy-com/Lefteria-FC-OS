using System.Net;
using System.Text.Json;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    private async Task FirstTeamTrialSettingsClosedAsync()
    {
        using var client=factory.CreateClient(new() { AllowAutoRedirect=false });
        using var response=await client.GetAsync("/api/trials/settings");
        Check.Equal(HttpStatusCode.OK,response.StatusCode,
            "First-team trial settings GET did not return source closed defaults.");
        Check.Equal("no-store",response.Headers.CacheControl?.ToString(),
            "Public trial status must not be cached while submissions are unavailable.");
        using var body=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var json=body.RootElement;
        Check.True(!json.GetProperty("open").GetBoolean(),
            "Unverified first-team registration unexpectedly opened.");
        Check.Equal("Ετοιμος να Ξεκινησεις το Ταξιδι σου;",
            json.GetProperty("headline").GetString(),
            "First-team trial source heading differs.");
        Check.True(json.GetProperty("subtitle").GetString()?.Contains("LEFTERIA FC",StringComparison.Ordinal)==true,
            "Original first-team subtitle missing.");
        Check.Equal("Δηλωσε Συμμετοχη για Δοκιμαστικα",
            json.GetProperty("button_text").GetString(),
            "Original trial application CTA differs.");
        Check.True(json.GetProperty("closed_message").GetString()?.Contains("προσωρινά κλειστές",StringComparison.Ordinal)==true,
            "Original closed message missing.");
        Check.True(json.EnumerateObject().Count()==5,
            "Public settings exposed an unexpected private or admin field.");

        using var submit=await client.PostAsync("/api/trials/settings",new StringContent("{}"));
        Check.Equal(HttpStatusCode.MethodNotAllowed,submit.StatusCode,
            "Public settings endpoint unexpectedly accepted a write.");
        using var application=await client.PostAsync("/api/trials/submit",new StringContent("{}"));
        Check.True(application.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
            "Private athlete application endpoint unexpectedly activated.");
    }
}
