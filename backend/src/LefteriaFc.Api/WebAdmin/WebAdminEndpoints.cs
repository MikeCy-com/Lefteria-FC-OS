using LefteriaFc.Application.WebAdmin;

namespace LefteriaFc.Api.WebAdmin;

public static class WebAdminEndpoints
{
    public static IEndpointRouteBuilder MapWebAdmin(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/auth").WithTags("Web administration");
        Errors(group.MapPost("/login", async (WebAdminLoginCommand command, WebAdminService service, HttpContext context, CancellationToken token) =>
        {
            var address = context.Connection.RemoteIpAddress;
            var client = address?.IsIPv4MappedToIPv6 == true ? address.MapToIPv4().ToString() : address?.ToString() ?? "unknown";
            var result = await service.LoginAsync(command, client, token);
            context.Response.Cookies.Append(WebAdminServices.CookieName, result.Token, new CookieOptions
            {
                Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/", IsEssential = true,
                Expires = result.Session.ExpiresAt, MaxAge = result.Session.ExpiresAt - DateTimeOffset.UtcNow
            });
            return TypedResults.Ok(result.Session);
        }).WithName("LoginWebAdmin").Produces<WebAdminSessionDto>());
        Errors(group.MapGet("/me", (HttpContext context) => TypedResults.Ok((WebAdminSessionDto)context.Items[typeof(WebAdminSessionDto)]!))
            .RequireAuthorization(WebAdminServices.Policy).WithName("GetWebAdminSession").Produces<WebAdminSessionDto>());
        Errors(group.MapPost("/logout", async (WebAdminService service, HttpContext context, CancellationToken token) =>
        {
            await service.LogoutAsync(context.Request.Cookies[WebAdminServices.CookieName], token);
            context.Response.Cookies.Delete(WebAdminServices.CookieName, new CookieOptions
            { Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/" });
            return TypedResults.NoContent();
        }).WithName("LogoutWebAdmin").Produces(204));
        return endpoints;
    }

    private static RouteHandlerBuilder Errors(RouteHandlerBuilder endpoint)
    {
        foreach (var status in new[] { 400, 401, 403, 413, 415, 422, 429, 503 }) endpoint.Produces<WebAdminError>(status);
        return endpoint;
    }
}
