using LefteriaFc.Api.Release;
using Microsoft.AspNetCore.Http;

namespace LefteriaFc.Identity.IntegrationTests;

internal static class AcademyMvpBoundaryChecks
{
    public static void Run()
    {
        Check.Equal(true,AcademyMvpBoundary.ValidMode(null),
            "Unset optional Academy MVP flag should preserve normal development.");
        Check.Equal(true,AcademyMvpBoundary.ValidMode("true"),
            "Exactly true must enable pilot protection.");
        Check.Equal(true,AcademyMvpBoundary.ValidMode("false"),
            "Exactly false must preserve the full development environment.");
        foreach (var invalid in new[]{"TRUE","False","1","yes","", "pilot"})
            Check.Equal(false,AcademyMvpBoundary.ValidMode(invalid),
                "Unreviewed optional-feature setting was silently accepted.");
        Check.Equal(true,AcademyMvpBoundary.Disabled("true"),
            "Exact opt-in was not recognized.");
        Check.Equal(false,AcademyMvpBoundary.Disabled("false"),
            "Disabled pilot accidentally blocked normal development.");

        foreach (var path in new[]
        {
            "/api/admin/pos","/api/admin/pos/sites",
            "/api/admin/pos/cashier/registers",
            "/api/admin/finance/payabl/payment-requests",
            "/api/admin/finance/payabl/payment-requests/any",
            "/api/payabl/webhooks/00000000-0000-0000-0000-000000000001/demo",
            "/api/mobile/hub/fan-subscription",
            "/api/mobile/hub/fan-subscription/plans",
            "/api/mobile/hub/fan-subscription/checkout",
            "/api/mobile/hub/fan-learning/next",
            "/api/admin/finance/payabl/training-fees/00000000-0000-0000-0000-000000000001/sms"
        })
            Check.Equal(true,AcademyMvpBoundary.IsOptionalEndpoint(new PathString(path)),
                "Optional unverified paid/checkout endpoint stayed enabled: "+path);

        foreach (var path in new[]
        {
            "/api/health/live","/api/health/ready","/api/mobile/family",
            "/api/mobile/finance/training-fees",
            "/api/admin/finance/payabl/status",
            "/api/admin/finance/payabl/training-fees",
            "/api/admin/finance/payabl/training-fees/sample",
            "/api/admin/finance/payabl/invoices",
            "/api/admin/football/audiences",
            "/api/admin/football/groups/with-profile",
            "/api/admin/academy/attendance",
            "/api/mobile/hub/player-learning/next",
            "/api/admin/possessions","/api/mobile/hub/fan-subscriptions",
            "/api/payable","/api/admin/finance/payabl/payment-requests-other"
        })
            Check.Equal(false,AcademyMvpBoundary.IsOptionalEndpoint(new PathString(path)),
                "Core Academy workflow or nonmatching endpoint was blocked: "+path);
    }
}
