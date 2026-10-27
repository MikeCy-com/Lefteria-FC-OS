using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using LefteriaFc.Api.Identity;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using Microsoft.Extensions.Options;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    // Pure synthetic cryptographic checks. No host, database, source key or provider.
    private static Task LegacyMobileTokenValidationAsync()
    {
        var now = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
        const string secret = "synthetic literal signing secret π";
        const string header = "{\"alg\":\"HS256\",\"typ\":\"JWT\"}";
        var options = LegacyFixtureOptions(secret, now);
        var validator = new LegacyMobileTokenValidator(Options.Create(options));
        var payload = LegacyFixturePayload(now);
        var token = LegacySign(header, payload.ToJsonString(), Encoding.UTF8.GetBytes(secret));
        var proof = validator.Validate(token, now);
        Check.Equal("legacy-mobile-subject", proof.Subject, "Legacy subject changed.");
        Check.Equal("35799123456", proof.Phone.Digits, "Signed legacy phone did not use canonical full digits.");
        Check.Equal(MobileRole.Parent, proof.Role, "Legacy role was reinterpreted.");
        Check.Equal(now.AddMinutes(-1), proof.IssuedAt, "Legacy issued date changed.");
        Check.Equal(now.AddHours(1), proof.ExpiresAt, "Legacy expiry was extended or clamped.");
        Check.Equal(proof.DeviceSessionId, validator.Validate(token, now).DeviceSessionId,
            "Identical historical no-jti tokens must have identical token-bound provenance.");
        var sessionBytes = proof.DeviceSessionId.ToByteArray(bigEndian: true);
        Check.True((sessionBytes[6] & 0xf0) == 0x80 && (sessionBytes[8] & 0xc0) == 0x80,
            "Legacy session fingerprint is not a version8 UUID with the RFC variant.");
        Check.True(!proof.ToString().Contains(proof.Subject, StringComparison.Ordinal) &&
            !proof.ToString().Contains(proof.Phone.Digits, StringComparison.Ordinal) &&
            !validator.ToString().Contains(secret, StringComparison.Ordinal) &&
            !options.ToString().Contains(secret, StringComparison.Ordinal), "Legacy diagnostic strings expose private values.");

        var current = payload.DeepClone().AsObject(); current["jti"] = Guid.NewGuid().ToString("D");
        var currentToken = LegacySign(header, current.ToJsonString(), Encoding.UTF8.GetBytes(secret));
        Check.True(validator.Validate(currentToken, now).DeviceSessionId != proof.DeviceSessionId,
            "Distinct jti-bearing compact tokens shared a token-bound session.");
        var anotherKey = LegacyFixtureOptions(secret, now);
        Check.True(new LegacyMobileTokenValidator(Options.Create(anotherKey)).Validate(token, now).DeviceSessionId != proof.DeviceSessionId,
            "Fingerprint key did not separate token-bound sessions.");

        foreach (var role in new[] { "parent", "coach", "management", "player", "fan" })
        {
            var claims = payload.DeepClone().AsObject(); claims["role"] = role;
            Check.Equal(role, validator.Validate(LegacySign(header, claims.ToJsonString(), Encoding.UTF8.GetBytes(secret)), now).Role.ToWireValue(),
                "An audited mobile role was rejected.");
        }
        var ninetyDays = payload.DeepClone().AsObject(); ninetyDays["iat"] = now.ToUnixTimeSeconds();
        ninetyDays["exp"] = now.AddDays(90).ToUnixTimeSeconds();
        Check.Equal(now.AddDays(90), validator.Validate(LegacySign(header, ninetyDays.ToJsonString(), Encoding.UTF8.GetBytes(secret)), now).ExpiresAt,
            "Exact90-day audited lifetime was rejected.");

        // Preserve literal UTF-8 meaning, including old short keys and base64-looking text.
        foreach (var literal in new[] { "x", " ", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) })
        {
            var literalOptions = LegacyFixtureOptions(literal, now);
            var literalValidator = new LegacyMobileTokenValidator(Options.Create(literalOptions));
            var literalToken = LegacySign(header, payload.ToJsonString(), Encoding.UTF8.GetBytes(literal));
            Check.Equal(proof.Subject, literalValidator.Validate(literalToken, now).Subject, "Literal source secret was altered or rejected for length.");
            if (literal.Length > 10)
                LegacyExpectFault(literalValidator, LegacySign(header, payload.ToJsonString(), Convert.FromBase64String(literal)), now,
                    MobileAuthErrorCode.LegacyTokenInvalid);
        }

        foreach (var malformed in new string?[] { null, "", ".", "a.b.c", token + ".", " " + token, token + "\n", new('a', 4097) })
            LegacyExpectFault(validator, malformed, now, MobileAuthErrorCode.LegacyTokenInvalid);
        LegacyExpectFault(validator, LegacySign(header, payload.ToJsonString(), RandomNumberGenerator.GetBytes(32)), now, MobileAuthErrorCode.LegacyTokenInvalid);
        var tampered = payload.DeepClone().AsObject(); tampered["exp"] = now.AddHours(2).ToUnixTimeSeconds();
        LegacyExpectFault(validator, token.Split('.')[0] + "." + LegacyEncode(Encoding.UTF8.GetBytes(tampered.ToJsonString())) + "." + token.Split('.')[2],
            now, MobileAuthErrorCode.LegacyTokenInvalid);
        foreach (var malformedUtf8 in new byte[][] { [0xff], [0xc0, 0xaf], [0xed, 0xa0, 0x80], [0xe2, 0x82] })
        {
            var badHeader = LegacyReplaceUtf8(header, "JWT", malformedUtf8);
            var badPayload = LegacyReplaceUtf8(payload.ToJsonString(), "legacy-mobile-subject", malformedUtf8);
            LegacyExpectFault(validator, LegacySignParts(LegacyEncode(badHeader), LegacyEncode(Encoding.UTF8.GetBytes(payload.ToJsonString())),
                Encoding.UTF8.GetBytes(secret)), now, MobileAuthErrorCode.LegacyTokenInvalid);
            LegacyExpectFault(validator, LegacySignParts(LegacyEncode(Encoding.UTF8.GetBytes(header)), LegacyEncode(badPayload),
                Encoding.UTF8.GetBytes(secret)), now, MobileAuthErrorCode.LegacyTokenInvalid);
        }

        foreach (var badHeader in new[]
        {
            "{\"alg\":\"none\",\"typ\":\"JWT\"}", "{\"alg\":\"HS512\",\"typ\":\"JWT\"}",
            "{\"alg\":\"RS256\",\"typ\":\"JWT\"}", "{\"alg\":\"hs256\",\"typ\":\"JWT\"}",
            "{\"alg\":\"HS256\"}", "{\"alg\":\"HS256\",\"typ\":\"jwt\"}",
            "{\"alg\":\"HS256\",\"typ\":\"JWT\",\"alg\":\"HS256\"}",
            "{\"alg\":\"HS256\",\"typ\":\"JWT\",\"a\\u006cg\":\"HS256\"}",
            "{\"alg\":\"HS256\",\"typ\":\"JWT\",\"kid\":\"unused\"}",
            "{\"alg\":\"HS256\",\"typ\":\"JWT\",\"jku\":\"https://example.invalid/key\"}",
            "{\"alg\":\"HS256\",\"typ\":\"JWT\",\"jwk\":{}}",
            "{\"alg\":\"HS256\",\"typ\":\"JWT\",\"crit\":[]}", "[]", "null", "{broken"
        }) LegacyExpectFault(validator, LegacySign(badHeader, payload.ToJsonString(), Encoding.UTF8.GetBytes(secret)), now, MobileAuthErrorCode.LegacyTokenInvalid);

        void InvalidClaims(Action<JsonObject> change)
        {
            var claims = payload.DeepClone().AsObject(); change(claims);
            LegacyExpectFault(validator, LegacySign(header, claims.ToJsonString(), Encoding.UTF8.GetBytes(secret)), now, MobileAuthErrorCode.LegacyTokenInvalid);
        }
        foreach (var extra in new[] { "iss", "aud", "sid", "nbf", "unexpected" }) InvalidClaims(claims => claims[extra] = "unsupported");
        InvalidClaims(claims => claims.Remove("sub")); InvalidClaims(claims => claims["sub"] = null);
        InvalidClaims(claims => claims["sub"] = ""); InvalidClaims(claims => claims["sub"] = " ");
        InvalidClaims(claims => claims["sub"] = new string('s', 129)); InvalidClaims(claims => claims["sub"] = "subject\ncontrol");
        InvalidClaims(claims => claims["sub"] = 7); InvalidClaims(claims => claims["phone"] = "letters99123456");
        InvalidClaims(claims => claims["phone"] = "9912345"); InvalidClaims(claims => claims["phone"] = 99123456);
        InvalidClaims(claims => claims["role"] = "admin"); InvalidClaims(claims => claims["role"] = "Parent");
        InvalidClaims(claims => claims["role"] = new JsonArray("fan")); InvalidClaims(claims => claims["type"] = "access");
        InvalidClaims(claims => claims["type"] = "Mobile_access");
        InvalidClaims(claims => claims["jti"] = null); InvalidClaims(claims => claims["jti"] = Guid.NewGuid().ToString("N"));
        InvalidClaims(claims => claims["jti"] = "not-a-uuid"); InvalidClaims(claims => claims["jti"] = 7);
        InvalidClaims(claims => claims.Remove("iat")); InvalidClaims(claims => claims.Remove("exp"));
        InvalidClaims(claims => claims["iat"] = now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        InvalidClaims(claims => claims["exp"] = null); InvalidClaims(claims => claims["exp"] = true);
        InvalidClaims(claims => claims["iat"] = now.AddSeconds(1).ToUnixTimeSeconds());
        InvalidClaims(claims => claims["exp"] = now.ToUnixTimeSeconds());
        InvalidClaims(claims => claims["exp"] = now.AddSeconds(-1).ToUnixTimeSeconds());
        InvalidClaims(claims => claims["iat"] = now.AddDays(-91).ToUnixTimeSeconds());
        InvalidClaims(claims => claims["iat"] = long.MinValue); InvalidClaims(claims => claims["exp"] = long.MaxValue);
        var integerPayload = payload.ToJsonString();
        var issuedSeconds = now.AddMinutes(-1).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var numeric in new[] { issuedSeconds + ".0", issuedSeconds + "e0", "9223372036854775808" })
        {
            var badNumber = integerPayload.Replace("\"iat\":" + now.AddMinutes(-1).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
                "\"iat\":" + numeric, StringComparison.Ordinal);
            LegacyExpectFault(validator, LegacySign(header, badNumber, Encoding.UTF8.GetBytes(secret)), now, MobileAuthErrorCode.LegacyTokenInvalid);
        }
        var duplicateClaims = integerPayload[..^1] + ",\"sub\":\"second\"}";
        LegacyExpectFault(validator, LegacySign(header, duplicateClaims, Encoding.UTF8.GetBytes(secret)), now, MobileAuthErrorCode.LegacyTokenInvalid);
        LegacyExpectFault(validator, LegacySign(header, "[]", Encoding.UTF8.GetBytes(secret)), now, MobileAuthErrorCode.LegacyTokenInvalid);

        // The low unused signature bits must not create a second compact spelling/sid.
        var pieces = token.Split('.');
        LegacyExpectFault(validator, pieces[0] + "." + pieces[1] + "." + LegacyEncode(new byte[31]), now, MobileAuthErrorCode.LegacyTokenInvalid);
        var noncanonicalSignature = LegacyNoncanonical(pieces[2]);
        Check.True(Convert.FromBase64String(noncanonicalSignature.Replace('-', '+').Replace('_', '/') + "=")
            .SequenceEqual(Convert.FromBase64String(pieces[2].Replace('-', '+').Replace('_', '/') + "=")),
            "Noncanonical base64url regression did not retain the exact signature bytes.");
        LegacyExpectFault(validator, pieces[0] + "." + pieces[1] + "." + noncanonicalSignature, now, MobileAuthErrorCode.LegacyTokenInvalid);
        LegacyExpectFault(validator, token + "=", now, MobileAuthErrorCode.LegacyTokenInvalid);
        var headerAlias = LegacyNoncanonical(LegacyEncode(Encoding.UTF8.GetBytes(header + " ")));
        LegacyExpectFault(validator, LegacySignParts(headerAlias, pieces[1], Encoding.UTF8.GetBytes(secret)), now, MobileAuthErrorCode.LegacyTokenInvalid);

        Check.True(LegacyMobileBridgeOptions.IsValid(new()), "Disabled bridge requires secret settings.");
        LegacyExpectFault(new LegacyMobileTokenValidator(Options.Create(new LegacyMobileBridgeOptions())), token, now, MobileAuthErrorCode.LegacyBridgeUnavailable);
        LegacyExpectFault(validator, null, options.StartsAt!.Value.AddSeconds(-1), MobileAuthErrorCode.LegacyBridgeUnavailable);
        LegacyExpectFault(validator, null, options.EndsAt!.Value, MobileAuthErrorCode.LegacyBridgeUnavailable);
        void InvalidOptions(Action<LegacyMobileBridgeOptions> change)
        {
            var invalid = LegacyFixtureOptions(secret, now); change(invalid);
            Check.True(!LegacyMobileBridgeOptions.IsValid(invalid), "Invalid enabled bridge configuration passed validation.");
            LegacyExpectFault(new LegacyMobileTokenValidator(Options.Create(invalid)), token, now, MobileAuthErrorCode.LegacyBridgeUnavailable);
        }
        InvalidOptions(value => value.SigningSecret = ""); InvalidOptions(value => value.FingerprintKey = "not-base64");
        InvalidOptions(value => value.SigningSecret = new string('s', 4097));
        InvalidOptions(value => value.SigningSecret = new string('π', 2049));
        InvalidOptions(value => value.SigningSecret = "invalid\ud800key");
        InvalidOptions(value => value.FingerprintKey = Convert.ToBase64String(new byte[31]));
        InvalidOptions(value => value.FingerprintKey = Convert.ToBase64String(new byte[129]));
        InvalidOptions(value => value.FingerprintKey += new string(' ', 181));
        InvalidOptions(value => value.FingerprintKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(secret)));
        InvalidOptions(value => value.StartsAt = null); InvalidOptions(value => value.EndsAt = null);
        InvalidOptions(value => value.StartsAt = now.ToOffset(TimeSpan.FromHours(2)));
        InvalidOptions(value => value.EndsAt = value.StartsAt);
        InvalidOptions(value => value.EndsAt = value.StartsAt!.Value.AddDays(90).AddSeconds(1));
        var bounded = LegacyFixtureOptions(new string('π', 2048), now);
        bounded.FingerprintKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(128));
        Check.True(LegacyMobileBridgeOptions.IsValid(bounded), "Exact configured key byte bounds were rejected.");
        options.SigningSecret = "mutated"; options.Enabled = false;
        Check.Equal(proof.DeviceSessionId, validator.Validate(token, now).DeviceSessionId, "Validator configuration changed without restart.");
        return Task.CompletedTask;
    }

    private static LegacyMobileBridgeOptions LegacyFixtureOptions(string secret, DateTimeOffset now) => new()
    {
        Enabled = true, SigningSecret = secret,
        FingerprintKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        StartsAt = now.AddDays(-1), EndsAt = now.AddDays(89)
    };

    private static JsonObject LegacyFixturePayload(DateTimeOffset now) => new()
    {
        ["sub"] = "legacy-mobile-subject", ["phone"] = "+357 99 123456", ["role"] = "parent", ["type"] = "mobile_access",
        ["iat"] = now.AddMinutes(-1).ToUnixTimeSeconds(), ["exp"] = now.AddHours(1).ToUnixTimeSeconds()
    };

    private static string LegacyEncode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] LegacyReplaceUtf8(string json, string marker, byte[] malformed)
    {
        var position = json.IndexOf(marker, StringComparison.Ordinal);
        Check.True(position >= 0, "Malformed UTF-8 fixture marker was absent.");
        var prefix = Encoding.UTF8.GetBytes(json[..position]);
        var suffix = Encoding.UTF8.GetBytes(json[(position + 1)..]);
        return [.. prefix, .. malformed, .. suffix];
    }
    private static string LegacySign(string header, string payload, byte[] key) =>
        LegacySignParts(LegacyEncode(Encoding.UTF8.GetBytes(header)), LegacyEncode(Encoding.UTF8.GetBytes(payload)), key);
    private static string LegacySignParts(string header, string payload, byte[] key)
    {
        var input = header + "." + payload;
        return input + "." + LegacyEncode(HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(input)));
    }
    private static string LegacyNoncanonical(string value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        Check.True(value.Length % 4 is 2 or 3, "Base64url alias fixture requires unused bits.");
        var index = alphabet.IndexOf(value[^1], StringComparison.Ordinal);
        return value[..^1] + alphabet[index + 1];
    }
    private static void LegacyExpectFault(LegacyMobileTokenValidator validator, string? token, DateTimeOffset now, MobileAuthErrorCode code)
    {
        try { validator.Validate(token, now); }
        catch (MobileAuthException error)
        {
            Check.Equal(code, error.Code, "Legacy token failed with an unexpected error code.");
            Check.Equal(code == MobileAuthErrorCode.LegacyTokenInvalid ? MobileAuthFaultKind.Unauthorized : MobileAuthFaultKind.ServiceUnavailable,
                error.Kind, "Legacy token failed with an unexpected HTTP fault kind.");
            Check.Equal(code == MobileAuthErrorCode.LegacyTokenInvalid ? "Legacy mobile token is invalid." : "Legacy mobile bridge is unavailable.",
                error.Detail, "Legacy failure details expose token-specific information.");
            Check.True(error.InnerException is null, "Legacy fault retained private parser exception details.");
            return;
        }
        throw new InvalidOperationException("Invalid legacy token/configuration was accepted.");
    }
}
