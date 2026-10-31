using System.Security.Cryptography;
using System.Text;
using LefteriaFc.Application.WebAdmin;
using Microsoft.Extensions.Options;

namespace LefteriaFc.Infrastructure.WebAdmin;

public sealed class WebAdminSecrets(IOptions<WebAdminOptions> options) : IWebAdminSecrets
{
    public string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public bool IsToken(string? token) => token is { Length: 43 } && token.All(character =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_');
    public string TokenDigest(string token) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token))).ToLowerInvariant();
    public string CredentialDigest(string passwordHash) => Fingerprint("admin-credential", passwordHash);
    public bool SameDigest(string left, string right) => left.Length == 64 && right.Length == 64 &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
    public string Fingerprint(string purpose, string value) => Convert.ToHexString(HMACSHA256.HashData(
        Convert.FromBase64String(options.Value.ThrottleKey), Encoding.UTF8.GetBytes("lefteria-web-admin-v1\n" + purpose + "\n" + value))).ToLowerInvariant();
}
