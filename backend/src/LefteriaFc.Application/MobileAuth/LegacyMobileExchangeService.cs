using LefteriaFc.Domain.Identity;

namespace LefteriaFc.Application.MobileAuth;

public sealed partial class MobileAuthService
{
    public async Task<MobileLoginResult> ExchangeLegacyAsync(ILegacyMobileTokenValidator validator,
        string? compactToken,MobileAuthRequestContext context,CancellationToken token=default)
    {
        await EnforceThrottleAsync(MobileAuthThrottleScope.LegacyExchange,context,token);
        var proof=validator.Validate(compactToken,timeProvider.GetUtcNow());
        await using var transaction=await repository.BeginTransactionAsync(token);
        var target=await transaction.ResolveLegacyMobileSubjectAsync(proof.Subject,token);
        if(target is null)throw InvalidLegacy();
        var profiles=ValidateProfiles(await transaction.LoadIdentityProfilesForUpdateAsync(proof.Phone,token),proof.Phone);
        var current=profiles.SingleOrDefault(profile=>profile.Id==target);
        if(current is null||current.Phone!=proof.Phone||current.Role!=proof.Role||!IsAvailable(current))throw InvalidLegacy();
        // A slow database call must not carry validation across key retirement
        // or the old token's absolute deadline.
        proof=validator.Validate(compactToken,timeProvider.GetUtcNow());
        var session=new MobileSession(proof.DeviceSessionId,current.Id,proof.Phone.Digits,proof.Role,proof.ExpiresAt);
        var signed=tokenIssuer.Issue(session);
        var roles=Available(profiles).Select(profile=>profile.Role.ToWireValue()).ToArray();
        await transaction.AppendAuditAsync(new("mobile.legacy.exchange","success",timeProvider.GetUtcNow(),
            current.Id,session.DeviceSessionId,ReasonCode:"mapped_legacy_token"),token);
        await transaction.CommitAsync(token);
        return new(signed,ToUserDto(current,roles,true),current.Role.ToWireValue(),roles);
    }

    private static MobileAuthException InvalidLegacy()=>new(MobileAuthFaultKind.Unauthorized,
        MobileAuthErrorCode.LegacyTokenInvalid,"Η προηγούμενη σύνδεση δεν είναι διαθέσιμη. Συνδεθείτε με κωδικό SMS.");
}
