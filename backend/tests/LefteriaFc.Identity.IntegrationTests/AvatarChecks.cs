using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    private const string AvatarPath = "/api/mobile/profile/avatar";

    private async Task AvatarDeliveryAsync()
    {
        using var client = Client();
        var phone = database.NewPhone();
        var fanId = await database.SeedIdentityAsync(phone, MobileRole.Fan);
        var parentId = await database.SeedIdentityAsync(phone, MobileRole.Parent);
        await SetAvatarUrlAsync(fanId, "/api/uploads/avatars/legacy-original.png");
        await SetAvatarUrlAsync(parentId, "/api/uploads/players/independent-parent.jpg");
        var login = await LoginAsync(client, phone, "fan");
        var audits = await database.CountSuccessfulAuditsAsync(fanId);
        var firstUrl = await UploadAvatarAsync(client, AvatarImage(1600, 800, SKEncodedImageFormat.Png), "image/png");
        Check.True(firstUrl.StartsWith("/api/uploads/avatars/", StringComparison.Ordinal) && MobileAvatarLimits.IsFileName(firstUrl.Split('/')[^1]), "Avatar URL is not an opaque JPEG path.");
        Check.True(!firstUrl.Contains(fanId.ToString(), StringComparison.OrdinalIgnoreCase), "Avatar URL exposed its identity ID.");
        await using (var context = database.CreateContext())
        {
            var stored = await context.MobileAvatars.AsNoTracking().SingleAsync(row => row.IdentityId == fanId);
            Check.True(stored.Width == 1024 && stored.Height == 512 && stored.JpegBytes.Length <= MobileAvatarLimits.OutputBytes, "Avatar output exceeded its dimensions/byte limit.");
            Check.Equal("/api/uploads/avatars/legacy-original.png", stored.PreviousAvatarUrl, "Avatar lost rollback URL provenance.");
            var digest = Convert.ToHexString(SHA256.HashData(stored.JpegBytes)).ToLowerInvariant();
            Check.True(stored.FileName.EndsWith("_" + digest + ".jpg", StringComparison.Ordinal), "Filename did not bind its JPEG digest.");
            var identity = await context.MobileIdentities.AsNoTracking().SingleAsync(row => row.Id == fanId);
            Check.Equal(firstUrl, identity.AvatarUrl, "Image and URL were not committed together.");
            var audit = await context.SecurityAuditEvents.AsNoTracking().SingleAsync(row => row.ActorIdentityId == fanId && row.Action == "mobile.avatar.update");
            Check.Equal("own_avatar", audit.ReasonCode, "Avatar audit reason differs.");
            Check.Equal(Guid.Parse(Claims(login.GetProperty("token").GetString()!).GetProperty("sid").GetString()!), audit.DeviceSessionId, "Avatar audit lost signed device binding.");
            Check.Equal(identity.UpdatedAt, (DateTimeOffset?)audit.OccurredAt, "Image/profile/audit timestamps differ.");
        }
        using var publicClient = Client();
        var jpeg = await PublicAvatarAsync(publicClient, firstUrl, false);
        await PublicAvatarAsync(publicClient, firstUrl, true, jpeg.Length);
        var secondUrl = await UploadAvatarAsync(client, AvatarImage(60, 40, SKEncodedImageFormat.Jpeg), "image/jpeg");
        Check.True(firstUrl != secondUrl, "Replacement reused an immutable URL.");
        using (var old = await publicClient.GetAsync(firstUrl)) Check.Equal(HttpStatusCode.NotFound, old.StatusCode, "Replaced URL stayed readable at origin.");
        await using (var context = database.CreateContext())
        {
            Check.Equal(1, await context.MobileAvatars.CountAsync(row => row.IdentityId == fanId), "Replacement grew storage rows.");
            Check.Equal("/api/uploads/avatars/legacy-original.png", (await context.MobileAvatars.SingleAsync(row => row.IdentityId == fanId)).PreviousAvatarUrl, "Replacement overwrote rollback provenance.");
        }
        Check.Equal(audits + 2, await database.CountSuccessfulAuditsAsync(fanId), "Avatar writes did not append exactly one audit each.");
        Check.Equal("/api/uploads/players/independent-parent.jpg", (await database.IdentityAsync(phone, MobileRole.Parent))!.AvatarUrl, "Fan avatar copied across roles.");
        var switched = await PostAsync(client, "/api/mobile/auth/switch-role", new { role = "parent" }, 200);
        client.DefaultRequestHeaders.Authorization = new("Bearer", switched.GetProperty("token").GetString());
        var parentUrl = await UploadAvatarAsync(client, AvatarImage(25, 20, SKEncodedImageFormat.Webp), "image/webp");
        Check.Equal(secondUrl, (await database.IdentityAsync(phone, MobileRole.Fan))!.AvatarUrl, "Parent write changed fan avatar.");
        Check.Equal(parentUrl, (await GetAsync(client, "/api/mobile/auth/me", 200)).GetProperty("avatar_url").GetString(), "Own profile omitted its avatar.");
        foreach (var path in new[] { "/api/uploads/avatars/invalid.jpg", "/api/uploads/avatars/" + new string('a', 32) + "_" + new string('b', 64) + ".jpg", "/api/uploads/private_attachments/forbidden.jpg" })
        {
            using var missing = await publicClient.GetAsync(path);
            Check.Equal(HttpStatusCode.NotFound, missing.StatusCode, "Avatar delivery expanded to a generic path.");
        }
    }

    private async Task SetAvatarUrlAsync(Guid id, string url)
    {
        await using var context = database.CreateContext();
        await context.MobileIdentities.Where(row => row.Id == id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.AvatarUrl, url));
    }
    private async Task<int> AvatarCountAsync(Guid id)
    {
        await using var context = database.CreateContext();
        return await context.MobileAvatars.CountAsync(row => row.IdentityId == id);
    }
    private static MultipartFormDataContent AvatarForm(byte[] bytes, string contentType)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes); file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", "original-private-filename-do-not-store.img");
        return form;
    }
    private static async Task<HttpResponseMessage> UploadAvatarResponseAsync(HttpClient client, byte[] bytes, string type)
    {
        using var form = AvatarForm(bytes, type);
        return await client.PostAsync(AvatarPath, form);
    }
    private static async Task<string> UploadAvatarAsync(HttpClient client, byte[] bytes, string type)
    {
        using var response = await UploadAvatarResponseAsync(client, bytes, type);
        Check.True(response.Headers.CacheControl?.NoStore == true, "Avatar mutation must be no-store.");
        return (await ExpectAsync(response, 200)).GetProperty("avatar_url").GetString()!;
    }
    private static async Task RejectAvatarAsync(HttpClient client, byte[] bytes, string type, params int[] expected)
    {
        using var response = await UploadAvatarResponseAsync(client, bytes, type);
        Check.True(expected.Contains((int)response.StatusCode), $"Expected avatar rejection; observed HTTP {(int)response.StatusCode}.");
    }
    private static async Task<byte[]> PublicAvatarAsync(HttpClient client, string url, bool head, int? expectedLength = null)
    {
        using var request = new HttpRequestMessage(head ? HttpMethod.Head : HttpMethod.Get, url);
        using var response = await client.SendAsync(request);
        Check.Equal(HttpStatusCode.OK, response.StatusCode, "Public avatar delivery failed.");
        Check.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType, "Avatar MIME differs.");
        Check.True(response.Headers.CacheControl?.Public == true && response.Headers.CacheControl.MaxAge == TimeSpan.FromDays(30) && response.Headers.CacheControl.Extensions.Any(item => item.Name == "immutable"), "Avatar cache contract differs.");
        Check.True(response.Headers.TryGetValues("X-Content-Type-Options", out var sniff) && sniff.Single() == "nosniff", "Avatar omitted nosniff.");
        Check.True(response.Headers.TryGetValues("Content-Security-Policy", out var csp) && csp.Single() == "sandbox", "Avatar omitted sandbox CSP.");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Check.Equal(head ? 0 : (int)response.Content.Headers.ContentLength!, bytes.Length, "Avatar GET/HEAD body length differs.");
        if (expectedLength is not null) Check.Equal((long?)expectedLength, response.Content.Headers.ContentLength, "HEAD lost GET content length.");
        return bytes;
    }
    private static byte[] AvatarImage(int width, int height, SKEncodedImageFormat format, bool transparent = false)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(transparent ? SKColors.Transparent : SKColors.Red);
            if (!transparent)
            {
                using var paint = new SKPaint();
                paint.Color = SKColors.Lime; canvas.DrawRect(width / 2f, 0, width / 2f, height / 2f, paint);
                paint.Color = SKColors.Blue; canvas.DrawRect(0, height / 2f, width / 2f, height / 2f, paint);
                paint.Color = SKColors.Yellow; canvas.DrawRect(width / 2f, height / 2f, width / 2f, height / 2f, paint);
            }
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 100);
        return data.ToArray();
    }
}
