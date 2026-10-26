using System.Security.Cryptography;
using LefteriaFc.Domain.Identity;

namespace LefteriaFc.Application.MobileAuth;

public sealed partial class MobileAuthService
{
    // Cheap preflight before any multipart bytes/native image work. Commit repeats
    // these checks under locks because eligibility can change during decoding.
    public async Task AuthorizeAvatarUploadAsync(MobileSession session, CancellationToken cancellationToken)
        => await AuthorizeCurrentIdentityAsync(session, cancellationToken);

    public async Task AuthorizeCurrentIdentityAsync(MobileSession session, CancellationToken cancellationToken)
    {
        RequireSession(session);
        var phone = PhoneNumber.Parse(session.NormalizedPhone);
        var current = await repository.GetIdentityProfileAsync(session.IdentityId, cancellationToken) ?? throw AccountUnavailable();
        RequireProfileIdentity(current, phone, session.IdentityId, session.Role);
        if (!IsAvailable(current)) throw AccountUnavailable();
    }

    public async Task<MobileAvatarResult> UpdateAvatarAsync(MobileSession session, SanitizedMobileAvatar avatar,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(avatar);
        RequireSession(session);
        if (avatar.JpegBytes.Length is < 4 or > MobileAvatarLimits.OutputBytes ||
            avatar.Width is < 1 or > MobileAvatarLimits.OutputSide || avatar.Height is < 1 or > MobileAvatarLimits.OutputSide ||
            avatar.JpegBytes[0] != 0xff || avatar.JpegBytes[1] != 0xd8 || avatar.JpegBytes[^2] != 0xff || avatar.JpegBytes[^1] != 0xd9)
            throw new MobileAuthException(MobileAuthFaultKind.Validation, MobileAuthErrorCode.InvalidAvatar, "The image could not be processed.");
        var phone = PhoneNumber.Parse(session.NormalizedPhone);
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        var profiles = ValidateProfiles(await transaction.LoadIdentityProfilesForUpdateAsync(phone, cancellationToken), phone);
        RequireSession(session);
        var current = profiles.SingleOrDefault(profile => profile.Id == session.IdentityId) ?? throw AccountUnavailable();
        RequireProfileIdentity(current, phone, session.IdentityId, session.Role);
        if (!IsAvailable(current)) throw AccountUnavailable();
        var fileName = Guid.NewGuid().ToString("N") + "_" + Convert.ToHexString(SHA256.HashData(avatar.JpegBytes)).ToLowerInvariant() + ".jpg";
        var now = timeProvider.GetUtcNow();
        await transaction.UpdateAvatarAsync(current.Id, fileName, avatar, now, cancellationToken);
        await transaction.AppendAuditAsync(new AuthAuditEntry("mobile.avatar.update", "success", now,
            current.Id, session.DeviceSessionId, ReasonCode: "own_avatar"), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(MobileAvatarLimits.Url(fileName));
    }
}
