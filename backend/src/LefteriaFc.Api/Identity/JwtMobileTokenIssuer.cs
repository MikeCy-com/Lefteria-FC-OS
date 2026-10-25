using System.Globalization;
using System.Security.Claims;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Infrastructure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LefteriaFc.Api.Identity;

public sealed class JwtMobileTokenIssuer(IOptions<MobileAuthOptions> options, TimeProvider timeProvider) : IMobileTokenIssuer
{
    public string Issue(MobileSession session)
    {
        var now = timeProvider.GetUtcNow();
        if (session.ExpiresAt.ToUnixTimeSeconds() <= now.ToUnixTimeSeconds())
        {
            throw new MobileSessionAccessException(MobileSessionDenial.Expired);
        }
        var settings = options.Value;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer, Audience = settings.Audience,
            Expires = session.ExpiresAt.UtcDateTime, IssuedAt = now.UtcDateTime, NotBefore = now.UtcDateTime,
            Subject = new ClaimsIdentity([
                new Claim("sub", session.IdentityId.ToString("D", CultureInfo.InvariantCulture)),
                new Claim("role", session.Role.ToWireValue()),
                new Claim("phone", PhoneNumber.Parse(session.NormalizedPhone).WireValue),
                new Claim("type", "mobile_access"),
                new Claim("sid", session.DeviceSessionId.ToString("D", CultureInfo.InvariantCulture)),
                new Claim("jti", Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture))
            ]),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Convert.FromBase64String(settings.SigningKey)), SecurityAlgorithms.HmacSha256)
        });
    }
}
