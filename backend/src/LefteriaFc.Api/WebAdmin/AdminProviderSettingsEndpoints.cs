using LefteriaFc.Infrastructure.Identity;
using LefteriaFc.Infrastructure.WebAdmin;
using LefteriaFc.Application.WebAdmin;
using Microsoft.Extensions.Options;

namespace LefteriaFc.Api.WebAdmin;

/// <summary>Read-only, super-admin-only diagnostics for SMS configuration.
/// Credentials, full sender numbers and tokens are never returned or logged.
/// Editing/rotation requires a separate audited secret-management workflow.</summary>
public static class AdminProviderSettingsEndpoints
{
    public static IEndpointRouteBuilder MapAdminProviderSettings(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/settings/providers/twilio", async (
            HttpContext context, WebAdminService auth,
            IOptions<TwilioSmsOptions> options, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl="private, no-store";
            context.Response.Headers.Pragma="no-cache";
            var session=await auth.CurrentAsync(
                context.Request.Cookies[WebAdminServices.CookieName],ct);
            if(session is null)
                return Results.Json(new WebAdminError("Sign in again.","not_authenticated"),statusCode:401);
            if(session.User.Role!="super_admin")
                return Results.Json(new WebAdminError("Super Admin access required.","not_authorized"),statusCode:403);
            var twilio=options.Value;
            var sidOk=twilio.AccountSid.Length==34 &&
                twilio.AccountSid.StartsWith("AC",StringComparison.Ordinal) &&
                twilio.AccountSid.Skip(2).All(Uri.IsHexDigit);
            var tokenOk=!string.IsNullOrWhiteSpace(twilio.AuthToken);
            var fromOk=!string.IsNullOrWhiteSpace(twilio.FromNumber);
            return Results.Json(new {
                provider="twilio",
                credential_source="operator_configuration",
                account_sid_configured=sidOk,
                auth_token_configured=tokenOk,
                from_number_configured=fromOk,
                role_senders_configured=twilio.RoleFromNumbers.Count(x=>!string.IsNullOrWhiteSpace(x.Value)),
                ready=sidOk&&tokenOk&&fromOk,
                editing_available=false,
                test_send_available=false
            });
        })
        .RequireAuthorization(WebAdminServices.Policy)
        .WithName("ReadAdminTwilioSettingsStatus")
        .WithTags("Web administration")
        .ExcludeFromDescription();
        return endpoints;
    }
}
