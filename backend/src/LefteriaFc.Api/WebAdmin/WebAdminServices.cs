using System.Security.Claims;
using System.Text.Encodings.Web;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Infrastructure.WebAdmin;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Npgsql;

namespace LefteriaFc.Api.WebAdmin;

public static class WebAdminServices
{
    public const string Scheme = "WebAdmin";
    public const string Policy = "WebAdministrator";
    public const string CookieName = "__Host-lefteria-admin";

    public static IServiceCollection AddWebAdmin(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<WebAdminOptions>().Bind(configuration.GetSection("WebAdmin"))
            .Validate(WebAdminOptions.IsValid, "Enabled web administration requires a trusted HTTPS origin, independent key and dedicated database credentials.")
            .Validate(options => Independent(options, configuration), "Web administration must use independent database credentials and keys.")
            .ValidateOnStart();
        services.AddSingleton<IWebAdminSettings, WebAdminSettings>();
        services.AddSingleton<IWebAdminSecrets, WebAdminSecrets>();
        services.AddSingleton<IWebAdminPasswordVerifier, BoundedBcryptPasswordVerifier>();
        services.AddSingleton<WebAdminConnections>();
        services.AddScoped<IWebAdminRepository, PostgresWebAdminRepository>();
        services.AddScoped<WebAdminService>();
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, WebAdminAuthenticationHandler>(Scheme, _ => { });
        services.AddAuthorization(options => options.AddPolicy(Policy, policy => policy
            .AddAuthenticationSchemes(Scheme).RequireAuthenticatedUser().RequireClaim("type", "web_admin")));
        return services;
    }

    private static bool Independent(WebAdminOptions options, IConfiguration configuration)
    {
        if (!options.Enabled) return true;
        try
        {
            var actual = configuration.GetConnectionString("Postgres");
            var mobile = string.IsNullOrWhiteSpace(actual) ? null : new NpgsqlConnectionStringBuilder(actual);
            if (options.Username == (mobile?.Username ?? configuration["Database:Username"]) ||
                options.Password == (mobile?.Password ?? configuration["Database:Password"])) return false;
            var key = Convert.FromBase64String(options.ThrottleKey);
            return new[] { configuration["MobileAuth:SigningKey"], configuration["MobileAuth:OtpKey"] }
                .Where(value => !string.IsNullOrEmpty(value)).All(value => !key.AsSpan().SequenceEqual(Convert.FromBase64String(value!)));
        }
        catch (FormatException) { return false; }
        catch (ArgumentException) { return false; }
    }
}

public sealed class WebAdminAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, WebAdminService service)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var session = await service.CurrentAsync(Request.Cookies[WebAdminServices.CookieName], Context.RequestAborted);
        if (session is null) return AuthenticateResult.NoResult();
        Context.Items[typeof(WebAdminSessionDto)] = session;
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, session.User.Id.ToString()), new Claim(ClaimTypes.Name, session.User.Username),
            new Claim("type", "web_admin")], WebAdminServices.Scheme);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), WebAdminServices.Scheme));
    }
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        return Response.WriteAsJsonAsync(new WebAdminError("Not authenticated.", "not_authenticated"));
    }
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 403;
        return Response.WriteAsJsonAsync(new WebAdminError("Not authorized.", "not_authorized"));
    }
}
