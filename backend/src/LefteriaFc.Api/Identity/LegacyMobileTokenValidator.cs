using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using Microsoft.Extensions.Options;

namespace LefteriaFc.Api.Identity;

// Supports only the audited source PyJWT mobile_access claim families. This
// validator has no provider, database, URL discovery or raw-token persistence.
public sealed class LegacyMobileTokenValidator : ILegacyMobileTokenValidator
{
    private const int MaximumTokenLength = 4096;
    private static readonly TimeSpan MaximumLifetime = TimeSpan.FromDays(90);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly string[] PayloadFields = ["sub", "phone", "role", "type", "iat", "exp", "jti"];
    private const string FingerprintNamespace = "lefteria-fc-os:legacy-mobile-token-session:v1\0";
    private readonly bool configured;
    private readonly DateTimeOffset startsAt;
    private readonly DateTimeOffset endsAt;
    private readonly byte[] signingKey = [];
    private readonly byte[] fingerprintKey = [];

    public LegacyMobileTokenValidator(IOptions<LegacyMobileBridgeOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        // IOptions is restart-configured; keep a private immutable snapshot.
        var value = options.Value;
        var snapshot = new LegacyMobileBridgeOptions
        {
            Enabled = value.Enabled, SigningSecret = value.SigningSecret,
            FingerprintKey = value.FingerprintKey, StartsAt = value.StartsAt, EndsAt = value.EndsAt
        };
        configured = snapshot.Enabled && LegacyMobileBridgeOptions.IsValid(snapshot);
        if (!configured) return;
        startsAt = snapshot.StartsAt!.Value;
        endsAt = snapshot.EndsAt!.Value;
        signingKey = new UTF8Encoding(false, true).GetBytes(snapshot.SigningSecret);
        fingerprintKey = Convert.FromBase64String(snapshot.FingerprintKey);
    }

    public LegacyMobileTokenProof Validate(string? compactToken, DateTimeOffset now)
    {
        if (!configured || now < startsAt || now >= endsAt) throw Unavailable();
        try
        {
            if (string.IsNullOrEmpty(compactToken) || compactToken.Length > MaximumTokenLength) throw Invalid();
            var parts = compactToken.Split('.');
            if (parts.Length != 3) throw Invalid();
            var headerBytes = DecodeCanonical(parts[0]);
            var payloadBytes = DecodeCanonical(parts[1]);
            var signature = DecodeCanonical(parts[2]);
            if (signature.Length != 32) throw Invalid();
            var signingInput = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
            var expected = HMACSHA256.HashData(signingKey, signingInput);
            if (!CryptographicOperations.FixedTimeEquals(expected, signature)) throw Invalid();

            using var header = ParseObject(headerBytes);
            if (header.RootElement.EnumerateObject().Any(property => property.Name is not ("alg" or "typ")) ||
                RequiredString(header.RootElement, "alg") != "HS256" ||
                RequiredString(header.RootElement, "typ") != "JWT") throw Invalid();

            using var payload = ParseObject(payloadBytes);
            var claims = payload.RootElement;
            if (claims.EnumerateObject().Any(property => !PayloadFields.Contains(property.Name, StringComparer.Ordinal))) throw Invalid();
            var subject = RequiredString(claims, "sub");
            if (subject.Length is < 1 or > 128 || string.IsNullOrWhiteSpace(subject) || subject.Any(char.IsControl)) throw Invalid();
            var phone = PhoneNumber.Parse(RequiredString(claims, "phone"));
            if (!MobileRoles.TryParse(RequiredString(claims, "role"), out var role) ||
                RequiredString(claims, "type") != "mobile_access") throw Invalid();
            var issuedAt = NumericDate(claims, "iat");
            var expiresAt = NumericDate(claims, "exp");
            if (issuedAt > now || expiresAt <= now || expiresAt <= issuedAt || expiresAt - issuedAt > MaximumLifetime) throw Invalid();
            if (claims.TryGetProperty("jti", out var tokenId) &&
                (tokenId.ValueKind != JsonValueKind.String || tokenId.GetString() is not { Length: 36 } jti ||
                 !Guid.TryParseExact(jti, "D", out _))) throw Invalid();

            // This is token-bound provenance, not a reconstructed device ID.
            // Identical old no-jti tokens necessarily map to the same session.
            var fingerprint = HMACSHA256.HashData(fingerprintKey,
                Encoding.ASCII.GetBytes(FingerprintNamespace + compactToken));
            fingerprint[6] = (byte)((fingerprint[6] & 0x0f) | 0x80);
            fingerprint[8] = (byte)((fingerprint[8] & 0x3f) | 0x80);
            var sessionId = new Guid(fingerprint.AsSpan(0, 16), bigEndian: true);
            return new(subject, phone, role, sessionId, issuedAt, expiresAt);
        }
        catch (Exception error) when (error is JsonException or FormatException or ArgumentException or
            InvalidOperationException or KeyNotFoundException or OverflowException)
        {
            throw Invalid();
        }
    }

    private static byte[] DecodeCanonical(string encoded)
    {
        if (encoded.Length == 0 || encoded.Length % 4 == 1 || encoded.Any(character =>
                character is not (>= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')))
            throw Invalid();
        var padded = encoded.Replace('-', '+').Replace('_', '/');
        padded += (encoded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        var bytes = Convert.FromBase64String(padded);
        var canonical = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        if (!string.Equals(canonical, encoded, StringComparison.Ordinal)) throw Invalid();
        return bytes;
    }

    private static JsonDocument ParseObject(byte[] bytes)
    {
        // Validate the signed octets, not a decoder's replacement characters.
        _ = StrictUtf8.GetCharCount(bytes);
        var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        try
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw Invalid();
            RejectDuplicateKeys(document.RootElement);
            return document;
        }
        catch { document.Dispose(); throw; }
    }

    private static void RejectDuplicateKeys(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw Invalid();
                RejectDuplicateKeys(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RejectDuplicateKeys(item);
    }

    private static string RequiredString(JsonElement claims, string key)
    {
        var value = claims.GetProperty(key);
        return value.ValueKind == JsonValueKind.String ? value.GetString()! : throw Invalid();
    }

    private static DateTimeOffset NumericDate(JsonElement claims, string key)
    {
        var value = claims.GetProperty(key);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var seconds) ||
            value.GetRawText().Any(character => character is not (>= '0' and <= '9' or '-'))) throw Invalid();
        return DateTimeOffset.FromUnixTimeSeconds(seconds);
    }

    private static MobileAuthException Invalid() => new(MobileAuthFaultKind.Unauthorized,
        MobileAuthErrorCode.LegacyTokenInvalid, "Legacy mobile token is invalid.");
    private static MobileAuthException Unavailable() => new(MobileAuthFaultKind.ServiceUnavailable,
        MobileAuthErrorCode.LegacyBridgeUnavailable, "Legacy mobile bridge is unavailable.");
    public override string ToString() => "Strict legacy mobile token validator";
}
