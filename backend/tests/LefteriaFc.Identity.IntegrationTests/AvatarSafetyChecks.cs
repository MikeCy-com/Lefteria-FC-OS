using System.Buffers.Binary;
using System.Text;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    private async Task AvatarValidationAsync()
    {
        using var client = Client();
        var phone = database.NewPhone();
        var id = await database.SeedIdentityAsync(phone, MobileRole.Fan);
        await LoginAsync(client, phone, "fan");
        var audits = await database.CountSuccessfulAuditsAsync(id);
        var jpeg = AvatarImage(80, 40, SKEncodedImageFormat.Jpeg);
        await RejectAvatarAsync(client, jpeg, "image/png", 400);
        await RejectAvatarAsync(client, Encoding.UTF8.GetBytes("not an image"), "image/jpeg", 400);
        await RejectAvatarAsync(client, [], "image/png", 400);
        await RejectAvatarAsync(client, jpeg[..^2], "image/jpeg", 400);
        var png = AvatarImage(20, 20, SKEncodedImageFormat.Png);
        await RejectAvatarAsync(client, png[..^12], "image/png", 400);
        await RejectAvatarAsync(client, jpeg, "image/svg+xml", 400);
        await RejectAvatarAsync(client, new byte[MobileAvatarLimits.InputBytes + 1], "image/png", 413);
        await RejectAvatarAsync(client, AvatarImage(8193, 1, SKEncodedImageFormat.Png), "image/png", 422);
        await RejectAvatarAsync(client, AvatarImage(4001, 4000, SKEncodedImageFormat.Png), "image/png", 422);
        using (var form = AvatarForm(jpeg, "image/jpeg"))
        {
            form.Add(new StringContent("untrusted"), "identity_id");
            using var response = await client.PostAsync(AvatarPath, form);
            Check.Equal(400, (int)response.StatusCode, "Avatar accepted extra multipart fields.");
        }
        using (var form = AvatarForm(jpeg, "image/jpeg"))
        {
            var duplicate = new ByteArrayContent(jpeg); duplicate.Headers.ContentType = new("image/jpeg");
            form.Add(duplicate, "file", "duplicate.jpg");
            using var response = await client.PostAsync(AvatarPath, form);
            Check.Equal(400, (int)response.StatusCode, "Avatar accepted multiple files.");
        }
        Check.Equal(audits, await database.CountSuccessfulAuditsAsync(id), "Rejected avatar emitted a success audit.");
        Check.Equal(0, await AvatarCountAsync(id), "Rejected input persisted raw bytes.");

        var expectedCorners = new[]
        {
            new[] { SKColors.Red, SKColors.Lime, SKColors.Blue, SKColors.Yellow },
            new[] { SKColors.Lime, SKColors.Red, SKColors.Yellow, SKColors.Blue },
            new[] { SKColors.Yellow, SKColors.Blue, SKColors.Lime, SKColors.Red },
            new[] { SKColors.Blue, SKColors.Yellow, SKColors.Red, SKColors.Lime },
            new[] { SKColors.Red, SKColors.Blue, SKColors.Lime, SKColors.Yellow },
            new[] { SKColors.Blue, SKColors.Red, SKColors.Yellow, SKColors.Lime },
            new[] { SKColors.Yellow, SKColors.Lime, SKColors.Blue, SKColors.Red },
            new[] { SKColors.Lime, SKColors.Yellow, SKColors.Red, SKColors.Blue }
        };
        using var publicClient = Client();
        for (var orientation = 1; orientation <= 8; orientation++)
        {
            var url = await UploadAvatarAsync(client, AddAvatarExif(jpeg, orientation), "image/jpeg");
            var bytes = await PublicAvatarAsync(publicClient, url, false);
            var text = Encoding.Latin1.GetString(bytes);
            Check.True(!text.Contains("private-avatar-metadata-probe", StringComparison.Ordinal) && !text.Contains("Exif", StringComparison.Ordinal), "Original EXIF/comment metadata escaped sanitization.");
            using var bitmap = SKBitmap.Decode(bytes);
            Check.Equal(orientation <= 4 ? 80 : 40, bitmap.Width, "EXIF output width is wrong.");
            Check.Equal(orientation <= 4 ? 40 : 80, bitmap.Height, "EXIF output height is wrong.");
            var positions = new[] { (bitmap.Width / 4, bitmap.Height / 4), (bitmap.Width * 3 / 4, bitmap.Height / 4),
                (bitmap.Width / 4, bitmap.Height * 3 / 4), (bitmap.Width * 3 / 4, bitmap.Height * 3 / 4) };
            for (var corner = 0; corner < positions.Length; corner++)
            {
                var actual = bitmap.GetPixel(positions[corner].Item1, positions[corner].Item2);
                var expected = expectedCorners[orientation - 1][corner];
                Check.True(Math.Abs(actual.Red - expected.Red) < 45 && Math.Abs(actual.Green - expected.Green) < 45 && Math.Abs(actual.Blue - expected.Blue) < 45,
                    "EXIF orientation moved a colored corner incorrectly.");
            }
        }
        var transparent = AvatarImage(10, 10, SKEncodedImageFormat.Png, transparent: true);
        var transparentUrl = await UploadAvatarAsync(client, transparent, "image/png");
        using (var bitmap = SKBitmap.Decode(await PublicAvatarAsync(publicClient, transparentUrl, false)))
        {
            var pixel = bitmap.GetPixel(5, 5);
            Check.True(pixel.Red > 245 && pixel.Green > 245 && pixel.Blue > 245, "Alpha pixels were not flattened white.");
        }
        // Complete two-frame 1x1 GIF89a: first red, second blue. Frame-zero
        // decoding must not enumerate/render the animation into the avatar.
        byte[] gif = [71,73,70,56,57,97,1,0,1,0,128,0,0,255,0,0,0,0,255,
            33,249,4,0,1,0,0,0,44,0,0,0,0,1,0,1,0,0,2,2,68,1,0,
            33,249,4,0,1,0,0,0,44,0,0,0,0,1,0,1,0,0,2,2,76,1,0,59];
        var gifUrl = await UploadAvatarAsync(client, gif, "image/gif");
        using (var bitmap = SKBitmap.Decode(await PublicAvatarAsync(publicClient, gifUrl, false)))
        {
            var pixel = bitmap.GetPixel(0, 0);
            Check.True(pixel.Red > 210 && pixel.Blue < 45, "Animated GIF did not retain only its first frame.");
        }
        await RejectAvatarAsync(client, gif[..^1], "image/gif", 400);
    }

    private static byte[] AddAvatarExif(byte[] jpeg, int orientation)
    {
        byte[] exif = [69,120,105,102,0,0,73,73,42,0,8,0,0,0,1,0,18,1,3,0,1,0,0,0,(byte)orientation,0,0,0,0,0,0,0];
        var comment = Encoding.UTF8.GetBytes("private-avatar-metadata-probe");
        using var memory = new MemoryStream();
        memory.Write(jpeg.AsSpan(0, 2));
        var length = new byte[2];
        foreach (var (marker, payload) in new[] { ((byte)0xe1, exif), ((byte)0xfe, comment) })
        {
            memory.WriteByte(0xff); memory.WriteByte(marker);
            BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)(payload.Length + 2));
            memory.Write(length); memory.Write(payload);
        }
        memory.Write(jpeg.AsSpan(2));
        return memory.ToArray();
    }
}
