using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Infrastructure.WebAdmin;

namespace LefteriaFc.Identity.IntegrationTests;

internal static class WebAdminPasswordChecks
{
    // Frozen fake vectors generated with pyca/bcrypt==4.1.3, the version pinned
    // by original GitHub main 380b402 backend/requirements.txt. No real account
    // data or live service is used. For each case and revision 2a/2b/2y:
    // bcrypt.hashpw(password.encode("utf-8"),
    //     ("$" + revision + "$04$" + "." * 22).encode("ascii"))
    // Each revision was generated and checked independently; its suffix agrees.
    // Fixed public salts and cost04 are solely for deterministic, fast fixtures.
    private static readonly (string Name, string Password, string Suffix, string Different, string? Equivalent)[] Vectors =
    [
        ("ASCII", "fixture-password-26", "......................zZBj5OGxtMOVwSKwuhgTqOM4l8/c6/O",
            "Fixture-password-26", null),
        ("Greek and emoji", "Λευτεριά⚽️🙂-fixture", ".......................Z5L0L9S5lx.ea44SEj66FC0aCGr.OC",
            "Λευτεριά⚽️🙃-fixture", null),
        ("Leading and trailing spaces", "  fixture password  ", "......................jisM.i3.PCwc.shsoBrSJD4AMnC8.Ju",
            "fixture password", null),
        ("ASCII truncation at72 bytes", new string('A', 72) + "Z", ".......................KyxKLMd5vyP1icw21wqSvz/XeB9gba",
            new string('A', 71) + "B", new string('A', 72) + "different suffix"),
        ("Multibyte truncation inside emoji", new string('x', 71) + "🙂tail", "......................999CSajH.9qjPcCkY/C/lHwlHRH114u",
            new string('x', 71) + "α", new string('x', 71) + "🙃different suffix"),
        ("Embedded NUL", "fixture\0password", "......................fIXBRdhqkyAQnzXXppUB./CKpW2nbAG",
            "fixture", null)
    ];

    public static async Task RunAsync()
    {
        var verifier = new BoundedBcryptPasswordVerifier();
        var verified = 0;
        foreach (var revision in new[] { "2a", "2b", "2y" })
        {
            foreach (var vector in Vectors)
            {
                var hash = "$" + revision + "$04$" + vector.Suffix;
                Check.True(await BoundedAsync(verifier, vector.Password, hash),
                    "Python bcrypt4.1.3 compatibility failed: " + vector.Name + " / " + revision + ".");
                Check.True(!await BoundedAsync(verifier, vector.Different, hash),
                    "Python bcrypt4.1.3 incorrect password was accepted: " + vector.Name + " / " + revision + ".");
                if (vector.Equivalent is not null)
                    Check.True(await BoundedAsync(verifier, vector.Equivalent, hash),
                        "Legacy byte truncation compatibility failed: " + vector.Name + " / " + revision + ".");
                verified++;
            }
        }
        Console.WriteLine($"PASS {verified} frozen Python bcrypt4.1.3 vectors, wrong candidates and byte truncation boundaries.");

        await RejectedHashesAsync(verifier);
        Console.WriteLine("PASS malformed, unsupported and excessive-cost bcrypt hashes fail closed within a bounded wait.");
        await ConcurrencyAsync(verifier);
        Console.WriteLine("PASS bcrypt concurrency returns controlled503 login_busy without growing a CPU queue.");
    }

    private static async Task RejectedHashesAsync(BoundedBcryptPasswordVerifier verifier)
    {
        var suffix = Vectors[0].Suffix;
        foreach (var hash in new string?[]
        {
            null, "", "not-a-bcrypt-hash", "$2b$04$short", "$2b$03$" + suffix,
            "$2x$04$" + suffix, "$2b$15$" + suffix, "$2b$31$" + suffix,
            "$2b$04$" + suffix[..^1] + "!", "$2b$04$" + suffix + "\n"
        })
            Check.True(!await BoundedAsync(verifier, Vectors[0].Password, hash),
                "Malformed, unsupported or excessive-cost bcrypt hash was accepted.");

        // Original Python UTF-8 encoding rejects unpaired surrogate code units;
        // the verifier must fail closed rather than normalize them into a key.
        Check.True(!await BoundedAsync(verifier, "\ud800", "$2b$04$" + suffix),
            "Invalid UTF-16 input was silently normalized during password verification.");
    }

    private static async Task ConcurrencyAsync(BoundedBcryptPasswordVerifier verifier)
    {
        // A valid cost14 encoding keeps both CPU slots occupied while the third
        // request checks immediate admission. The checksum need not match.
        var workHash = "$2b$14$" + Vectors[0].Suffix;
        var first = verifier.VerifyAsync("bounded-work-fixture", workHash, CancellationToken.None);
        var second = verifier.VerifyAsync("bounded-work-fixture", workHash, CancellationToken.None);
        try
        {
            try
            {
                await verifier.VerifyAsync("bounded-work-fixture", workHash, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15));
                throw new InvalidOperationException("A third concurrent bcrypt job was admitted.");
            }
            catch (WebAdminException error)
            {
                Check.Equal(503, error.Status, "Concurrent bcrypt work returned an unexpected status.");
                Check.Equal("login_busy", error.Code, "Concurrent bcrypt work returned an unexpected fault code.");
            }
        }
        finally { await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30)); }
    }

    private static Task<bool> BoundedAsync(BoundedBcryptPasswordVerifier verifier, string password, string? hash) =>
        verifier.VerifyAsync(password, hash, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15));
}
