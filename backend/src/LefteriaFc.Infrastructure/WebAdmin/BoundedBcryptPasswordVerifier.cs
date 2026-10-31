using LefteriaFc.Application.WebAdmin;
using System.Text;

namespace LefteriaFc.Infrastructure.WebAdmin;

public sealed class BoundedBcryptPasswordVerifier : IWebAdminPasswordVerifier
{
    // One bounded CPU queue per API process. Login throttles run before this.
    private static readonly SemaphoreSlim Work = new(2, 2);
    // Generated test-only candidate, used only for unknown/unsupported accounts.
    private const string DummyHash = "$2b$12$R9h/cIPz0gi.URNNX3kh2OPST9/PgBkqquzi.Ss7KIUgO2t0jWMUW";

    public async Task<bool> VerifyAsync(string password, string? encodedHash, CancellationToken cancellationToken)
    {
        try { _ = new UTF8Encoding(false, true).GetByteCount(password); }
        catch (EncoderFallbackException) { return false; }
        if (!await Work.WaitAsync(0, cancellationToken))
            throw new WebAdminException(503, "login_busy", "Authentication is busy. Try again shortly.");
        try
        {
            var supported = Supported(encodedHash);
            var matched = await Task.Run(() => Verify(password, supported ? encodedHash! : DummyHash), CancellationToken.None);
            cancellationToken.ThrowIfCancellationRequested();
            return supported && matched;
        }
        finally { Work.Release(); }
    }

    private static bool Verify(string password, string hash)
    {
        try { return BCrypt.Net.BCrypt.Verify(password, hash, enhancedEntropy: false); }
        catch (BCrypt.Net.SaltParseException) { return false; }
        catch (ArgumentException) { return false; }
    }

    private static bool Supported(string? hash) => hash is { Length: 60 } && hash[0] == '$' && hash[1] == '2' &&
        hash[2] is 'a' or 'b' or 'y' && hash[3] == '$' && hash[6] == '$' &&
        int.TryParse(hash.AsSpan(4, 2), out var cost) && cost is >= 4 and <= 14 &&
        hash.AsSpan(7).IndexOfAnyExcept("./ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789") < 0;
}
