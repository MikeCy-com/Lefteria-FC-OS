namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminHttpChecks
{
    private async Task EnabledAdminReadinessAsync()
    {
        // The configured admin-auth runtime is a separate credential/role
        // from the mobile DB user. Anonymous health is safe and carries no
        // role, organization, migration revision or secret details.
        using var client=Client();
        using var response=await client.GetAsync("/api/health/ready");
        var body=await ExpectJsonAsync(response,200);
        Require(body.GetProperty("status").GetString()=="ready",
            "Enabled web-admin runtime did not pass operational privilege verification.");
        Require(response.Headers.CacheControl?.NoStore==true,
            "Operational readiness can be incorrectly cached by a deployment edge.");
        Require(!body.TryGetProperty("role",out _) &&
                !body.TryGetProperty("migration",out _) &&
                !body.TryGetProperty("database",out _),
                "Anonymous health disclosed role/schema/database internals.");
    }
}
