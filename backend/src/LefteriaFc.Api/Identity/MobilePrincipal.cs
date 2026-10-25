using System.Globalization;
using System.Security.Claims;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;

namespace LefteriaFc.Api.Identity;

public static class MobilePrincipal
{
    public static MobileSession Read(ClaimsPrincipal principal)
    {
        if (principal.FindFirstValue("type") != "mobile_access" ||
            !Guid.TryParse(principal.FindFirstValue("sub"), out var id) ||
            !Guid.TryParse(principal.FindFirstValue("sid"), out var sessionId) ||
            !MobileRoles.TryParse(principal.FindFirstValue("role"), out var role) ||
            !PhoneNumber.TryParse(principal.FindFirstValue("phone"), out var phone) ||
            !long.TryParse(principal.FindFirstValue("exp"), NumberStyles.None, CultureInfo.InvariantCulture, out var expires))
        {
            throw Invalid();
        }
        try { return new(sessionId, id, phone.Digits, role, DateTimeOffset.FromUnixTimeSeconds(expires)); }
        catch (ArgumentOutOfRangeException) { throw Invalid(); }
    }

    private static MobileAuthException Invalid() => new(MobileAuthFaultKind.Unauthorized,
        MobileAuthErrorCode.SessionInvalid, "Invalid token");
}
