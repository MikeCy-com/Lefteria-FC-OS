using System.Net.Http.Headers;
using System.Text;
using SkiaSharp;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminHttpChecks
{
    private const string OwnPhotoPath="/api/admin/auth/profile/photo";

    private static byte[] FixturePhoto(SKColor color)
    {
        using var bitmap=new SKBitmap(128,128);
        bitmap.Erase(color);
        using var image=SKImage.FromBitmap(bitmap);
        using var encoded=image.Encode(SKEncodedImageFormat.Png,95);
        return encoded.ToArray();
    }

    private static async Task<HttpResponseMessage> UploadFixturePhotoAsync(
        HttpClient client,string cookie,byte[] data)
    {
        using var form=new MultipartFormDataContent();
        using var image=new ByteArrayContent(data);
        image.Headers.ContentType=new MediaTypeHeaderValue("image/png");
        form.Add(image,"file","fixture.png");
        using var request=Mutation(OwnPhotoPath,form);
        request.Headers.Add("Cookie",cookie);
        return await client.SendAsync(request);
    }

    private async Task AdminOwnProfilePhotoAsync()
    {
        var accountA=await database.SeedAsync();
        var accountB=await database.SeedAsync();
        using var clientA=Client();
        using var clientB=Client();
        var loginA=await LoginAsync(clientA,accountA);
        var loginB=await LoginAsync(clientB,accountB);
        Console.WriteLine("CHECK admin-photo stage 1");
        using(var missing=await GetAsync(clientA,OwnPhotoPath,loginA.Cookie))
            await ExpectStatusAsync(missing,404);
        using(var missing=await GetAsync(clientB,OwnPhotoPath,loginB.Cookie))
            await ExpectStatusAsync(missing,404);
        Console.WriteLine("CHECK admin-photo stage 2");
        using(var unauth=await GetAsync(clientB,OwnPhotoPath,""))
            await ExpectStatusAsync(unauth,401);

        Console.WriteLine("CHECK admin-photo stage 3");
        using(var saved=await UploadFixturePhotoAsync(clientA,loginA.Cookie,FixturePhoto(SKColors.Red)))
            await ExpectStatusAsync(saved,204);
        byte[] personalBytes;
        Console.WriteLine("CHECK admin-photo stage 4");
        using(var own=await GetAsync(clientA,OwnPhotoPath,loginA.Cookie))
        {
            await ExpectStatusAsync(own,200);
            Require(own.Content.Headers.ContentType?.MediaType=="image/jpeg",
                "A sanitized admin portrait was not returned as private JPEG.");
            personalBytes=await own.Content.ReadAsByteArrayAsync();
            Require(personalBytes.Length>8&&personalBytes[0]==255&&personalBytes[1]==216,
                "The admin photo was not transcoded to JPEG.");
        }
        using(var cross=await GetAsync(clientB,OwnPhotoPath,loginB.Cookie))
            await ExpectStatusAsync(cross,404);

        Console.WriteLine("CHECK admin-photo stage 5");
        using(var saved=await UploadFixturePhotoAsync(clientB,loginB.Cookie,FixturePhoto(SKColors.Blue)))
            await ExpectStatusAsync(saved,204);
        using(var ownB=await GetAsync(clientB,OwnPhotoPath,loginB.Cookie))
        {
            await ExpectStatusAsync(ownB,200);
            var secondBytes=await ownB.Content.ReadAsByteArrayAsync();
            Require(!secondBytes.AsSpan().SequenceEqual(personalBytes),
                "Separate account photos were not isolated.");
        }
        using(var unchangedA=await GetAsync(clientA,OwnPhotoPath,loginA.Cookie))
        {
            await ExpectStatusAsync(unchangedA,200);
            Require((await unchangedA.Content.ReadAsByteArrayAsync()).AsSpan().SequenceEqual(personalBytes),
                "Another account upload overwrote the first private portrait.");
        }

        Console.WriteLine("CHECK admin-photo stage 6");
        using(var badForm=new MultipartFormDataContent())
        using(var request=Mutation(OwnPhotoPath,badForm))
        {
            request.Headers.Add("Cookie",loginA.Cookie);
            using var denied=await clientA.SendAsync(request);
            await ExpectStatusAsync(denied,422);
        }
        using(var unaffected=await GetAsync(clientA,OwnPhotoPath,loginA.Cookie))
        {
            await ExpectStatusAsync(unaffected,200);
            Require((await unaffected.Content.ReadAsByteArrayAsync()).AsSpan().SequenceEqual(personalBytes),
                "Invalid multipart upload mutated existing photo.");
        }

        Console.WriteLine("CHECK admin-photo stage 7");
        using(var request=new HttpRequestMessage(HttpMethod.Delete,OwnPhotoPath))
        {
            request.Content=new StringContent("{}",Encoding.UTF8,"application/json");
            request.Headers.Add("Origin",WebAdminApplicationFactory.Origin);
            request.Headers.Add("X-Lefteria-Admin","1");
            request.Headers.Add("Cookie",loginA.Cookie);
            using var removed=await clientA.SendAsync(request);
            await ExpectStatusAsync(removed,204);
        }
        using(var gone=await GetAsync(clientA,OwnPhotoPath,loginA.Cookie))
            await ExpectStatusAsync(gone,404);
        using(var stillB=await GetAsync(clientB,OwnPhotoPath,loginB.Cookie))
            await ExpectStatusAsync(stillB,200);

        Console.WriteLine("CHECK admin-photo stage 8");
        await database.ExpireSessionAsync(accountB,loginB.Token);
        using(var expired=await GetAsync(clientB,OwnPhotoPath,loginB.Cookie))
            await ExpectStatusAsync(expired,401);
    }
}
