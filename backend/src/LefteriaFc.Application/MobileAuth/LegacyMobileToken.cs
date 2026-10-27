using LefteriaFc.Domain.Identity;

namespace LefteriaFc.Application.MobileAuth;

// Produced only by the separately configured legacy validator. Never accept
// this shape from JSON, nor use a phone-only fallback for its source subject.
public sealed record LegacyMobileTokenProof(string Subject,PhoneNumber Phone,MobileRole Role,
    Guid DeviceSessionId,DateTimeOffset IssuedAt,DateTimeOffset ExpiresAt)
{
    public override string ToString()=>"Validated legacy mobile token proof";
}

public interface ILegacyMobileTokenValidator
{
    LegacyMobileTokenProof Validate(string? compactToken,DateTimeOffset now);
}
