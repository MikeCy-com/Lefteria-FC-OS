using System.Security.Cryptography;
using System.Text;

namespace LefteriaFc.Api.Identity;

// Separate from the rewrite token issuer. Enabling this bounded migration
// window never changes validation for the rewrite's own issuer/audience.
public sealed class LegacyMobileBridgeOptions
{
    public bool Enabled { get; set; }
    public string SigningSecret { get; set; } = "";
    public string FingerprintKey { get; set; } = "";
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }

    public static bool IsValid(LegacyMobileBridgeOptions? options)
    {
        if (options is null) return false;
        if (!options.Enabled) return true;
        if (string.IsNullOrEmpty(options.SigningSecret) || options.SigningSecret.Length > 4096 ||
            options.FingerprintKey is null || options.FingerprintKey.Length > 180 ||
            options.StartsAt is not { Offset: var startOffset } start || startOffset != TimeSpan.Zero ||
            options.EndsAt is not { Offset: var endOffset } end || endOffset != TimeSpan.Zero ||
            end <= start || end - start > TimeSpan.FromDays(90)) return false;
        try
        {
            // PyJWT uses the source secret's literal UTF-8 bytes, including a
            // short secret or a secret which happens to look like base64.
            var encoding = new UTF8Encoding(false, true);
            if (encoding.GetByteCount(options.SigningSecret) > 4096) return false;
            var signing = encoding.GetBytes(options.SigningSecret);
            var fingerprint = Convert.FromBase64String(options.FingerprintKey);
            return fingerprint.Length is >= 32 and <= 128 &&
                !CryptographicOperations.FixedTimeEquals(signing, fingerprint);
        }
        catch (Exception error) when (error is ArgumentException or FormatException)
        {
            return false;
        }
    }

    public override string ToString() => "Protected legacy mobile bridge configuration";
}
