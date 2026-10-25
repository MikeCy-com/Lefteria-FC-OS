using System.Security.Cryptography;

namespace LefteriaFc.Domain.Identity;

public enum OtpVerificationOutcome
{
    Accepted,
    InvalidCode,
    NotYetValid,
    Expired,
    TooManyAttempts,
    AlreadyConsumed
}

// Persistence must lock the challenge row while calling Verify and saving it.
// The digest is supplied by a keyed hashing adapter; raw OTPs never belong here.
public sealed class OtpChallenge
{
    public const int MaximumVerificationCalls = 5;
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(5);
    private readonly byte[] codeDigest;

    public OtpChallenge(Guid id, byte[] codeDigest, DateTimeOffset createdAt, DateTimeOffset expiresAt,
        int verificationCalls = 0, bool isConsumed = false)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A challenge ID is required.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(codeDigest);
        if (codeDigest.Length != SHA256.HashSizeInBytes)
        {
            throw new ArgumentException("A SHA-256 digest is required.", nameof(codeDigest));
        }

        if (expiresAt <= createdAt || expiresAt - createdAt > MaximumLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        }

        if (verificationCalls is < 0 or > MaximumVerificationCalls)
        {
            throw new ArgumentOutOfRangeException(nameof(verificationCalls));
        }

        Id = id;
        this.codeDigest = (byte[])codeDigest.Clone();
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        VerificationCalls = verificationCalls;
        IsConsumed = isConsumed;
    }

    public Guid Id { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset ExpiresAt { get; }
    public int VerificationCalls { get; private set; }
    public bool IsConsumed { get; private set; }

    public OtpVerificationOutcome Verify(
        ReadOnlySpan<byte> candidateDigest,
        DateTimeOffset now,
        bool consumeOnSuccess)
    {
        if (IsConsumed)
        {
            return OtpVerificationOutcome.AlreadyConsumed;
        }

        if (now >= ExpiresAt)
        {
            return OtpVerificationOutcome.Expired;
        }

        if (now < CreatedAt)
        {
            return OtpVerificationOutcome.NotYetValid;
        }

        if (VerificationCalls >= MaximumVerificationCalls)
        {
            return OtpVerificationOutcome.TooManyAttempts;
        }

        // Preserve the legacy five-call allowance, including the first role-picker call.
        VerificationCalls++;
        if (!CryptographicOperations.FixedTimeEquals(codeDigest, candidateDigest))
        {
            return OtpVerificationOutcome.InvalidCode;
        }

        IsConsumed = consumeOnSuccess;
        return OtpVerificationOutcome.Accepted;
    }
}
