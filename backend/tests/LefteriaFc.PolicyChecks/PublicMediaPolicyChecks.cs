using LefteriaFc.Application.PublicContent;

internal static class PublicMediaPolicyChecks
{
    public static IEnumerable<(string Name, Action Check)> Checks()
    {
        yield return ("Public media serves only original public image folders", () =>
        {
            Require(PublicMediaPolicy.ContentType("players", "29afab9715b4_Spartera News.png") == "image/png");
            Require(PublicMediaPolicy.ContentType("opponent", "a.jpg") == "image/jpeg");
            foreach (var folder in new[] { "player_docs", "staff_docs", "expenses", "private_attachments", "avatars", "app", "", "Players" })
                Require(PublicMediaPolicy.ContentType(folder, "a.jpg") is null);
        });
        yield return ("Public media rejects traversal, hidden files and active content", () =>
        {
            foreach (var name in new[] { "..", "../a.jpg", "a..jpg", ".hidden.jpg", "a/b.jpg", "a\\b.jpg", "a.svg", "a.html", "a.jpg.exe", "a%2e.jpg", "a\0.jpg" })
                Require(PublicMediaPolicy.ContentType("players", name) is null);
        });
        yield return ("Public media keeps the original immutable cache policy", () =>
            Require(PublicMediaPolicy.CacheControl == "public, max-age=2592000, immutable"));
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Public media policy violated.");
    }
}
