using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using Microsoft.AspNetCore.Http.Features;

namespace LefteriaFc.Api.Identity;

public sealed record MobileAuthError(
    [property: JsonPropertyName("detail")] string Detail,
    [property: JsonPropertyName("code")] string Code);
public sealed record OtpRequestBody(string? Phone);
public sealed record FanRegistrationBody(string? Phone,
    [property: JsonPropertyName("first_name")] string? FirstName = null,
    [property: JsonPropertyName("last_name")] string? LastName = null, string? Name = null, string? Birthday = null);
public sealed record RoleSwitchBody(string? Role);
public sealed record MobileProfilePreferenceBody
{
    [JsonPropertyName("favorite_team")]
    public JsonElement FavoriteTeam { get; init; }
}
public sealed record OrganizationSelectionBody([property: JsonPropertyName("organization_id")] string? OrganizationId);
public sealed record OtpVerificationBody
{
    public string? Phone { get; init; }
    public string? Code { get; init; }
    public string? Role { get; init; }
    [JsonPropertyName("requested_role")]
    public string? RequestedRole { get; init; }
    [JsonPropertyName("remember_days")]
    public JsonElement RememberDays { get; init; }
}

public static class MobileIdentityEndpoints
{
    public static IApplicationBuilder UseMobileRequestGuards(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/api/mobile"))
        {
            context.Response.Headers.CacheControl = "no-store";
            if (context.Request.Path.StartsWithSegments("/api/mobile/auth") ||
                string.Equals(context.Request.Path.Value?.TrimEnd('/'), "/api/mobile/profile", StringComparison.OrdinalIgnoreCase))
            {
                const long limit = 16 * 1024;
                if (context.Request.ContentLength > limit)
                {
                    context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    return;
                }
                if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature)
                    feature.MaxRequestBodySize = limit;
            }
        }
        await next(context);
    });

    public static IEndpointRouteBuilder MapMobileIdentity(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/api/mobile/auth").WithTags("Mobile identity");
        auth.MapPost("/request-otp", (OtpRequestBody body, MobileAuthService service, HttpContext context, CancellationToken token) =>
                Execute(() => service.RequestOtpAsync(body.Phone, RequestContext(context), token), context))
            .WithName("RequestMobileOtp").Produces<OtpRequestResult>().WithMobileErrors();
        auth.MapPost("/register-fan", (FanRegistrationBody body, MobileAuthService service, HttpContext context, CancellationToken token) =>
                Execute(() => service.RegisterFanAsync(new(body.Phone, body.FirstName, body.LastName, body.Name, body.Birthday), RequestContext(context), token), context))
            .WithName("RegisterMobileFan").Produces<FanRegistrationResult>().WithMobileErrors();
        auth.MapPost("/verify-otp", (OtpVerificationBody body, MobileAuthService service, HttpContext context, CancellationToken token) =>
                Execute(() => service.VerifyOtpAsync(new(body.Phone, body.Code, body.Role, body.RequestedRole,
                    ParseRemember(body.RememberDays)), RequestContext(context), token), context))
            .WithName("VerifyMobileOtp").Produces<MobileVerificationResult>().WithMobileErrors();
        auth.MapPost("/switch-role", (RoleSwitchBody body, MobileAuthService service, HttpContext context, CancellationToken token) =>
                Execute(() => service.SwitchRoleAsync(MobilePrincipal.Read(context.User), body.Role, RequestContext(context), token), context))
            .RequireAuthorization(IdentityServices.Policy).WithName("SwitchMobileRole").Produces<MobileRoleSwitchResult>().WithMobileErrors();
        auth.MapGet("/me", (MobileAuthService service, HttpContext context, CancellationToken token) =>
                Execute(() => service.GetMeAsync(MobilePrincipal.Read(context.User), token), context))
            .RequireAuthorization(IdentityServices.Policy).WithName("GetMobileIdentity").Produces<MobileUserDto>().WithMobileErrors();
        auth.MapPut("/me", (MobileProfilePreferenceBody body, MobileAuthService service, HttpContext context, CancellationToken token) =>
                Execute(() => service.UpdateFavoriteTeamAsync(MobilePrincipal.Read(context.User),
                    body.FavoriteTeam.ValueKind != JsonValueKind.Undefined, ParseFavoriteTeam(body.FavoriteTeam), token), context))
            .RequireAuthorization(IdentityServices.Policy).WithName("UpdateMobileProfilePreference")
            .Produces<MobileProfilePreferenceResult>().WithMobileErrors();

        endpoints.MapPut("/api/mobile/profile", (MobileProfileUpdateBody body, MobileAuthService service, HttpContext context, CancellationToken token) =>
                Execute(() => service.UpdateProfileAsync(MobilePrincipal.Read(context.User), body.ToCommand(), token), context))
            .RequireAuthorization(IdentityServices.Policy).WithTags("Mobile profile")
            .WithName("UpdateMobileProfile").Produces<MobileUserDto>()
            .Produces(StatusCodes.Status413PayloadTooLarge).WithMobileErrors();

        var management = endpoints.MapGroup("/api/mobile/management").WithTags("Mobile organization")
            .RequireAuthorization(IdentityServices.Policy);
        management.MapGet("/organization", (MobileAuthService service, HttpContext context, CancellationToken token) =>
                Execute(() => service.GetOrganizationAsync(MobilePrincipal.Read(context.User), token), context))
            .WithName("GetMobileOrganization").Produces<MobileOrganizationContextDto>().WithMobileErrors();
        management.MapPost("/organization", (OrganizationSelectionBody body, MobileAuthService service, HttpContext context, CancellationToken token) =>
                Execute(() => service.SelectOrganizationAsync(MobilePrincipal.Read(context.User), body.OrganizationId, token), context))
            .WithName("SelectMobileOrganization").Produces<MobileOrganizationContextDto>().WithMobileErrors();
        return endpoints;
    }

    public static RememberDeviceSelection ParseRemember(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Undefined => new(false, null),
        JsonValueKind.Null => new(true, null),
        JsonValueKind.Number when value.GetRawText() is "30" or "60" or "90" => new(true, value.GetInt32()),
        _ => throw new MobileAuthException(MobileAuthFaultKind.Validation, MobileAuthErrorCode.InvalidRememberSelection,
            "Επιλέξτε 30, 60 ή 90 ημέρες.")
    };

    private static string? ParseFavoriteTeam(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null => null,
        JsonValueKind.String => value.GetString(),
        _ => throw new MobileAuthException(MobileAuthFaultKind.Validation, MobileAuthErrorCode.InvalidTeamPreference,
            "Επιλέξτε πρώτη ομάδα, ακαδημία ή και τις δύο.")
    };

    private static async Task<IResult> Execute<T>(Func<Task<T>> action, HttpContext context)
    {
        try { return TypedResults.Ok(await action()); }
        catch (MobileAuthException exception)
        {
            if (exception.RetryAfterSeconds is { } retry)
                context.Response.Headers.RetryAfter = retry.ToString(CultureInfo.InvariantCulture);
            return TypedResults.Json(new MobileAuthError(exception.Detail,
                JsonNamingPolicy.SnakeCaseLower.ConvertName(exception.Code.ToString())), statusCode: exception.Kind switch
                {
                    MobileAuthFaultKind.BadRequest => 400, MobileAuthFaultKind.Unauthorized => 401,
                    MobileAuthFaultKind.Forbidden => 403, MobileAuthFaultKind.NotFound => 404,
                    MobileAuthFaultKind.Validation => 422, MobileAuthFaultKind.TooManyRequests => 429,
                    _ => 503
                });
        }
        catch (MobileSessionAccessException)
        {
            return TypedResults.Json(new MobileAuthError("Token expired", "session_expired"), statusCode: 401);
        }
    }

    private static RouteHandlerBuilder WithMobileErrors(this RouteHandlerBuilder endpoint)
    {
        foreach (var code in new[] { 400, 401, 403, 404, 422, 429, 503 }) endpoint.Produces<MobileAuthError>(code);
        return endpoint;
    }

    private static MobileAuthRequestContext RequestContext(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        var client = address is null ? "unknown" : address.IsIPv4MappedToIPv6 ? address.MapToIPv4().ToString() : address.ToString();
        var agent = context.Request.Headers.UserAgent.ToString();
        var platform = agent.Contains("iPhone", StringComparison.OrdinalIgnoreCase) || agent.Contains("iPad", StringComparison.OrdinalIgnoreCase)
            ? "iOS" : agent.Contains("Android", StringComparison.OrdinalIgnoreCase) ? "Android" : "Web/PWA";
        return new(client, platform);
    }
}
