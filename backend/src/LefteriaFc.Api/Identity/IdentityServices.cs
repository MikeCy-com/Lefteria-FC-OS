using System.Net;
using System.Security.Cryptography;
using System.Text;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LefteriaFc.Api.Identity;

public static class IdentityServices
{
    public const string Scheme = "Mobile";
    public const string Policy = "MobileIdentity";

    public static IServiceCollection AddMobileIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MobileAuthOptions>().Bind(configuration.GetSection("MobileAuth"))
            .Validate(x => MobileAuthOptions.IsStrongKey(x.SigningKey), "MobileAuth:SigningKey must be base64 encoding at least 32 random bytes.")
            .Validate(x => MobileAuthOptions.IsStrongKey(x.OtpKey), "MobileAuth:OtpKey must be base64 encoding at least 32 random bytes.")
            .Validate(x => !string.IsNullOrWhiteSpace(x.Issuer) && !string.IsNullOrWhiteSpace(x.Audience), "Mobile token issuer and audience are required.")
            .ValidateOnStart();
        services.AddOptions<TwilioSmsOptions>().Bind(configuration.GetSection("Twilio"));
        services.AddOptions<LegacyMobileBridgeOptions>().Bind(configuration.GetSection("LegacyMobileBridge"))
            .Validate(LegacyMobileBridgeOptions.IsValid,"Enabled legacy bridge requires its literal signing secret, independent fingerprint key and bounded UTC acceptance window.")
            .Validate<IOptions<MobileAuthOptions>>((legacy,mobile)=>DistinctLegacyKeys(legacy,mobile.Value),
                "Legacy, fingerprint, rewrite signing and OTP keys must be independent.")
            .ValidateOnStart();
        services.AddSingleton<ILegacyMobileTokenValidator,LegacyMobileTokenValidator>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOtpProtector, HmacOtpProtector>();
        services.AddSingleton<IMobileTokenIssuer, JwtMobileTokenIssuer>();
        services.AddScoped<IAuthThrottle, PostgresAuthThrottle>();
        services.AddScoped<IMobileAuthRepository, PostgresMobileAuthRepository>();
        services.AddScoped<MobileAuthService>();
        services.AddHttpClient<ISmsSender, TwilioSmsSender>(client => client.Timeout = TimeSpan.FromSeconds(10))
            .RemoveAllLoggers();

        services.AddAuthentication().AddJwtBearer(Scheme, _ => { });
        services.AddOptions<JwtBearerOptions>(Scheme).Configure<IOptions<MobileAuthOptions>>((options, configured) =>
        {
            var settings = configured.Value;
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(settings.SigningKey)),
                RequireSignedTokens = true,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                ValidateIssuer = true, ValidIssuer = settings.Issuer,
                ValidateAudience = true, ValidAudience = settings.Audience,
                ValidateLifetime = true, RequireExpirationTime = true, ClockSkew = TimeSpan.Zero
            };
            options.Events = new JwtBearerEvents
            {
                OnChallenge = async context =>
                {
                    context.HandleResponse();
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new MobileAuthError("Not authenticated", "not_authenticated"));
                },
                OnForbidden = async context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new MobileAuthError("Not authorized", "not_authorized"));
                }
            };
        });
        services.AddAuthorization(options => options.AddPolicy(Policy, policy => policy
            .AddAuthenticationSchemes(Scheme).RequireAuthenticatedUser().RequireClaim("type", "mobile_access")));
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            foreach (var value in configuration.GetSection("Http:KnownProxies").Get<string[]>() ?? [])
            {
                if (!IPAddress.TryParse(value, out var address)) throw new InvalidOperationException("Http:KnownProxies must contain explicit IP addresses.");
                options.KnownProxies.Add(address);
            }
        });
        return services;
    }

    private static bool DistinctLegacyKeys(LegacyMobileBridgeOptions legacy,MobileAuthOptions mobile)
    {
        if(!legacy.Enabled)return true;
        if(!MobileAuthOptions.IsStrongKey(legacy.FingerprintKey)||!MobileAuthOptions.IsStrongKey(mobile.SigningKey)||
            !MobileAuthOptions.IsStrongKey(mobile.OtpKey)||legacy.SigningSecret is null)return false;
        var fingerprint=Convert.FromBase64String(legacy.FingerprintKey);
        var source=Encoding.UTF8.GetBytes(legacy.SigningSecret);
        var signing=Convert.FromBase64String(mobile.SigningKey);var otp=Convert.FromBase64String(mobile.OtpKey);
        return !CryptographicOperations.FixedTimeEquals(fingerprint,source)&&
            !CryptographicOperations.FixedTimeEquals(fingerprint,signing)&&!CryptographicOperations.FixedTimeEquals(fingerprint,otp)&&
            !CryptographicOperations.FixedTimeEquals(source,signing)&&!CryptographicOperations.FixedTimeEquals(source,otp)&&
            !CryptographicOperations.FixedTimeEquals(signing,otp);
    }
}
